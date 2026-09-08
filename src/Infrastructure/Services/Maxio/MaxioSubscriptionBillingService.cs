using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// Subscriptions are enrolled with remittance (invoice) collection because the storefront does
    /// not capture payment details. This lets a subscription exist without a card on file; the
    /// recurring amount is invoiced and collected out of band.
    /// </summary>
    public const string SubscriptionPaymentCollectionMethod = "remittance";

    private const string OrganizationName = "eShopOnWeb";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CustomerLocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        HttpClient httpClient,
        MaxioOptions options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(FamilyHandle)}/products.json";
        var envelopes = await SendGetAsync<List<ProductEnvelope>>(path, HttpStatusCode.OK, cancellationToken)
            ?? new List<ProductEnvelope>();

        var plans = envelopes
            .Where(e => e.Product is not null && e.Product!.ArchivedAt is null && !string.IsNullOrWhiteSpace(e.Product.Handle))
            .Select(e => MapProduct(e.Product!))
            .OrderBy(p => p.Price)
            .ThenBy(p => p.Id)
            .ToList();

        return plans;
    }

    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(
        string customerReference,
        string customerEmail,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        GuardCustomerArguments(customerReference, customerEmail);

        var plan = await FindPlanAsync(planHandle, cancellationToken)
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var customer = await EnsureCustomerAsync(customerReference, customerEmail, cancellationToken);

        var gate = CustomerLocks.GetOrAdd(customerReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await FindCurrentSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("User {CustomerReference} already subscribed to plan {PlanHandle} (subscription {SubscriptionId}); returning existing subscription.",
                    customerReference, plan.Handle, existing.SubscriptionId);
                return new SubscriptionEnrollmentResult { Subscription = existing, WasCreated = false };
            }

            var subscription = await CreateSubscriptionAsync(customer, plan, cancellationToken);
            _logger.LogInformation("Created subscription {SubscriptionId} for user {CustomerReference} on plan {PlanHandle}.",
                subscription.SubscriptionId, customerReference, plan.Handle);
            return new SubscriptionEnrollmentResult { Subscription = subscription, WasCreated = true };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken = default)
    {
        GuardCustomerArguments(customerReference, string.Empty);

        var customer = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var path = $"customers/{customer.Id}/subscriptions.json";
        var envelopes = await SendGetAsync<List<SubscriptionEnvelope>>(path, HttpStatusCode.OK, cancellationToken)
            ?? new List<SubscriptionEnvelope>();

        return envelopes
            .Where(e => e.Subscription is not null)
            .Select(e => MapSubscription(e.Subscription!))
            .Where(s => s.IsCurrent)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    private string FamilyHandle => _options.ProductFamilyHandle
        ?? throw new InvalidOperationException(
            $"Maxio:{nameof(MaxioOptions.ProductFamilyHandle)} is not configured. Set the {MaxioOptions.EnvProductFamilyHandle} environment variable.");

    private async Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<MaxioCustomerDto> EnsureCustomerAsync(string customerReference, string customerEmail, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = EmailNameParser.Parse(customerEmail);
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomerRequestData
            {
                FirstName = firstName,
                LastName = lastName,
                Email = customerEmail,
                Reference = customerReference,
                Organization = OrganizationName
            }
        };

        try
        {
            var envelope = await SendPostAsync<CustomerEnvelope>("customers.json", request, HttpStatusCode.Created, cancellationToken);
            if (envelope?.Customer is { } created)
            {
                return created;
            }
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            _logger.LogWarning("Customer create for {CustomerReference} rejected (422); retrying lookup to honour reference uniqueness.",
                customerReference);
        }

        var retried = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (retried is not null)
        {
            return retried;
        }

        throw new MaxioApiException(
            $"The billing provider could not create a customer record for '{customerReference}'.", 422);
    }

    private async Task<MaxioCustomerDto?> FindCustomerByReferenceAsync(string customerReference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(customerReference)}";
        var envelope = await SendGetAsync<CustomerEnvelope>(path, HttpStatusCode.OK, cancellationToken, allowNotFound: true);
        return envelope?.Customer;
    }

    private async Task<SubscriptionDetails?> FindCurrentSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await SendGetAsync<List<SubscriptionEnvelope>>(path, HttpStatusCode.OK, cancellationToken)
            ?? new List<SubscriptionEnvelope>();

        return envelopes
            .Where(e => e.Subscription is not null)
            .Select(e => MapSubscription(e.Subscription!))
            .FirstOrDefault(s =>
                s.IsCurrent &&
                string.Equals(s.ProductHandle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<SubscriptionDetails> CreateSubscriptionAsync(MaxioCustomerDto customer, SubscriptionPlan plan, CancellationToken cancellationToken)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionRequestData
            {
                ProductHandle = plan.Handle,
                CustomerReference = customer.Reference ?? customer.Id.ToString(),
                PaymentCollectionMethod = SubscriptionPaymentCollectionMethod
            }
        };

        var envelope = await SendPostAsync<SubscriptionEnvelope>("subscriptions.json", request, HttpStatusCode.Created, cancellationToken);
        var dto = envelope?.Subscription
            ?? throw new MaxioApiException("The billing provider returned an empty subscription payload.", null);

        var subscription = MapSubscription(dto);
        if (string.IsNullOrWhiteSpace(subscription.ProductHandle) && plan.Handle is not null)
        {
            subscription = subscription with { ProductHandle = plan.Handle, ProductName = plan.Name };
        }

        return subscription;
    }

    private async Task<T?> SendGetAsync<T>(string path, HttpStatusCode expectedStatus, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode != expectedStatus)
        {
            ThrowForUnexpectedResponse(response.StatusCode, content);
        }

        return Deserialize<T>(content);
    }

    private async Task<T?> SendPostAsync<T>(string path, object payload, HttpStatusCode expectedStatus, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(path))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode != expectedStatus)
        {
            ThrowForUnexpectedResponse(response.StatusCode, content);
        }

        return Deserialize<T>(content);
    }

    private Uri BuildUri(string path)
    {
        if (_httpClient.BaseAddress is null)
        {
            throw new InvalidOperationException(
                $"The Maxio API base address is not configured. Set the {MaxioOptions.EnvSubdomain} environment variable or provide Maxio:BaseUrl.");
        }

        return new Uri(path, UriKind.Relative);
    }

    private void ThrowForUnexpectedResponse(HttpStatusCode statusCode, string content)
    {
        var message = BuildErrorMessage((int)statusCode, content);
        throw new MaxioApiException(message, (int)statusCode);
    }

    private static string BuildErrorMessage(int statusCode, string content)
    {
        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                var error = JsonSerializer.Deserialize<MaxioErrorResponse>(content, JsonOptions);
                var messages = ExtractErrorMessages(error?.Errors);
                if (messages.Count > 0)
                {
                    return $"The billing provider rejected the request (HTTP {statusCode}): {string.Join(" ", messages)}";
                }
            }
            catch (JsonException)
            {
            }
        }

        return $"The billing provider returned HTTP {(int)statusCode}.";
    }

    private static List<string> ExtractErrorMessages(object? errors)
    {
        var result = new List<string>();
        switch (errors)
        {
            case JsonElement { ValueKind: JsonValueKind.Array } array:
                foreach (var item in array.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        result.Add(item.GetString()!);
                    }
                }
                break;
            case JsonElement { ValueKind: JsonValueKind.Object } obj:
                foreach (var property in obj.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                result.Add(item.GetString()!);
                            }
                        }
                    }
                    else if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        result.Add(property.Value.GetString()!);
                    }
                }
                break;
            case string text when !string.IsNullOrWhiteSpace(text):
                result.Add(text);
                break;
        }

        return result;
    }

    private static T? Deserialize<T>(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions);
    }

    private static SubscriptionPlan MapProduct(MaxioProductDto product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        Interval = product.Interval ?? 1,
        IntervalUnit = product.IntervalUnit,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty,
        Archived = product.ArchivedAt is not null
    };

    private static SubscriptionDetails MapSubscription(MaxioSubscriptionDto subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State ?? "unknown",
        CustomerId = subscription.Customer?.Id ?? 0,
        ProductId = subscription.Product?.Id,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        ProductPriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
        BalanceInCents = subscription.BalanceInCents,
        Currency = subscription.Currency,
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
    };

    private static void GuardCustomerArguments(string customerReference, string customerEmail)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(customerReference));
        }

        if (!string.IsNullOrWhiteSpace(customerEmail) &&
            (!customerEmail.Contains('@') || customerEmail.Length > 254))
        {
            throw new ArgumentException("A valid customer e-mail address is required.", nameof(customerEmail));
        }
    }
}
