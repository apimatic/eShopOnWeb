using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for Maxio Advanced Billing. Paths, auth, and payloads match <c>maxio-spec/openapi.yaml</c>.
/// Auth: HTTP Basic, username = API key, password = <c>x</c>.
/// </summary>
public class MaxioBillingClient : IMaxioBillingClient
{
    internal static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly HttpClient _http;
    private readonly IOptions<MaxioOptions> _options;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(
        HttpClient http,
        IOptions<MaxioOptions> options,
        IAppLogger<MaxioBillingClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListProductsForProductFamilyAsync(
        CancellationToken cancellationToken = default)
    {
        var familyHandle = _options.Value.ProductFamilyHandle;
        // GET /product_families/{product_family_id}/products.json
        // product_family_id: "Either the product family's id or its handle prefixed with `handle:`"
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?page=1&per_page=200";
        var envelopes = await SendAsync<List<ProductResponse>>(HttpMethod.Get, path, null, cancellationToken)
                        ?? new List<ProductResponse>();

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => p!.ToPlan())
            .ToList();
    }

    public async Task<MaxioCustomer?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        // GET /customers/lookup.json?reference=
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var envelope = await SendAsync<CustomerResponse>(HttpMethod.Get, path, null, cancellationToken);
            return envelope?.Customer?.ToCustomer();
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        // POST /customers.json
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = shopper.FirstName,
                LastName = shopper.LastName,
                Email = shopper.Email,
                Reference = shopper.Reference
            }
        };

        var envelope = await SendAsync<CustomerResponse>(HttpMethod.Post, "customers.json", body, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioApiException(System.Net.HttpStatusCode.BadGateway, "Maxio returned an empty customer payload.");
        }

        return envelope.Customer.ToCustomer();
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        // GET /customers/{customer_id}/subscriptions.json
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await SendAsync<List<SubscriptionResponse>>(HttpMethod.Get, path, null, cancellationToken)
                        ?? new List<SubscriptionResponse>();

        return envelopes
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => s!.ToSubscription())
            .ToList();
    }

    public async Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        // GET /subscriptions/lookup.json?reference=
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var envelope = await SendAsync<SubscriptionResponse>(HttpMethod.Get, path, null, cancellationToken);
            return envelope?.Subscription?.ToSubscription();
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string? subscriptionReference,
        CancellationToken cancellationToken = default)
    {
        // POST /subscriptions.json
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = subscriptionReference,
                // Relationship Invoicing: remittance collects without a stored payment profile.
                PaymentCollectionMethod = "remittance"
            }
        };

        var envelope = await SendAsync<SubscriptionResponse>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException(System.Net.HttpStatusCode.BadGateway, "Maxio returned an empty subscription payload.");
        }

        return envelope.Subscription.ToSubscription();
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken)
    {
        if (_http.BaseAddress is null)
        {
            throw new InvalidOperationException(
                "Maxio is not configured. Set Maxio:ApiKey, Maxio:ProductFamilyHandle, and Maxio:Subdomain (or Maxio:BaseUrl) via user-secrets or MAXIO_* environment variables.");
        }
        const int maxAttempts = 3;
        HttpResponseMessage? response = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativePath);
            if (body is not null)
            {
                request.Content = new StringContent(
                    JsonSerializer.Serialize(body, JsonOptions),
                    Encoding.UTF8,
                    "application/json");
            }

            response = await _http.SendAsync(request, cancellationToken);

            if (IsTransient(response.StatusCode) && attempt < maxAttempts)
            {
                var delay = GetRetryDelay(response, attempt);
                _logger.LogWarning(
                    "Transient Maxio response {StatusCode} on {Method} {Path}; retry {Attempt}/{Max} in {Delay}ms.",
                    (int)response.StatusCode, method, relativePath, attempt, maxAttempts, delay.TotalMilliseconds);
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            break;
        }

        using (response)
        {
            var payload = response is null ? null : await response.Content.ReadAsStringAsync(cancellationToken);

            if (response is not null && response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(payload))
                {
                    return default;
                }

                return JsonSerializer.Deserialize<T>(payload, JsonOptions);
            }

            var status = response?.StatusCode ?? System.Net.HttpStatusCode.BadGateway;
            var errors = MaxioErrorParser.Parse(payload);
            var message = errors.Count > 0
                ? string.Join(" ", errors)
                : $"Maxio request failed with status {(int)status}.";

            _logger.LogWarning(
                "Maxio {Method} {Path} failed with {StatusCode}.",
                method, relativePath, (int)status);

            throw new MaxioApiException(status, message, errors);
        }
    }

    private static bool IsTransient(System.Net.HttpStatusCode status) =>
        status == System.Net.HttpStatusCode.TooManyRequests
        || status == System.Net.HttpStatusCode.BadGateway
        || status == System.Net.HttpStatusCode.ServiceUnavailable
        || status == System.Net.HttpStatusCode.GatewayTimeout;

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        return TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
