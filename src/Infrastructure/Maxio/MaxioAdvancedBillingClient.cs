using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<MaxioSettings> _settings;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(
        HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        var familyId = FormatHandle(productFamilyHandle);
        var plans = new List<SubscriptionPlan>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            var path = $"product_families/{familyId}/products.json?page={page}&per_page={perPage}";
            var pageItems = await SendAsync<List<ProductResponse>>(HttpMethod.Get, path, null, cancellationToken)
                            ?? new List<ProductResponse>();

            plans.AddRange(pageItems
                .Where(item => item.Product is not null)
                .Select(item => MapPlan(item.Product)));

            if (pageItems.Count < perPage)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<SubscriptionPlan?> ReadProductByHandleAsync(
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var encodedHandle = Uri.EscapeDataString(productHandle);
            var response = await SendAsync<ProductResponse>(
                HttpMethod.Get,
                $"products/handle/{encodedHandle}.json",
                null,
                cancellationToken);
            return response?.Product is null ? null : MapPlan(response.Product);
        }
        catch (BillingException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<BillingCustomer?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await SendAsync<CustomerResponse>(
                HttpMethod.Get,
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
                null,
                cancellationToken);
            return response?.Customer is null ? null : MapCustomer(response.Customer);
        }
        catch (BillingException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<BillingCustomer>> ListCustomersAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers.json?q={Uri.EscapeDataString(query)}&per_page=50";
        var response = await SendAsync<List<CustomerResponse>>(HttpMethod.Get, path, null, cancellationToken)
                       ?? new List<CustomerResponse>();
        return response
            .Where(item => item.Customer is not null)
            .Select(item => MapCustomer(item.Customer))
            .ToList();
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        BillingCustomer customer,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomerPayload
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        var response = await SendAsync<CustomerResponse>(HttpMethod.Post, "customers.json", request, cancellationToken);
        if (response?.Customer is null)
        {
            throw new BillingException("Maxio did not return a customer after create.", 502);
        }

        return MapCustomer(response.Customer);
    }

    public async Task<BillingCustomer> UpdateCustomerAsync(
        int customerId,
        BillingCustomer customer,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateCustomerRequest
        {
            Customer = new UpdateCustomerPayload
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        var response = await SendAsync<CustomerResponse>(
            HttpMethod.Put,
            $"customers/{customerId}.json",
            request,
            cancellationToken);
        if (response?.Customer is null)
        {
            throw new BillingException("Maxio did not return a customer after update.", 502);
        }

        return MapCustomer(response.Customer);
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<List<SubscriptionResponse>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken) ?? new List<SubscriptionResponse>();

        return response
            .Where(item => item.Subscription is not null)
            .Select(item => MapSubscription(item.Subscription!))
            .ToList();
    }

    public async Task<ShopperSubscription> CreateSubscriptionAsync(
        string productHandle,
        int customerId,
        string? subscriptionReference,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionPayload
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = subscriptionReference,
                PaymentCollectionMethod = "remittance"
            }
        };

        var response = await SendAsync<SubscriptionResponse>(
            HttpMethod.Post,
            "subscriptions.json",
            request,
            cancellationToken,
            expectedSuccess: HttpStatusCode.Created);

        if (response?.Subscription is null)
        {
            throw new BillingException("Maxio did not return a subscription after create.", 502);
        }

        return MapSubscription(response.Subscription);
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken,
        HttpStatusCode? expectedSuccess = null)
    {
        EnsureConfigured();

        var isGet = method == HttpMethod.Get;
        var maxAttempts = isGet ? 3 : 1;
        HttpResponseMessage? response = null;
        string? content = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativePath);
            ApplyAuth(request);
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            response = await _httpClient.SendAsync(request, cancellationToken);
            content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                break;
            }

            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests ||
                            (isGet && (int)response.StatusCode >= 500);
            if (!retryable || attempt == maxAttempts)
            {
                throw CreateApiException(response.StatusCode, content);
            }

            await DelayForRetryAsync(response, attempt, cancellationToken);
        }

        if (response is null)
        {
            throw new BillingException("Maxio request failed before a response was received.", 502);
        }

        if (expectedSuccess is not null && response.StatusCode != expectedSuccess && !response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content, MaxioJson.SerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Maxio response from {Path}.", relativePath);
            throw new BillingException("Maxio returned a response that could not be parsed.", 502);
        }
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.Value.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private void EnsureConfigured()
    {
        if (!_settings.Value.IsConfigured)
        {
            throw new BillingException(
                "Maxio billing is not configured. Set Maxio:ApiKey and Maxio:Subdomain (or Maxio:BaseUrl).",
                503);
        }
    }

    private static async Task DelayForRetryAsync(HttpResponseMessage response, int attempt, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
        if (response.Headers.RetryAfter?.Delta is TimeSpan retryAfter)
        {
            delay = retryAfter;
        }

        await Task.Delay(delay, cancellationToken);
    }

    private static BillingException CreateApiException(HttpStatusCode statusCode, string? content)
    {
        var errors = ParseErrors(content);
        var message = errors.Count > 0
            ? string.Join(" ", errors)
            : $"Maxio API request failed with status {(int)statusCode}.";

        var mappedStatus = statusCode switch
        {
            HttpStatusCode.Unauthorized => 502,
            HttpStatusCode.Forbidden => 502,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.UnprocessableEntity => 400,
            HttpStatusCode.TooManyRequests => 429,
            _ when (int)statusCode >= 500 => 502,
            _ => (int)statusCode
        };

        return new BillingException(message, mappedStatus, errors);
    }

    private static IReadOnlyList<string> ParseErrors(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<string>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ErrorListResponse>(content, MaxioJson.SerializerOptions);
            if (parsed?.Errors is { Count: > 0 })
            {
                return parsed.Errors;
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw body.
        }

        return new[] { content.Trim() };
    }

    private static string FormatHandle(string handle)
    {
        if (handle.StartsWith("handle:", StringComparison.OrdinalIgnoreCase))
        {
            return handle;
        }

        return "handle:" + handle;
    }

    private static SubscriptionPlan MapPlan(ProductPayload product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        RequireCreditCard = product.RequireCreditCard,
        ProductFamilyHandle = product.ProductFamily?.Handle,
        ProductFamilyName = product.ProductFamily?.Name
    };

    private static BillingCustomer MapCustomer(CustomerPayload customer) => new()
    {
        Id = customer.Id,
        Email = customer.Email ?? string.Empty,
        Reference = customer.Reference,
        FirstName = customer.FirstName ?? string.Empty,
        LastName = customer.LastName ?? string.Empty
    };

    private static ShopperSubscription MapSubscription(SubscriptionPayload subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        ProductPriceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0,
        NextBillingDate = ParseTimestamp(subscription.NextAssessmentAt) ?? ParseTimestamp(subscription.CurrentPeriodEndsAt),
        CurrentPeriodStartedAt = ParseTimestamp(subscription.CurrentPeriodStartedAt),
        CurrentPeriodEndsAt = ParseTimestamp(subscription.CurrentPeriodEndsAt),
        CreatedAt = ParseTimestamp(subscription.CreatedAt),
        Reference = subscription.Reference,
        CustomerId = subscription.Customer?.Id
    };

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
