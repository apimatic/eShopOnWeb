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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing adapter. Customers are keyed by eShopOnWeb user id (<c>reference</c>);
/// subscribe is idempotent per shopper + plan handle.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "assessing",
        "pending",
        "trialing",
        "paused",
        "past_due",
        "soft_failure",
        "unpaid",
        "awaiting_signup"
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly SubscribeIdempotencyGate _idempotencyGate;

    public MaxioSubscriptionBillingService(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger,
        SubscribeIdempotencyGate idempotencyGate)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _idempotencyGate = idempotencyGate;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var products = await ListFamilyProductsAsync(cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(ToPlan)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(Shopper shopper, string productHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        ArgumentNullException.ThrowIfNull(shopper);

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException("A productHandle is required.", 400);
        }

        productHandle = productHandle.Trim();
        var key = $"{shopper.UserId}:{productHandle}";

        return await _idempotencyGate.RunAsync(key, async ct =>
        {
            var plan = await RequirePlanAsync(productHandle, ct);
            var customer = await EnsureCustomerAsync(shopper, ct);

            var existing = await FindLiveSubscriptionAsync(customer.Id, productHandle, ct);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for user {UserId} plan {ProductHandle}",
                    existing.Id, shopper.UserId, productHandle);
                return ToCustomerSubscription(existing);
            }

            var uniquenessToken = Guid.NewGuid().ToString("D");
            var created = await CreateSubscriptionWithRecoveryAsync(customer, shopper, plan, uniquenessToken, ct);
            return ToCustomerSubscription(created);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListShopperSubscriptionsAsync(Shopper shopper, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        ArgumentNullException.ThrowIfNull(shopper);

        var customer = await FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.Id)
            .Select(ToCustomerSubscription)
            .ToList();
    }

    private async Task<MaxioSubscription> CreateSubscriptionWithRecoveryAsync(
        MaxioCustomer customer,
        Shopper shopper,
        SubscriptionPlan plan,
        string uniquenessToken,
        CancellationToken cancellationToken)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            UniquenessToken = uniquenessToken,
            Subscription = new MaxioCreateSubscriptionBody
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                CustomerReference = shopper.UserId,
                Reference = SubscriptionReference(shopper.UserId, plan.Handle),
                PaymentCollectionMethod = "remittance"
            }
        };

        try
        {
            var created = await SendAsync<MaxioSubscriptionEnvelope>(
                HttpMethod.Post,
                "subscriptions.json",
                request,
                cancellationToken);

            if (created.Subscription is null)
            {
                throw new BillingException("Maxio created a subscription but returned an empty body.");
            }

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for user {UserId} plan {ProductHandle}",
                created.Subscription.Id, shopper.UserId, plan.Handle);

            return created.Subscription;
        }
        catch (BillingException ex) when (ex.StatusCode is 409 or 422)
        {
            var recovered = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (recovered is not null)
            {
                _logger.LogInformation(
                    "Recovered existing Maxio subscription {SubscriptionId} after {StatusCode} for user {UserId} plan {ProductHandle}",
                    recovered.Id, ex.StatusCode, shopper.UserId, plan.Handle);
                return recovered;
            }

            throw;
        }
    }

    private async Task<SubscriptionPlan> RequirePlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListAvailablePlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        return plan;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(Shopper shopper, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = NamesFromShopper(shopper);
        var uniquenessToken = Guid.NewGuid().ToString("D");
        var request = new MaxioCreateCustomerRequest
        {
            UniquenessToken = uniquenessToken,
            Customer = new MaxioCreateCustomerBody
            {
                FirstName = firstName,
                LastName = lastName,
                Email = shopper.Email,
                Reference = shopper.UserId
            }
        };

        try
        {
            var created = await SendAsync<MaxioCustomerEnvelope>(
                HttpMethod.Post,
                "customers.json",
                request,
                cancellationToken);

            if (created.Customer is null)
            {
                throw new BillingException("Maxio created a customer but returned an empty body.");
            }

            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}", created.Customer.Id, shopper.UserId);
            return created.Customer;
        }
        catch (BillingException ex) when (ex.StatusCode is 409 or 422)
        {
            var recovered = await FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, body: null, cancellationToken);
            return envelope.Customer;
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString(_options.ProductFamilyHandle.Trim());
        var products = new List<MaxioProduct>();
        var page = 1;

        while (true)
        {
            var path = $"product_families/handle:{family}/products.json?page={page}&per_page=200";
            var batch = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, body: null, cancellationToken);
            var pageProducts = (batch ?? new List<MaxioProductEnvelope>())
                .Select(e => e.Product)
                .Where(p => p is not null)
                .Cast<MaxioProduct>()
                .ToList();

            products.AddRange(pageProducts);
            if (pageProducts.Count < 200)
            {
                break;
            }

            page++;
        }

        return products;
    }

    private async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var batch = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, path, body: null, cancellationToken);
        return (batch ?? new List<MaxioSubscriptionEnvelope>())
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Cast<MaxioSubscription>()
            .ToList();
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            IsLive(s.State)
            && string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativePath);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastException = ex;
                if (attempt == maxAttempts || !IsRetryableTransport(ex) || cancellationToken.IsCancellationRequested)
                {
                    throw new BillingException("Unable to reach Maxio Advanced Billing.", 503);
                }

                await DelayBackoffAsync(attempt, cancellationToken);
                continue;
            }

            using (response)
            {
                var payload = await response.Content.ReadAsStringAsync(cancellationToken);

                if ((int)response.StatusCode == 429)
                {
                    lastException = new BillingException("Maxio rate limit exceeded.", 429);
                    if (attempt == maxAttempts)
                    {
                        throw lastException;
                    }

                    await DelayBackoffAsync(attempt, cancellationToken);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    throw new BillingException(ParseErrors(payload) ?? "Duplicate Maxio request.", 409);
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new BillingException(ParseErrors(payload) ?? "Maxio resource was not found.", 404);
                }

                if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
                {
                    throw new BillingException(ParseErrors(payload) ?? "Maxio rejected the request.", 422);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var message = ParseErrors(payload) ?? $"Maxio request failed with HTTP {(int)response.StatusCode}.";
                    var status = (int)response.StatusCode is >= 400 and < 500 ? 400 : 502;
                    throw new BillingException(message, status);
                }

                if (string.IsNullOrWhiteSpace(payload))
                {
                    return Activator.CreateInstance<T>();
                }

                var parsed = JsonSerializer.Deserialize<T>(payload, MaxioJson.SerializerOptions);
                if (parsed is null)
                {
                    throw new BillingException("Maxio returned an empty JSON document.");
                }

                return parsed;
            }
        }

        throw lastException ?? new BillingException("Unable to reach Maxio Advanced Billing.", 503);
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new BillingNotConfiguredException();
        }
    }

    private static bool IsRetryableTransport(Exception exception) =>
        exception is HttpRequestException || (exception is TaskCanceledException tce && tce.InnerException is TimeoutException);

    private static async Task DelayBackoffAsync(int attempt, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
        await Task.Delay(delay, cancellationToken);
    }

    private static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && LiveSubscriptionStates.Contains(state);

    private static string SubscriptionReference(string userId, string productHandle) =>
        $"{userId}:{productHandle}";

    private static (string FirstName, string LastName) NamesFromShopper(Shopper shopper)
    {
        var source = shopper.Email;
        var at = source.IndexOf('@');
        var local = at > 0 ? source[..at] : shopper.UserName;
        if (string.IsNullOrWhiteSpace(local))
        {
            local = "Shopper";
        }

        var parts = local.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (TruncateName(parts[0]), TruncateName(parts[1]));
        }

        return (TruncateName(local), "eShopOnWeb");
    }

    private static string TruncateName(string value)
    {
        value = value.Trim();
        return value.Length <= 40 ? value : value[..40];
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) =>
        new(
            product.Id,
            product.Handle!,
            product.Name,
            product.Description,
            product.PriceInCents,
            product.Interval,
            product.IntervalUnit ?? "month");

    private static CustomerSubscription ToCustomerSubscription(MaxioSubscription subscription) =>
        new(
            subscription.Id,
            subscription.State ?? "unknown",
            subscription.Product?.Handle,
            subscription.Product?.Name,
            subscription.ProductPriceInCents,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);

    private static string? ParseErrors(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return payload.Length <= 500 ? payload : payload[..500];
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                var items = errors.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                return string.Join("; ", items);
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                var items = errors.EnumerateObject()
                    .Select(p => $"{p.Name}: {p.Value}");
                return string.Join("; ", items);
            }

            if (errors.ValueKind == JsonValueKind.String)
            {
                return errors.GetString();
            }
        }
        catch (JsonException)
        {
            return payload.Length <= 500 ? payload : payload[..500];
        }

        return null;
    }
}
