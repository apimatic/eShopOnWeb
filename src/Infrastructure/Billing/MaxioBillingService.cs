using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing integration (REST). Confirmed against the official Maxio
/// Advanced Billing API: Basic auth with API key as username and "x" as password;
/// customers via POST /customers.json and GET /customers/lookup.json?reference=;
/// products via GET /product_families/{handle:family}/products.json and
/// GET /products/handle/{handle}.json; subscriptions via POST /subscriptions.json,
/// GET /subscriptions/lookup.json?reference=, and GET /customers/{id}/subscriptions.json.
/// </summary>
public sealed class MaxioBillingService : ISubscriptionBillingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "trialing",
        "past_due",
        "unpaid",
        "pending",
        "paused",
        "awaiting_signup",
        "assessing",
        "on_hold"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        HttpClient httpClient,
        IOptions<MaxioSettings> options,
        ILogger<MaxioBillingService> logger)
    {
        _http = httpClient;
        _settings = options.Value;
        _logger = logger;
        ConfigureClient();
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var familyId = "handle:" + _settings.ProductFamilyHandle;
        var path = $"product_families/{familyId}/products.json?page=1&per_page=200&include_archived=false";
        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken);

        var plans = new List<SubscriptionPlan>();
        foreach (var envelope in envelopes ?? Enumerable.Empty<MaxioProductEnvelope>())
        {
            var product = envelope.Product;
            if (product is null || string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt is not null)
            {
                continue;
            }

            plans.Add(MapPlan(product));
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            throw new BillingException("productHandle is required.");
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new BillingException("A user id is required to subscribe.");
        }

        var gate = UserLocks.GetOrAdd(request.UserId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureProductInFamilyAsync(request.ProductHandle, cancellationToken);

            var subscriptionReference = BuildSubscriptionReference(request.UserId, request.ProductHandle);
            var existingByReference = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existingByReference is not null)
            {
                return new SubscribeResult(MapSubscription(existingByReference), created: false);
            }

            var customer = await EnsureCustomerAsync(request, cancellationToken);

            var existingForProduct = await FindLiveSubscriptionForProductAsync(customer.Id, request.ProductHandle, cancellationToken);
            if (existingForProduct is not null)
            {
                return new SubscribeResult(MapSubscription(existingForProduct), created: false);
            }

            var created = await CreateSubscriptionAsync(customer.Id, request.ProductHandle, subscriptionReference, cancellationToken);
            return new SubscribeResult(MapSubscription(created), created: true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<CustomerSubscription>();
        }

        var customer = await LookupCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customer.Id}/subscriptions.json",
            null,
            cancellationToken);

        return (envelopes ?? new List<MaxioSubscriptionEnvelope>())
            .Where(e => e.Subscription is not null)
            .Select(e => MapSubscription(e.Subscription!))
            .ToList();
    }

    private void ConfigureClient()
    {
        if (_http.BaseAddress is null && !string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            try
            {
                _http.BaseAddress = new Uri(_settings.GetApiBaseUrl(), UriKind.Absolute);
            }
            catch (InvalidOperationException)
            {
                // Left unset; EnsureConfigured will fail on first use with a clear message.
            }
        }

        if (!string.IsNullOrWhiteSpace(_settings.ApiKey) && _http.DefaultRequestHeaders.Authorization is null)
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        if (!_http.DefaultRequestHeaders.Accept.Any())
        {
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new BillingException("Maxio:ApiKey is not configured.", StatusCodes.ServiceUnavailable);
        }

        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new BillingException("Maxio:ProductFamilyHandle is not configured.", StatusCodes.ServiceUnavailable);
        }

        if (_http.BaseAddress is null)
        {
            throw new BillingException("Maxio:BaseUrl or Maxio:Subdomain is not configured.", StatusCodes.ServiceUnavailable);
        }
    }

    private async Task EnsureProductInFamilyAsync(string productHandle, CancellationToken cancellationToken)
    {
        MaxioProductEnvelope? envelope;
        try
        {
            envelope = await SendAsync<MaxioProductEnvelope>(
                HttpMethod.Get,
                $"products/handle/{Uri.EscapeDataString(productHandle)}.json",
                null,
                cancellationToken,
                allowNotFound: true);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            throw new BillingException($"Unknown subscription plan '{productHandle}'.");
        }

        var product = envelope?.Product;
        if (product is null || product.ArchivedAt is not null)
        {
            throw new BillingException($"Unknown subscription plan '{productHandle}'.");
        }

        var familyHandle = product.ProductFamily?.Handle;
        if (!string.Equals(familyHandle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException($"Plan '{productHandle}' is not available in this catalog.");
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscribeRequest request, CancellationToken cancellationToken)
    {
        var existing = await LookupCustomerByReferenceAsync(request.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(request.Email, request.UserName);
        var body = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = string.IsNullOrWhiteSpace(request.Email) ? $"{request.UserId}@users.eshop.local" : request.Email,
                Reference = request.UserId
            }
        };

        try
        {
            var created = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken);
            if (created?.Customer is null)
            {
                throw new MaxioApiException("Maxio returned an empty customer payload.", StatusCodes.BadGateway);
            }

            return created.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            var raced = await LookupCustomerByReferenceAsync(request.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw new BillingException(ex.Message);
        }
    }

    private async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            cancellationToken,
            allowNotFound: true);

        return envelope?.Customer;
    }

    private async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            cancellationToken,
            allowNotFound: true);

        return envelope?.Subscription;
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionForProductAsync(
        long customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);

        return (envelopes ?? new List<MaxioSubscriptionEnvelope>())
            .Select(e => e.Subscription)
            .FirstOrDefault(s =>
                s is not null
                && string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase)
                && IsLive(s.State));
    }

    private async Task<MaxioSubscription> CreateSubscriptionAsync(
        long customerId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference,
                PaymentCollectionMethod = "remittance"
            }
        };

        try
        {
            var created = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
            if (created?.Subscription is null)
            {
                throw new MaxioApiException("Maxio returned an empty subscription payload.", StatusCodes.BadGateway);
            }

            return created.Subscription;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            var existing = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw new BillingException(ex.Message);
        }
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        _logger.LogInformation("Maxio {Method} {Path}", method.Method, relativePath);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException("Unable to reach Maxio Advanced Billing.", StatusCodes.BadGateway, ex.Message);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(payload))
                {
                    return default;
                }

                try
                {
                    return JsonSerializer.Deserialize<T>(payload, JsonOptions);
                }
                catch (JsonException ex)
                {
                    throw new MaxioApiException("Maxio returned a payload that could not be parsed.", StatusCodes.BadGateway, ex.Message);
                }
            }

            var message = FormatProviderError(payload, (int)response.StatusCode);
            _logger.LogWarning("Maxio {Method} {Path} failed with {StatusCode}", method.Method, relativePath, (int)response.StatusCode);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new MaxioApiException("Maxio authentication failed. Check Maxio:ApiKey.", StatusCodes.BadGateway);
            }

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                throw new MaxioApiException(message, (int)response.StatusCode, payload);
            }

            if ((int)response.StatusCode >= 500)
            {
                throw new MaxioApiException("Maxio Advanced Billing is unavailable.", StatusCodes.BadGateway, payload);
            }

            throw new MaxioApiException(message, (int)response.StatusCode, payload);
        }
    }

    private static string FormatProviderError(string payload, int statusCode)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return $"Maxio request failed with HTTP {statusCode}.";
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.TryGetProperty("errors", out var errors))
            {
                var flattened = FlattenErrors(errors);
                if (!string.IsNullOrWhiteSpace(flattened))
                {
                    return flattened;
                }
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString() ?? $"Maxio request failed with HTTP {statusCode}.";
            }
        }
        catch (JsonException)
        {
            // Fall through to a generic message; never echo a raw provider dump to callers.
        }

        return $"Maxio request failed with HTTP {statusCode}.";
    }

    private static string FlattenErrors(JsonElement errors)
    {
        if (errors.ValueKind == JsonValueKind.String)
        {
            return errors.GetString() ?? string.Empty;
        }

        if (errors.ValueKind == JsonValueKind.Array)
        {
            return string.Join("; ", errors.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        if (errors.ValueKind == JsonValueKind.Object)
        {
            var parts = new List<string>();
            foreach (var property in errors.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        parts.Add($"{property.Name}: {item.GetString()}");
                    }
                }
                else if (property.Value.ValueKind == JsonValueKind.String)
                {
                    parts.Add($"{property.Name}: {property.Value.GetString()}");
                }
            }

            return string.Join("; ", parts);
        }

        return string.Empty;
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product)
    {
        return new SubscriptionPlan
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? product.Handle ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Price = product.PriceInCents / 100m,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? "month"
        };
    }

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription)
    {
        var priceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0;

        return new CustomerSubscription
        {
            Id = subscription.Id,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            PriceInCents = priceInCents,
            Price = priceInCents / 100m,
            State = subscription.State ?? "unknown",
            NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
        };
    }

    private static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && LiveSubscriptionStates.Contains(state);

    internal static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"{userId}:{productHandle}";

    internal static (string FirstName, string LastName) SplitDisplayName(string? email, string? userName)
    {
        var source = !string.IsNullOrWhiteSpace(email) ? email : userName;
        if (string.IsNullOrWhiteSpace(source))
        {
            return ("Shopper", "eShopOnWeb");
        }

        var at = source.IndexOf('@');
        var local = at > 0 ? source[..at] : source;
        local = string.IsNullOrWhiteSpace(local) ? "Shopper" : local;
        return (local, "eShopOnWeb");
    }

    private static class StatusCodes
    {
        public const int BadGateway = 502;
        public const int ServiceUnavailable = 503;
    }
}
