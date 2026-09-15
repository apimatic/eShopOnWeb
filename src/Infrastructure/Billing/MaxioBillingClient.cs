using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var familyId = $"handle:{_options.ProductFamilyHandle}";
        var plans = new List<SubscriptionPlan>();
        var page = 1;

        while (true)
        {
            var path = $"product_families/{familyId}/products.json?page={page}&per_page=200&include_archived=false";
            var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken)
                            ?? new List<MaxioProductEnvelope>();

            foreach (var envelope in envelopes)
            {
                var product = envelope.Product;
                if (product == null || string.IsNullOrWhiteSpace(product.Handle) || !string.IsNullOrEmpty(product.ArchivedAt))
                {
                    continue;
                }

                plans.Add(MapPlan(product));
            }

            if (envelopes.Count < 200)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, null, cancellationToken);
            return envelope?.Customer == null ? null : MapCustomer(envelope.Customer);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var body = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerBody
            {
                Reference = reference,
                FirstName = firstName,
                LastName = lastName,
                Email = email
            }
        };

        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken);
        if (envelope?.Customer == null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio create-customer response did not include a customer.");
        }

        return MapCustomer(envelope.Customer);
    }

    public async Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get, path, null, cancellationToken);
            return envelope?.Subscription == null ? null : MapSubscription(envelope.Subscription);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionBody
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                Reference = reference,
                PaymentCollectionMethod = "remittance"
            }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        if (envelope?.Subscription == null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio create-subscription response did not include a subscription.");
        }

        return MapSubscription(envelope.Subscription);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken) ?? new List<MaxioSubscriptionEnvelope>();

        return envelopes
            .Where(e => e.Subscription != null)
            .Select(e => MapSubscription(e.Subscription!))
            .ToList();
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string relativePath, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, FormatError(response.StatusCode, payload));
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle) ||
            (string.IsNullOrWhiteSpace(_options.Subdomain) && string.IsNullOrWhiteSpace(_options.BaseUrl)))
        {
            throw new MaxioNotConfiguredException();
        }
    }

    private static string FormatError(HttpStatusCode statusCode, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return $"Maxio API returned {(int)statusCode} {statusCode}.";
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                return $"Maxio API returned {(int)statusCode} {statusCode}: {errors}";
            }
        }
        catch (JsonException)
        {
            // Fall through and return the raw payload.
        }

        return $"Maxio API returned {(int)statusCode} {statusCode}: {payload}";
    }

    private static BillingCustomer MapCustomer(MaxioCustomerPayload customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference ?? string.Empty,
        Email = customer.Email ?? string.Empty
    };

    private static SubscriptionPlan MapPlan(MaxioProductPayload product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        Price = ToDollars(product.PriceInCents),
        Interval = product.Interval ?? 1,
        IntervalUnit = product.IntervalUnit ?? "month"
    };

    private static CustomerSubscription MapSubscription(MaxioSubscriptionPayload subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        Price = ToDollars(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
        NextBillingDate = subscription.NextAssessmentAt,
        Reference = subscription.Reference
    };

    private static decimal ToDollars(long? cents) => cents.HasValue ? cents.Value / 100m : 0m;
}
