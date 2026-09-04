using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<MaxioBillingClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var options = GetValidatedOptions();
        var response = await SendAsync<MaxioProductListResponse>(
            HttpMethod.Get,
            $"product_families/handle:{Uri.EscapeDataString(options.ProductFamilyHandle)}/products.json?page=1&per_page=200",
            null,
            cancellationToken);

        return response!.Items
            .Where(item => item.Product is not null && item.Product.ArchivedAt is null && !string.IsNullOrWhiteSpace(item.Product.Handle))
            .Select(item => new MaxioPlan(
                item.Product!.Handle!,
                item.Product.Name,
                item.Product.Description,
                item.Product.PriceInCents,
                item.Product.Interval,
                item.Product.IntervalUnit))
            .ToArray();
    }

    public async Task<MaxioCustomerRecord?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioCustomerListResponse>(
            HttpMethod.Get,
            $"customers.json?q={Uri.EscapeDataString(reference)}&page=1&per_page=200",
            null,
            cancellationToken,
            allowNotFound: true);

        return response?.Items
            .Select(item => item.Customer)
            .Where(customer => string.Equals(customer.Reference, reference, StringComparison.Ordinal))
            .Select(customer => new MaxioCustomerRecord(customer.Id, customer.Reference!, customer.Email))
            .FirstOrDefault();
    }

    public async Task<MaxioCustomerRecord> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post,
            "customers.json",
            new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email,
                    reference
                }
            },
            cancellationToken);

        return new MaxioCustomerRecord(response!.Customer.Id, response.Customer.Reference ?? reference, response.Customer.Email);
    }

    public async Task<string> GetNoPaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSiteEnvelope>(HttpMethod.Get, "site.json", null, cancellationToken);
        // Billing API documents invoice for legacy Statements Architecture and
        // remittance for Relationship Invoicing. Both avoid card capture at signup.
        return response!.Site.RelationshipInvoicingEnabled ? "remittance" : "invoice";
    }

    public async Task<MaxioSubscriptionRecord?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            cancellationToken,
            allowNotFound: true);

        return response is null ? null : MapSubscription(response.Subscription);
    }

    public async Task<MaxioSubscriptionRecord> CreateSubscriptionAsync(string customerReference, string subscriptionReference, string productHandle, string paymentCollectionMethod, DateTimeOffset nextBillingAt, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            new
            {
                subscription = new
                {
                    product_handle = productHandle,
                    customer_reference = customerReference,
                    reference = subscriptionReference,
                    payment_collection_method = paymentCollectionMethod,
                    next_billing_at = nextBillingAt
                }
            },
            cancellationToken);

        return MapSubscription(response!.Subscription);
    }

    public async Task<IReadOnlyList<MaxioSubscriptionRecord>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionListResponse>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);

        return response!.Items.Select(item => MapSubscription(item.Subscription)).ToArray();
    }

    private MaxioOptions GetValidatedOptions()
    {
        var options = _options.Value;
        options.Validate();
        return options;
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        var options = GetValidatedOptions();
        using var request = new HttpRequestMessage(method, new Uri(options.GetApiBaseUri(), path));
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, _jsonOptions), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Maxio Billing API returned HTTP {StatusCode} for {Method} {Path}.", (int)response.StatusCode, method, path);
            throw new MaxioApiException(response.StatusCode);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return DeserializeResponse<T>(json, response.StatusCode);
    }

    private T DeserializeResponse<T>(string json, HttpStatusCode statusCode)
    {
        try
        {
            var result = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            if (result is not null)
            {
                return result;
            }
        }
        catch (JsonException) when (json.TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            // Some legacy Billing API collection endpoints return the documented
            // item wrappers as a top-level JSON array rather than an items envelope.
            object? result = typeof(T) switch
            {
                var type when type == typeof(MaxioProductListResponse) => new MaxioProductListResponse
                {
                    Items = JsonSerializer.Deserialize<List<MaxioProductItem>>(json, _jsonOptions) ?? new()
                },
                var type when type == typeof(MaxioCustomerListResponse) => new MaxioCustomerListResponse
                {
                    Items = JsonSerializer.Deserialize<List<MaxioCustomerItem>>(json, _jsonOptions) ?? new()
                },
                var type when type == typeof(MaxioSubscriptionListResponse) => new MaxioSubscriptionListResponse
                {
                    Items = JsonSerializer.Deserialize<List<MaxioSubscriptionItem>>(json, _jsonOptions) ?? new()
                },
                _ => null
            };

            if (result is T typedResult)
            {
                return typedResult;
            }
        }

        throw new MaxioApiException(statusCode, "Maxio Billing API returned an invalid response.");
    }

    private static MaxioSubscriptionRecord MapSubscription(MaxioSubscription subscription)
    {
        return new MaxioSubscriptionRecord(
            subscription.Id,
            subscription.Reference,
            subscription.State,
            subscription.Product?.Handle,
            subscription.Product?.Name,
            subscription.PriceInCents,
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt);
    }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string? message = null)
        : base(message ?? $"Maxio Billing API returned HTTP {(int)statusCode}.")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
