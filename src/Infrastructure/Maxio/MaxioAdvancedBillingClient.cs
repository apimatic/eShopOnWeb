using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP adapter for Maxio Advanced Billing (Chargify). Auth is HTTP Basic with the
/// API key as the username and "X" as the password. See
/// https://ahshaikh-mintlify-deploy.mintlify.site/introduction/authentication
/// </summary>
public sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private const int MaxPageSize = 200;
    private const int MaxAttempts = 3;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;
    private readonly MaxioSettings _settings;

    public MaxioAdvancedBillingClient(
        HttpClient httpClient,
        ILogger<MaxioAdvancedBillingClient> logger,
        MaxioSettings settings)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settings = settings;

        EnsureHttpClientConfigured();
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var encodedHandle = Uri.EscapeDataString(productFamilyHandle);
        var plans = new List<SubscriptionPlan>();

        for (var page = 1; ; page++)
        {
            var path = $"product_families/handle:{encodedHandle}/products.json?page={page}&per_page={MaxPageSize}";
            using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new MaxioConfigurationException(
                    $"Maxio product family '{productFamilyHandle}' was not found. Check Maxio:ProductFamilyHandle.");
            }

            await EnsureSuccessAsync(response, cancellationToken);
            var envelopes = await ReadJsonAsync<List<MaxioProductEnvelope>>(response, cancellationToken) ?? new();
            plans.AddRange(envelopes
                .Select(e => e.Product)
                .Where(p => p is not null && p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
                .Select(p => MapPlan(p!)));

            if (envelopes.Count < MaxPageSize)
            {
                break;
            }
        }

        return plans;
    }

    public async Task<SubscriptionPlan?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken = default)
    {
        var path = $"products/handle/{Uri.EscapeDataString(productHandle)}.json";
        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<MaxioProductEnvelope>(response, cancellationToken);
        return envelope?.Product is null ? null : MapPlan(envelope.Product);
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(CreateBillingCustomer request, CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerBody
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Reference = request.Reference
            }
        };

        using var response = await SendAsync(HttpMethod.Post, "customers.json", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioApiException((int)response.StatusCode, new[] { "Maxio returned an empty customer payload." });
        }

        return MapCustomer(envelope.Customer);
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = new List<BillingSubscription>();

        for (var page = 1; ; page++)
        {
            var path = $"customers/{customerId}/subscriptions.json?page={page}&per_page={MaxPageSize}";
            using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var envelopes = await ReadJsonAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken) ?? new();
            subscriptions.AddRange(envelopes
                .Select(e => e.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!)));

            if (envelopes.Count < MaxPageSize)
            {
                break;
            }
        }

        return subscriptions;
    }

    public async Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription is null ? null : MapSubscription(envelope.Subscription);
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(CreateBillingSubscription request, CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionBody
            {
                ProductHandle = request.ProductHandle,
                CustomerId = request.CustomerId,
                Reference = request.Reference,
                PaymentCollectionMethod = request.PaymentCollectionMethod
            },
            UniquenessToken = request.UniquenessToken
        };

        using var response = await SendAsync(HttpMethod.Post, "subscriptions.json", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException((int)response.StatusCode, new[] { "Maxio returned an empty subscription payload." });
        }

        return MapSubscription(envelope.Subscription);
    }

    private void EnsureHttpClientConfigured()
    {
        if (_httpClient.BaseAddress is null &&
            (!string.IsNullOrWhiteSpace(_settings.BaseUrl) || !string.IsNullOrWhiteSpace(_settings.Subdomain)))
        {
            _httpClient.BaseAddress = new Uri(_settings.ResolveBaseUrl(), UriKind.Absolute);
        }

        if (_httpClient.DefaultRequestHeaders.Accept.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        if (_httpClient.DefaultRequestHeaders.Authorization is null && !string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativePath, object? content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new MaxioConfigurationException("Maxio:ApiKey is not configured.");
        }

        HttpResponseMessage? response = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            response?.Dispose();

            using var request = new HttpRequestMessage(method, relativePath);
            if (content is not null)
            {
                request.Content = new StringContent(
                    JsonSerializer.Serialize(content, MaxioJson.Options),
                    Encoding.UTF8,
                    "application/json");
            }

            _logger.LogInformation("Maxio {Method} {Path} (attempt {Attempt})", method, relativePath, attempt);
            response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode != (HttpStatusCode)429 || attempt == MaxAttempts)
            {
                return response;
            }

            var delay = ParseRetryAfter(response) ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
            _logger.LogWarning("Maxio returned 429 for {Path}; retrying in {DelaySeconds}s.", relativePath, delay.TotalSeconds);
            response.Dispose();
            response = null;
            await Task.Delay(delay, cancellationToken);
        }

        throw new MaxioApiException(429, new[] { "Maxio rate-limited the request." });
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        return null;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ParseErrors(body);

        throw response.StatusCode switch
        {
            HttpStatusCode.Conflict => new MaxioDuplicateException(errors),
            HttpStatusCode.UnprocessableEntity => new MaxioValidationException(errors),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new MaxioConfigurationException("Maxio rejected the configured API credentials."),
            HttpStatusCode.TooManyRequests => new MaxioApiException(429, errors),
            _ => new MaxioApiException((int)response.StatusCode, errors)
        };
    }

    private static IReadOnlyList<string> ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return new[] { body };
            }

            if (errorsElement.ValueKind == JsonValueKind.Array)
            {
                return errorsElement.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? e.ToString() : e.ToString())
                    .ToList();
            }

            if (errorsElement.ValueKind == JsonValueKind.Object)
            {
                return errorsElement.EnumerateObject()
                    .Select(p => $"{p.Name}: {p.Value}")
                    .ToList();
            }

            return new[] { errorsElement.ToString() };
        }
        catch (JsonException)
        {
            return new[] { body };
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, MaxioJson.Options);
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product)
    {
        return new SubscriptionPlan(
            Handle: product.Handle ?? string.Empty,
            Name: product.Name ?? product.Handle ?? string.Empty,
            Description: product.Description,
            Price: CentsToDecimal(product.PriceInCents),
            Interval: product.Interval,
            IntervalUnit: product.IntervalUnit ?? "month",
            ProductFamilyHandle: product.ProductFamily?.Handle ?? string.Empty);
    }

    private static BillingCustomer MapCustomer(MaxioCustomer customer)
    {
        return new BillingCustomer(
            customer.Id,
            customer.Reference ?? string.Empty,
            customer.Email ?? string.Empty);
    }

    private static BillingSubscription MapSubscription(MaxioSubscription subscription)
    {
        var priceCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0;

        return new BillingSubscription(
            Id: subscription.Id,
            ProductHandle: subscription.Product?.Handle ?? string.Empty,
            ProductName: subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            Price: CentsToDecimal(priceCents),
            State: subscription.State ?? string.Empty,
            NextBillingDate: subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            Reference: subscription.Reference,
            ProductFamilyHandle: subscription.Product?.ProductFamily?.Handle);
    }

    private static decimal CentsToDecimal(long cents) => cents / 100m;
}
