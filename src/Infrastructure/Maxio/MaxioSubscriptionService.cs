using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implementation of <see cref="IMaxioSubscriptionService"/> that talks to the Maxio Advanced
/// Billing REST API over HTTPS. All endpoints are read from the Maxio documentation and verified
/// against the sandbox site; no values are hard-coded (site, subdomain and catalog are configurable).
/// </summary>
public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private const string InvoiceCollectionMethod = "invoice";
    private const int PageSize = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _http;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionService(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _http = httpClient;
        var configured = options.Value;
        configured.EnsureConfigured();
        _productFamilyHandle = configured.ProductFamilyHandle;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ProductsInFamilyUrl(_productFamilyHandle));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var items = await ReadListAsync<ProductItemEnvelope>(response, cancellationToken);
        return items
            .Where(e => e.Product is not null)
            .Select(e => ToPlan(e.Product!))
            .ToList();
    }

    public async Task<MaxioCustomer> GetOrCreateCustomerAsync(string reference, string email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(reference));
        }

        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var (firstName, lastName) = SplitNameFromEmail(email);
            var payload = new CreateCustomerPayload
            {
                Customer = new CustomerDto
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = reference
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "/customers.json")
            {
                Content = JsonContent(payload)
            };
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // A concurrent request may have created the customer between our lookup and our create
                // (Maxio allows only one customer per reference). Re-check before surfacing the error.
                var raced = await FindCustomerByReferenceAsync(reference, cancellationToken);
                if (raced is not null)
                {
                    return raced;
                }
            }

            await EnsureSuccessAsync(response, cancellationToken);
            var envelope = await ReadEnvelopeAsync<CustomerEnvelope>(response, cancellationToken);
            if (envelope?.Customer is null)
            {
                throw new MaxioApiException((int)response.StatusCode, new[] { "Maxio returned an unexpected response while creating the customer." });
            }

            return ToCustomer(envelope.Customer);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            var raced = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(string reference, CancellationToken cancellationToken)
    {
        var customer = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
    }

    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(string reference, string email, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var customer = await GetOrCreateCustomerAsync(reference, email, cancellationToken);
        var currentSubscriptions = await ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);

        var existing = currentSubscriptions.FirstOrDefault(s =>
            string.Equals(s.PlanHandle, plan.Handle, StringComparison.Ordinal));
        if (existing is not null)
        {
            return new SubscriptionEnrollmentResult(existing, created: false);
        }

        var token = $"subscribe:{customer.Reference}:{plan.Handle}";
        try
        {
            var created = await CreateSubscriptionAsync(customer, plan, token, cancellationToken);
            return new SubscriptionEnrollmentResult(created, created: true);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.Conflict)
        {
            // DuplicateSubmissionError: a racing request already submitted the subscription with the same
            // uniqueness token. Reconcile by listing; if it is not visible yet, retry once with a fresh token.
            var reconciled = await ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
            var winner = reconciled.FirstOrDefault(s =>
                string.Equals(s.PlanHandle, plan.Handle, StringComparison.Ordinal));
            if (winner is not null)
            {
                return new SubscriptionEnrollmentResult(winner, created: false);
            }

            var retried = await CreateSubscriptionAsync(customer, plan, $"{token}:{Guid.NewGuid():N}", cancellationToken);
            return new SubscriptionEnrollmentResult(retried, created: true);
        }
    }

    private async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCustomer customer, MaxioPlan plan, string uniquenessToken, CancellationToken cancellationToken)
    {
        if (plan.RequiresCreditCard)
        {
            throw new MaxioApiException(
                (int)HttpStatusCode.UnprocessableEntity,
                new[] { $"The '{plan.Name}' plan requires a payment method, which this storefront does not collect. A different plan may not require one." });
        }

        var payload = new CreateSubscriptionPayload
        {
            Subscription = new CreateSubscriptionDto
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = plan.RequiresCreditCard ? null : InvoiceCollectionMethod
            },
            UniquenessToken = uniquenessToken
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/subscriptions.json")
        {
            Content = JsonContent(payload)
        };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await ReadEnvelopeAsync<SubscriptionEnvelope>(response, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException((int)response.StatusCode, new[] { "Maxio returned an unexpected response while creating the subscription." });
        }

        return ToSubscription(envelope.Subscription);
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var uri = "/customers/lookup.json?reference=" + Uri.EscapeDataString(reference);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadEnvelopeAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer is null ? null : ToCustomer(envelope.Customer);
    }

    private async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(long customerId, CancellationToken cancellationToken)
    {
        var results = new List<MaxioSubscription>();
        var page = 1;

        while (true)
        {
            var uri = $"/customers/{customerId}/subscriptions.json?per_page={PageSize}&page={page}";
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var items = await ReadListAsync<SubscriptionItemEnvelope>(response, cancellationToken);
            var batch = items.Where(e => e.Subscription is not null)
                .Select(e => ToSubscription(e.Subscription!))
                .Where(s => s.IsCurrent)
                .ToList();

            results.AddRange(batch);
            if (items.Count < PageSize)
            {
                break;
            }

            page++;
        }

        return results;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            body = string.Empty;
        }

        var errors = TryReadErrors(body);
        if (errors.Count == 0)
        {
            errors.Add(string.IsNullOrWhiteSpace(body)
                ? $"Maxio API request failed with status {(int)response.StatusCode} {response.ReasonPhrase}."
                : body);
        }

        throw new MaxioApiException((int)response.StatusCode, errors);
    }

    private static List<string> TryReadErrors(string body)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return messages;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                switch (errors.ValueKind)
                {
                    case JsonValueKind.Array:
                        foreach (var item in errors.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                messages.Add(item.GetString()!);
                            }
                            else
                            {
                                messages.Add(item.GetRawText());
                            }
                        }

                        break;
                    case JsonValueKind.Object:
                        foreach (var property in errors.EnumerateObject())
                        {
                            messages.Add($"{property.Name}: {property.Value.GetRawText()}");
                        }

                        break;
                    case JsonValueKind.String:
                        messages.Add(errors.GetString()!);
                        break;
                }
            }
            else if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                messages.Add(error.GetString()!);
            }
        }
        catch (JsonException)
        {
            // Fall back to the raw body below.
        }

        return messages;
    }

    private static StringContent JsonContent<T>(T payload) =>
        new(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

    private static async Task<IReadOnlyList<T>> ReadListAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>();
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException((int)response.StatusCode, new[] { $"Maxio returned an unreadable response: {ex.Message}" });
        }
    }

    private static async Task<T?> ReadEnvelopeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : class
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException((int)response.StatusCode, new[] { $"Maxio returned an unreadable response: {ex.Message}" });
        }
    }

    private static string ProductsInFamilyUrl(string familyHandle) =>
        "/product_families/handle:" + Uri.EscapeDataString(familyHandle) + "/products.json";

    private static (string FirstName, string LastName) SplitNameFromEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0)
        {
            return (email, "Shopper");
        }

        var local = email[..at];
        var domain = email[(at + 1)..];
        return (local, string.IsNullOrWhiteSpace(domain) ? "Shopper" : domain);
    }

    private static MaxioPlan ToPlan(ProductDto product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month",
        RequiresCreditCard = product.RequireCreditCard,
        Taxable = product.Taxable,
        ProductPricePointName = product.ProductPricePointName,
        ProductPricePointHandle = product.ProductPricePointHandle
    };

    private static MaxioCustomer ToCustomer(CustomerDto customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference ?? string.Empty,
        Email = customer.Email ?? string.Empty,
        FirstName = customer.FirstName,
        LastName = customer.LastName
    };

    private static MaxioSubscription ToSubscription(SubscriptionDto subscription)
    {
        var product = subscription.Product;
        var priceInCents = subscription.ProductPriceInCents ?? product?.PriceInCents ?? 0;
        var interval = product?.Interval ?? 1;
        var intervalUnit = product?.IntervalUnit ?? "month";

        return new MaxioSubscription
        {
            Id = subscription.Id,
            State = subscription.State ?? "unknown",
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? string.Empty,
            PriceInCents = priceInCents,
            Interval = interval,
            IntervalUnit = intervalUnit,
            Currency = subscription.Currency,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            CustomerId = subscription.Customer?.Id ?? 0
        };
    }

    // ---------------------------------------------------------------- wire DTOs

    private sealed class ProductItemEnvelope
    {
        public ProductDto? Product { get; set; }
    }

    private sealed class CustomerEnvelope
    {
        public CustomerDto? Customer { get; set; }
    }

    private sealed class SubscriptionEnvelope
    {
        public SubscriptionDto? Subscription { get; set; }
    }

    private sealed class SubscriptionItemEnvelope
    {
        public SubscriptionDto? Subscription { get; set; }
    }

    private sealed class CreateCustomerPayload
    {
        public CustomerDto? Customer { get; set; }
    }

    private sealed class CreateSubscriptionPayload
    {
        public CreateSubscriptionDto? Subscription { get; set; }
        public string? UniquenessToken { get; set; }
    }

    private sealed class CreateSubscriptionDto
    {
        public string? ProductHandle { get; set; }
        public long? CustomerId { get; set; }
        public string? PaymentCollectionMethod { get; set; }
    }

    private sealed class ProductDto
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public string? Handle { get; set; }
        public string? Description { get; set; }
        public long PriceInCents { get; set; }
        public int Interval { get; set; }
        public string? IntervalUnit { get; set; }
        public bool RequireCreditCard { get; set; }
        public bool Taxable { get; set; }
        public string? ProductPricePointName { get; set; }
        public string? ProductPricePointHandle { get; set; }
    }

    private sealed class CustomerDto
    {
        public long Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Reference { get; set; }
    }

    private sealed class SubscriptionDto
    {
        public long Id { get; set; }
        public string? State { get; set; }
        public string? Currency { get; set; }
        public string? PaymentCollectionMethod { get; set; }
        public long? ProductPriceInCents { get; set; }
        public bool CancelAtEndOfPeriod { get; set; }
        public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
        public DateTimeOffset? NextAssessmentAt { get; set; }
        public DateTimeOffset? ActivatedAt { get; set; }
        public DateTimeOffset? CanceledAt { get; set; }
        public ProductDto? Product { get; set; }
        public CustomerDto? Customer { get; set; }
    }
}
