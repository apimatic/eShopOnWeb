using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var familyKey = "handle:" + Uri.EscapeDataString(_options.ProductFamilyHandle.Trim());
        var plans = new List<SubscriptionPlan>();

        for (var page = 1; ; page++)
        {
            var path = $"product_families/{familyKey}/products.json?page={page}&per_page=200&include_archived=false";
            var envelopes = await GetAsync<List<MaxioProductEnvelope>>(path, cancellationToken)
                            ?? new List<MaxioProductEnvelope>();

            foreach (var envelope in envelopes)
            {
                var product = envelope.Product;
                if (product is null || string.IsNullOrWhiteSpace(product.Handle) || !string.IsNullOrEmpty(product.ArchivedAt))
                {
                    continue;
                }

                plans.Add(MapPlan(product));
            }

            if (envelopes.Count < 200)
            {
                break;
            }
        }

        return plans;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }

        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        ShopperIdentity shopper,
        string uniquenessToken,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var (firstName, lastName) = shopper.DisplayName();
        var payload = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = shopper.Email,
                Reference = shopper.UserId
            },
            UniquenessToken = uniquenessToken
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", payload, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }

        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken)
                       ?? throw new MaxioBillingException("Maxio returned an empty customer payload.", (int)response.StatusCode);
        if (envelope.Customer is null)
        {
            throw new MaxioBillingException("Maxio returned an empty customer payload.", (int)response.StatusCode);
        }

        return MapCustomer(envelope.Customer);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var subscriptions = new List<CustomerSubscription>();
        for (var page = 1; ; page++)
        {
            var path = $"customers/{customerId}/subscriptions.json?page={page}&per_page=200";
            var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(path, cancellationToken)
                            ?? new List<MaxioSubscriptionEnvelope>();

            foreach (var envelope in envelopes)
            {
                if (envelope.Subscription is null)
                {
                    continue;
                }

                subscriptions.Add(MapSubscription(envelope.Subscription));
            }

            if (envelopes.Count < 200)
            {
                break;
            }
        }

        return subscriptions;
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string subscriptionReference,
        string uniquenessToken,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var payload = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionAttributes
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = subscriptionReference,
                // Remittance invoices instead of capturing a card, so signup works when
                // the product does not require a payment method.
                PaymentCollectionMethod = "remittance"
            },
            UniquenessToken = uniquenessToken
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }

        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(JsonOptions, cancellationToken)
                       ?? throw new MaxioBillingException("Maxio returned an empty subscription payload.", (int)response.StatusCode);
        if (envelope.Subscription is null)
        {
            throw new MaxioBillingException("Maxio returned an empty subscription payload.", (int)response.StatusCode);
        }

        return MapSubscription(envelope.Subscription);
    }

    public async Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }

        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Subscription is null ? null : MapSubscription(envelope.Subscription);
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private void EnsureConfigured()
    {
        if (_options.IsConfigured && _httpClient.BaseAddress is not null)
        {
            return;
        }

        throw new MaxioConfigurationException(
            "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle.");
    }

    private static MaxioCustomer MapCustomer(MaxioCustomerPayload payload) => new()
    {
        Id = payload.Id,
        Reference = payload.Reference,
        Email = payload.Email
    };

    private static SubscriptionPlan MapPlan(MaxioProductPayload product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Price = CentsToDecimal(product.PriceInCents),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month"
    };

    private static CustomerSubscription MapSubscription(MaxioSubscriptionPayload subscription)
    {
        var priceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0;

        return new CustomerSubscription
        {
            Id = subscription.Id,
            State = subscription.State ?? "unknown",
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            PriceInCents = priceInCents,
            Price = CentsToDecimal(priceInCents),
            NextBillingDate = ParseTimestamp(subscription.CurrentPeriodEndsAt)
                              ?? ParseTimestamp(subscription.NextAssessmentAt),
            CreatedAt = ParseTimestamp(subscription.CreatedAt),
            Reference = subscription.Reference
        };
    }

    private static decimal CentsToDecimal(long cents) => cents / 100m;

    private static DateTimeOffset? ParseTimestamp(string? value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private async Task<MaxioBillingException> CreateExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = ExtractErrorMessage(body)
                      ?? $"Maxio request failed with status {(int)response.StatusCode}.";

        _logger.LogWarning(
            "Maxio {Method} {Path} failed with {Status}: {Message}",
            response.RequestMessage?.Method,
            response.RequestMessage?.RequestUri?.PathAndQuery,
            (int)response.StatusCode,
            message);

        return new MaxioBillingException(message, (int)response.StatusCode, body);
    }

    private static string? ExtractErrorMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return null;
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var item in errors.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(item.GetString()!);
                    }
                }

                return parts.Count > 0 ? string.Join(" ", parts) : null;
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                var parts = new List<string>();
                foreach (var property in errors.EnumerateObject())
                {
                    parts.Add($"{property.Name}: {property.Value}");
                }

                return parts.Count > 0 ? string.Join("; ", parts) : null;
            }

            if (errors.ValueKind == JsonValueKind.String)
            {
                return errors.GetString();
            }
        }
        catch (JsonException)
        {
            // Body was not JSON; fall through to a truncated raw snippet.
        }

        return body.Length <= 300 ? body : body[..300];
    }
}
