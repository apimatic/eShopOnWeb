using System;
using System.Collections.Concurrent;
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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing HTTP client. Customers are keyed by eShopOnWeb user id
/// (<c>reference</c>); subscriptions are keyed by user id + product handle so a
/// double-click cannot create duplicates.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "past_due", "soft_failure", "unpaid", "paused"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new();

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        IAppLogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public static void ConfigureClient(HttpClient client, MaxioOptions options)
    {
        client.BaseAddress = options.GetApiBaseUri();
        client.Timeout = TimeSpan.FromSeconds(100);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();

        var familyHandle = Uri.EscapeDataString(_options.ProductFamilyHandle);
        var path = $"product_families/handle:{familyHandle}/products.json?per_page=200";
        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken);

        return (envelopes ?? new List<MaxioProductEnvelope>())
            .Select(e => e.Product)
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => ToPlan(p!))
            .ToList();
    }

    public async Task<ShopperSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingValidationException("productHandle is required.");
        }

        productHandle = productHandle.Trim();
        var plan = await RequirePlanAsync(productHandle, cancellationToken);

        var gate = SubscribeGates.GetOrAdd(shopper.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);

            var existing = await FindExistingSubscriptionAsync(customer.Id, shopper.UserId, productHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for user {UserId} on plan {Handle}.",
                    existing.Id, shopper.UserId, productHandle);
                return ToShopperSubscription(existing, plan);
            }

            var created = await CreateSubscriptionAsync(customer.Id, shopper.UserId, productHandle, cancellationToken);
            return ToShopperSubscription(created, plan);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();

        var customer = await FindCustomerByReferenceAsync(CustomerReference(shopper.UserId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var path = $"customers/{customer.Id}/subscriptions.json?per_page=200";
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, path, null, cancellationToken);

        return (envelopes ?? new List<MaxioSubscriptionEnvelope>())
            .Select(e => e.Subscription)
            .Where(s => s is not null && !string.Equals(s.State, "failed_to_create", StringComparison.OrdinalIgnoreCase))
            .Select(s => ToShopperSubscription(s!))
            .ToList();
    }

    private async Task<SubscriptionPlan> RequirePlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new BillingValidationException(
                $"Unknown subscription plan '{productHandle}'. Use GET /api/subscription-plans for available handles.");
        }

        return plan;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(shopper.UserId);
        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(shopper.Email, shopper.UserName);
        var uniquenessToken = Guid.NewGuid().ToString();
        var payload = new
        {
            Customer = new
            {
                FirstName = firstName,
                LastName = lastName,
                Email = shopper.Email,
                Reference = reference
            },
            UniquenessToken = uniquenessToken
        };

        try
        {
            var created = await SendAsync<MaxioCustomerEnvelope>(
                HttpMethod.Post, "customers.json", payload, cancellationToken);
            if (created?.Customer is null)
            {
                throw new MaxioApiException("Maxio returned an empty customer after create.");
            }

            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.", created.Customer.Id, shopper.UserId);
            return created.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, null, cancellationToken, treatNotFoundAsNull: true);
            return envelope?.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<MaxioSubscription?> FindExistingSubscriptionAsync(
        int customerId,
        string userId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var byReference = await FindSubscriptionByReferenceAsync(SubscriptionReference(userId, productHandle), cancellationToken);
        if (byReference is not null && IsUsable(byReference))
        {
            return byReference;
        }

        var path = $"customers/{customerId}/subscriptions.json?per_page=200";
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, path, null, cancellationToken);
        return (envelopes ?? new List<MaxioSubscriptionEnvelope>())
            .Select(e => e.Subscription)
            .FirstOrDefault(s =>
                s is not null
                && IsUsable(s)
                && string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get, path, null, cancellationToken, treatNotFoundAsNull: true);
            return envelope?.Subscription;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<MaxioSubscription> CreateSubscriptionAsync(
        int customerId,
        string userId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var reference = SubscriptionReference(userId, productHandle);
        var collectionMethods = new[] { "remittance", "invoice" };
        MaxioApiException? lastError = null;

        foreach (var collectionMethod in collectionMethods)
        {
            var uniquenessToken = Guid.NewGuid().ToString();
            var payload = new
            {
                Subscription = new
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    Reference = reference,
                    PaymentCollectionMethod = collectionMethod
                },
                UniquenessToken = uniquenessToken
            };

            try
            {
                var created = await SendAsync<MaxioSubscriptionEnvelope>(
                    HttpMethod.Post, "subscriptions.json", payload, cancellationToken);
                if (created?.Subscription is null)
                {
                    throw new MaxioApiException("Maxio returned an empty subscription after create.");
                }

                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} for user {UserId} on plan {Handle} ({CollectionMethod}).",
                    created.Subscription.Id, userId, productHandle, collectionMethod);
                return created.Subscription;
            }
            catch (MaxioApiException ex) when (ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
            {
                var existing = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
                if (existing is not null)
                {
                    return existing;
                }

                lastError = ex;
                var canRetryWithInvoice = collectionMethod == "remittance"
                    && (ex.Message.Contains("payment_collection_method", StringComparison.OrdinalIgnoreCase)
                        || ex.Message.Contains("No payment method", StringComparison.OrdinalIgnoreCase));
                if (!canRetryWithInvoice)
                {
                    throw;
                }

                _logger.LogWarning(
                    "Maxio rejected remittance collection; retrying subscription create with invoice.");
            }
        }

        throw lastError ?? new MaxioApiException("Unable to create Maxio subscription.");
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken,
        bool treatNotFoundAsNull = false)
    {
        const int maxAttempts = 4;
        HttpResponseMessage? response = null;
        string? responseBody = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativePath);
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, MaxioJson.Options);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
                responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < maxAttempts)
            {
                _logger.LogWarning("Maxio request to {Path} timed out (attempt {Attempt}). Retrying.", relativePath, attempt);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken);
                continue;
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning("Maxio request to {Path} failed (attempt {Attempt}): {Message}. Retrying.", relativePath, attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken);
                continue;
            }

            if (response!.StatusCode == (HttpStatusCode)429 && attempt < maxAttempts)
            {
                _logger.LogWarning("Maxio rate-limited {Path} (attempt {Attempt}). Backing off.", relativePath, attempt);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
                continue;
            }

            break;
        }

        if (response is null)
        {
            throw new MaxioApiException($"Maxio request to {relativePath} failed with no response.");
        }

        if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(responseBody, MaxioJson.Options);
            }
            catch (JsonException ex)
            {
                throw new MaxioApiException($"Maxio returned unreadable JSON from {relativePath}.", ex, response.StatusCode);
            }
        }

        var detail = ExtractErrorDetail(responseBody);
        throw new MaxioApiException(
            $"Maxio {method} {relativePath} failed with {(int)response.StatusCode}: {detail}",
            response.StatusCode);
    }

    private static string ExtractErrorDetail(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "(empty body)";
        }

        try
        {
            var payload = JsonSerializer.Deserialize<MaxioErrorPayload>(responseBody, MaxioJson.Options);
            if (payload?.Errors is { Count: > 0 })
            {
                return string.Join("; ", payload.Errors);
            }
        }
        catch (JsonException)
        {
            // Fall through to truncated raw body.
        }

        return responseBody.Length <= 500 ? responseBody : responseBody[..500];
    }

    private static bool IsUsable(MaxioSubscription subscription) =>
        !string.IsNullOrWhiteSpace(subscription.State) && LiveStates.Contains(subscription.State);

    public static string CustomerReference(string userId) => $"eshoponweb:{userId}";

    public static string SubscriptionReference(string userId, string productHandle) =>
        $"eshoponweb:{userId}:{productHandle}";

    public static (string FirstName, string LastName) SplitDisplayName(string email, string userName)
    {
        var source = !string.IsNullOrWhiteSpace(email) ? email : userName;
        var local = source;
        var at = source.IndexOf('@');
        if (at > 0)
        {
            local = source[..at];
        }

        local = local.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Trim();
        if (string.IsNullOrWhiteSpace(local))
        {
            return ("Shopper", "eShopOnWeb");
        }

        var parts = local.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return (Capitalize(parts[0]), "Customer");
        }

        return (Capitalize(parts[0]), Capitalize(string.Join(' ', parts.Skip(1))));
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description ?? string.Empty,
        Price = product.PriceInCents / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    private static ShopperSubscription ToShopperSubscription(MaxioSubscription subscription, SubscriptionPlan? plan = null) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.Product?.Handle ?? plan?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? plan?.Name ?? string.Empty,
        Price = subscription.ProductPriceInCents > 0
            ? subscription.ProductPriceInCents / 100m
            : plan?.Price ?? (subscription.Product?.PriceInCents ?? 0) / 100m,
        Interval = subscription.Product?.Interval ?? plan?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? plan?.IntervalUnit ?? string.Empty,
        State = subscription.State ?? string.Empty,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
    };
}
