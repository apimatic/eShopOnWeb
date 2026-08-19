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
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// HTTP client for Maxio Advanced Billing (Chargify-compatible REST API).
/// Authentication: HTTP Basic, API key as username and "x" as password, per
/// https://ahshaikh-mintlify-deploy.mintlify.site/introduction/authentication
/// </summary>
public class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(
        HttpClient http,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();
        var family = Uri.EscapeDataString(_options.ProductFamilyHandle.Trim());
        var path = $"product_families/handle:{family}/products.json?include_archived=false&per_page=200";
        var envelopes = await SendAsync<List<ProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken)
                       ?? new List<ProductEnvelope>();

        return envelopes
            .Where(e => e.Product is not null && !string.IsNullOrWhiteSpace(e.Product.Handle))
            .Select(e => MapPlan(e.Product!))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<CustomerEnvelope>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();
        var body = new CustomerEnvelope
        {
            Customer = new CustomerPayload
            {
                FirstName = shopper.FirstName,
                LastName = shopper.LastName,
                Email = shopper.Email,
                Reference = shopper.UserId
            }
        };

        var created = await SendAsync<CustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken);
        if (created?.Customer is null)
        {
            throw new MaxioApiException(500, "Maxio create-customer returned an empty payload.");
        }

        return MapCustomer(created.Customer);
    }

    public async Task<ShopperSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<SubscriptionEnvelope>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return envelope?.Subscription is null ? null : MapSubscription(envelope.Subscription);
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await SendAsync<List<SubscriptionEnvelope>>(HttpMethod.Get, path, null, cancellationToken)
                       ?? new List<SubscriptionEnvelope>();

        return envelopes
            .Where(e => e.Subscription is not null)
            .Select(e => MapSubscription(e.Subscription!))
            .ToList();
    }

    public async Task<ShopperSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();
        var body = new SubscriptionEnvelope
        {
            Subscription = new SubscriptionPayload
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference,
                // Relationship Invoicing: remittance collects later without a stored card.
                // These catalog products do not require a payment profile at signup.
                PaymentCollectionMethod = "remittance"
            }
        };

        var created = await SendAsync<SubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        if (created?.Subscription is null)
        {
            throw new MaxioApiException(500, "Maxio create-subscription returned an empty payload.");
        }

        return MapSubscription(created.Subscription);
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        const int maxAttempts = 3;
        HttpResponseMessage? response = null;
        string? payload = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativePath);
            if (body is not null)
            {
                payload ??= JsonSerializer.Serialize(body, JsonOptions);
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            }

            response = await _http.SendAsync(request, cancellationToken);
            if (IsTransient(response.StatusCode) && attempt < maxAttempts)
            {
                _logger.LogWarning(
                    "Transient Maxio response {StatusCode} for {Method} {Path}; retry {Attempt}/{Max}.",
                    (int)response.StatusCode, method, relativePath, attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                continue;
            }

            break;
        }

        if (response is null)
        {
            throw new MaxioApiException(0, $"No response from Maxio for {method} {relativePath}.");
        }

        using (response)
        {
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Maxio {Method} {Path} failed with {StatusCode}: {Body}",
                    method, relativePath, (int)response.StatusCode, Truncate(content));
                throw new MaxioApiException(
                    (int)response.StatusCode,
                    $"Maxio request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {Truncate(content)}");
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(content, JsonOptions);
        }
    }

    private static bool IsTransient(HttpStatusCode status)
        => status == HttpStatusCode.TooManyRequests
           || status == HttpStatusCode.RequestTimeout
           || (int)status >= 500;

    private SubscriptionPlan MapPlan(ProductPayload product)
    {
        return new SubscriptionPlan
        {
            ProductId = product.Id,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? product.Handle ?? string.Empty,
            Description = product.Description,
            Price = CentsToAmount(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? "month",
            ProductFamilyHandle = product.ProductFamily?.Handle ?? _options.ProductFamilyHandle
        };
    }

    private static MaxioCustomer MapCustomer(CustomerPayload customer)
        => new()
        {
            Id = customer.Id,
            Reference = customer.Reference,
            Email = customer.Email ?? string.Empty,
            FirstName = customer.FirstName ?? string.Empty,
            LastName = customer.LastName ?? string.Empty
        };

    private static ShopperSubscription MapSubscription(SubscriptionPayload subscription)
    {
        var product = subscription.Product;
        var priceCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : product?.PriceInCents ?? 0;

        return new ShopperSubscription
        {
            SubscriptionId = subscription.Id,
            State = subscription.State ?? string.Empty,
            Reference = subscription.Reference,
            CustomerId = subscription.Customer?.Id ?? subscription.CustomerId ?? 0,
            ProductHandle = product?.Handle ?? subscription.ProductHandle ?? string.Empty,
            ProductName = product?.Name ?? string.Empty,
            Price = CentsToAmount(priceCents),
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt
        };
    }

    private static decimal CentsToAmount(long cents) => cents / 100m;

    private static string Truncate(string? value, int max = 500)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value[..max] + "...";
    }

    private sealed class CustomerEnvelope
    {
        public CustomerPayload? Customer { get; set; }
    }

    private sealed class CustomerPayload
    {
        public int Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Reference { get; set; }
    }

    private sealed class ProductEnvelope
    {
        public ProductPayload? Product { get; set; }
    }

    private sealed class ProductPayload
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Handle { get; set; }
        public string? Description { get; set; }
        public long PriceInCents { get; set; }
        public int Interval { get; set; }
        public string? IntervalUnit { get; set; }
        public ProductFamilyPayload? ProductFamily { get; set; }
    }

    private sealed class ProductFamilyPayload
    {
        public string? Handle { get; set; }
    }

    private sealed class SubscriptionEnvelope
    {
        public SubscriptionPayload? Subscription { get; set; }
    }

    private sealed class SubscriptionPayload
    {
        public int Id { get; set; }
        public string? State { get; set; }
        public string? Reference { get; set; }
        public string? ProductHandle { get; set; }
        public int? CustomerId { get; set; }
        public string? PaymentCollectionMethod { get; set; }
        public long ProductPriceInCents { get; set; }
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
        public DateTimeOffset? NextAssessmentAt { get; set; }
        public ProductPayload? Product { get; set; }
        public CustomerPayload? Customer { get; set; }
    }
}

public static class MaxioHttpClientFactory
{
    public static void Configure(HttpClient client, MaxioOptions options)
    {
        options.EnsureConfigured();
        client.BaseAddress = new Uri(options.ResolveBaseUrl());
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    }
}
