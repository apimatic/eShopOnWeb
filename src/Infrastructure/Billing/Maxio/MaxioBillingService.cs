using System;
using System.Collections.Concurrent;
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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public sealed class MaxioBillingService : ISubscriptionBillingService
{
    private const int DefaultPageSize = 200;
    private static readonly HashSet<string> OpenSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "trialing",
        "past_due",
        "pending",
        "assessing",
        "soft_failure",
        "paused",
        "unpaid",
        "on_hold",
        "suspended",
        "awaiting_signup"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        ConfigureClient();
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var handle = _options.ProductFamilyHandle.Trim();
        var plans = new List<SubscriptionPlan>();
        var page = 1;

        while (true)
        {
            var path = $"product_families/handle:{Uri.EscapeDataString(handle)}/products.json?page={page}&per_page={DefaultPageSize}";
            var products = await GetAsync<List<ProductResponse>>(path, cancellationToken).ConfigureAwait(false)
                ?? new List<ProductResponse>();

            foreach (var wrapper in products)
            {
                var plan = ToPlan(wrapper.Product);
                if (plan is not null)
                {
                    plans.Add(plan);
                }
            }

            if (products.Count < DefaultPageSize)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public Task<SubscribeResult> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        var handle = productHandle?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new SubscriptionBillingException("A productHandle is required.", 400);
        }

        EnsureConfigured();

        return WithLockAsync(shopper.UserId, async () =>
        {
            var product = await ReadProductByHandleAsync(handle, cancellationToken).ConfigureAwait(false);
            EnsureProductInConfiguredFamily(product, handle);

            var customer = await EnsureCustomerAsync(shopper, cancellationToken).ConfigureAwait(false);
            var existing = await FindOpenSubscriptionAsync(customer.Id!.Value, handle, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for shopper {ShopperId} on plan {ProductHandle}",
                    existing.Id,
                    shopper.UserId,
                    handle);
                return new SubscribeResult(existing, Created: false);
            }

            var created = await CreateSubscriptionAsync(customer.Id.Value, handle, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for shopper {ShopperId} on plan {ProductHandle}",
                created.Id,
                shopper.UserId,
                handle);
            return new SubscribeResult(created, Created: true);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsAsync(ShopperIdentity shopper, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shopper);
        EnsureConfigured();

        var customer = await FindCustomerByReferenceAsync(shopper.UserId, cancellationToken).ConfigureAwait(false);
        if (customer?.Id is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var payloads = await GetAsync<List<SubscriptionResponse>>(
            $"customers/{customer.Id.Value}/subscriptions.json",
            cancellationToken).ConfigureAwait(false) ?? new List<SubscriptionResponse>();

        return payloads
            .Select(wrapper => ToSubscription(wrapper.Subscription))
            .Where(subscription => subscription is not null)
            .Select(subscription => subscription!)
            .ToList();
    }

    private void ConfigureClient()
    {
        _httpClient.BaseAddress ??= MaxioBaseUrl.Resolve(_options);

        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan)
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        if (!_httpClient.DefaultRequestHeaders.Accept.Any())
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        if (!string.IsNullOrWhiteSpace(_options.ApiKey)
            && _httpClient.DefaultRequestHeaders.Authorization is null)
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || (string.IsNullOrWhiteSpace(_options.Subdomain) && string.IsNullOrWhiteSpace(_options.BaseUrl))
            || string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new SubscriptionBillingException(
                "Maxio billing is not configured. Bind Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl), and Maxio:ProductFamilyHandle.",
                503);
        }
    }

    private async Task<ProductPayload> ReadProductByHandleAsync(string productHandle, CancellationToken cancellationToken)
    {
        var path = $"products/handle/{Uri.EscapeDataString(productHandle)}.json";
        try
        {
            var response = await GetAsync<ProductResponse>(path, cancellationToken).ConfigureAwait(false);
            if (response?.Product is null)
            {
                throw new SubscriptionBillingException($"Subscription plan '{productHandle}' was not found.", 400);
            }

            return response.Product;
        }
        catch (SubscriptionBillingException ex) when (ex.StatusCode == 404)
        {
            throw new SubscriptionBillingException($"Subscription plan '{productHandle}' was not found.", 400);
        }
    }

    private void EnsureProductInConfiguredFamily(ProductPayload product, string productHandle)
    {
        var familyHandle = product.ProductFamily?.Handle;
        if (string.IsNullOrWhiteSpace(familyHandle)
            || !string.Equals(familyHandle, _options.ProductFamilyHandle.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionBillingException(
                $"Subscription plan '{productHandle}' is not part of the configured product family.",
                400);
        }

        if (product.ArchivedAt is not null)
        {
            throw new SubscriptionBillingException($"Subscription plan '{productHandle}' is no longer available.", 400);
        }
    }

    private async Task<CustomerPayload> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(shopper.UserId, cancellationToken).ConfigureAwait(false);
        if (existing?.Id is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ShopperName.FromIdentity(shopper.Email, shopper.UserName);
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomerPayload
            {
                FirstName = firstName,
                LastName = lastName,
                Email = shopper.Email,
                Reference = shopper.UserId
            }
        };

        try
        {
            var created = await PostAsync<CreateCustomerRequest, CustomerResponse>(
                "customers.json",
                request,
                cancellationToken,
                expectedStatus: HttpStatusCode.OK).ConfigureAwait(false);

            if (created?.Customer?.Id is null)
            {
                throw new SubscriptionBillingException("Maxio did not return a customer after create.", 502);
            }

            return created.Customer;
        }
        catch (SubscriptionBillingException ex) when (ex.StatusCode == 422)
        {
            var raced = await FindCustomerByReferenceAsync(shopper.UserId, cancellationToken).ConfigureAwait(false);
            if (raced?.Id is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<CustomerPayload?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var response = await GetAsync<CustomerResponse>(path, cancellationToken).ConfigureAwait(false);
            return response?.Customer;
        }
        catch (SubscriptionBillingException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<ShopperSubscription?> FindOpenSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var payloads = await GetAsync<List<SubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json",
            cancellationToken).ConfigureAwait(false) ?? new List<SubscriptionResponse>();

        return payloads
            .Select(wrapper => wrapper.Subscription)
            .Where(subscription => subscription is not null
                && IsOpenState(subscription!.State)
                && string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
            .Select(ToSubscription)
            .FirstOrDefault();
    }

    private async Task<ShopperSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionPayload
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = "remittance"
            }
        };

        try
        {
            var created = await PostAsync<CreateSubscriptionRequest, SubscriptionResponse>(
                "subscriptions.json",
                request,
                cancellationToken,
                expectedStatus: HttpStatusCode.Created).ConfigureAwait(false);

            var subscription = ToSubscription(created?.Subscription);
            if (subscription is null)
            {
                throw new SubscriptionBillingException("Maxio did not return a subscription after create.", 502);
            }

            return subscription;
        }
        catch (SubscriptionBillingException ex) when (ex.StatusCode == 422)
        {
            var existing = await FindOpenSubscriptionAsync(customerId, productHandle, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }

    private static bool IsOpenState(string? state) =>
        !string.IsNullOrWhiteSpace(state) && OpenSubscriptionStates.Contains(state);

    private static SubscriptionPlan? ToPlan(ProductPayload? product)
    {
        if (product is null || string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt is not null)
        {
            return null;
        }

        return new SubscriptionPlan
        {
            Handle = product.Handle,
            Name = product.Name ?? product.Handle,
            Description = product.Description,
            Price = CentsToDecimal(product.PriceInCents),
            Interval = product.Interval ?? 1,
            IntervalUnit = product.IntervalUnit ?? "month",
            PaymentMethodRequired = product.RequireCreditCard ?? false
        };
    }

    private static ShopperSubscription? ToSubscription(SubscriptionPayload? subscription)
    {
        if (subscription?.Id is null)
        {
            return null;
        }

        var product = subscription.Product;
        return new ShopperSubscription
        {
            Id = subscription.Id.Value,
            ProductHandle = product?.Handle ?? string.Empty,
            ProductName = product?.Name ?? product?.Handle ?? string.Empty,
            Price = CentsToDecimal(subscription.ProductPriceInCents ?? product?.PriceInCents),
            Interval = product?.Interval ?? 1,
            IntervalUnit = product?.IntervalUnit ?? "month",
            State = subscription.State ?? string.Empty,
            NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt
        };
    }

    private static decimal CentsToDecimal(long? cents) => (cents ?? 0) / 100m;

    private async Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string relativePath,
        TRequest body,
        CancellationToken cancellationToken,
        HttpStatusCode expectedStatus)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, expectedStatus, payload, allowAnySuccess: true);
        return JsonSerializer.Deserialize<TResponse>(payload, JsonOptions);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        HttpResponseMessage? response = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            response?.Dispose();
            using var attemptRequest = await CloneAsync(request).ConfigureAwait(false);
            response = await _httpClient.SendAsync(attemptRequest, cancellationToken).ConfigureAwait(false);

            if (!IsTransient(response.StatusCode) || attempt == maxAttempts)
            {
                return response;
            }

            _logger.LogWarning(
                "Transient Maxio response {StatusCode} on {Method} {Path}; retry {Attempt}/{MaxAttempts}",
                (int)response.StatusCode,
                request.Method,
                request.RequestUri,
                attempt,
                maxAttempts);

            await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken).ConfigureAwait(false);
        }

        return response!;
    }

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, HttpStatusCode.OK, payload);

        if (string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    private void EnsureSuccess(HttpResponseMessage response, HttpStatusCode expectedStatus, string payload, bool allowAnySuccess = false)
    {
        if (response.StatusCode == expectedStatus
            || (allowAnySuccess && (int)response.StatusCode is >= 200 and <= 299))
        {
            return;
        }

        var status = (int)response.StatusCode;
        var message = FormatError(payload) ?? $"Maxio request failed with HTTP {status}.";

        _logger.LogWarning("Maxio API returned HTTP {StatusCode}: {Message}", status, message);

        var mapped = status switch
        {
            400 or 404 or 409 or 422 => status == 422 ? 400 : status,
            401 or 403 => 502,
            _ => 502
        };

        if (status == 404)
        {
            mapped = 404;
        }

        throw new SubscriptionBillingException(message, mapped);
    }

    private static string? FormatError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var errors = JsonSerializer.Deserialize<ErrorListResponse>(payload, JsonOptions);
            if (errors?.Errors is { Count: > 0 })
            {
                return string.Join(" ", errors.Errors);
            }
        }
        catch (JsonException)
        {
            // Fall through to raw payload (truncated).
        }

        var trimmed = payload.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy
        };

        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (original.Content is not null)
        {
            var bytes = await original.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in original.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private static async Task<T> WithLockAsync<T>(string key, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var gate = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
