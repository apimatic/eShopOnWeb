using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscription;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of subscription billing.
/// Talks to the Billing API (HTTP Basic auth over TLS, JSON) and keeps all
/// Maxio ids/references derived from stable eShopOnWeb identifiers, so the
/// integration survives Maxio catalog re-seeds that reassign numeric ids.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    // Subscription states that represent a currently-billing (or collectible) subscription.
    // Anything else (canceled / expired / trial_ended / failed_to_create) is end-of-life.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "assessing", "awaiting_signup", "trialing", "active",
        "soft_failure", "past_due", "unpaid", "on_hold", "suspended"
    };

    private const string CustomerReferencePrefix = "eshoponweb-user:";
    private const string SubscriptionReferencePrefix = "eshoponweb-sub:";
    private const int MaxPerPage = 200;
    private const int MaxRetryAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly int[] RetryDelaysMs = { 500, 1500, 4000 };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;

    // Maxio throttles by concurrency (max 4 in-flight calls per site);
    // keep our side within that limit no matter how many app requests arrive.
    private readonly SemaphoreSlim _concurrencyGate = new(4, 4);

    public MaxioBillingService(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _options.Validate();
    }

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await ListProductsInFamilyAsync(_options.ProductFamilyHandle, cancellationToken);

        return products
            .Where(p => !p.ArchivedAt.HasValue && !string.IsNullOrEmpty(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .Select(ToPlanInfo)
            .ToList();
    }

    public async Task<SubscriptionCustomerResult> EnsureCustomerAsync(SubscriptionCustomerRequest customer, CancellationToken cancellationToken = default)
    {
        var reference = CustomerReference(customer.UserId);

        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return new SubscriptionCustomerResult((int)existing.Id, reference, created: false);
        }

        var body = new CreateCustomerBody
        {
            Customer = new CreateCustomerCustomer
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = reference
            }
        };

        try
        {
            var envelope = await SendJsonAsync<CreateCustomerBody, CustomerEnvelope>(
                HttpMethod.Post, "customers.json", body, expectNotFound: false, cancellationToken);
            return new SubscriptionCustomerResult((int)envelope.Customer.Id, reference, created: true);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422 && ex.IsDuplicateReferenceError)
        {
            // A concurrent signup created the customer first; reuse it.
            _logger.LogInformation("Maxio customer creation raced for reference {Reference}; re-reading existing customer.", reference);
            existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (existing is null)
            {
                throw;
            }
            return new SubscriptionCustomerResult((int)existing.Id, reference, created: false);
        }
    }

    public async Task<SubscriptionDetails> SubscribeAsync(SubscriptionSignupRequest signup, CancellationToken cancellationToken = default)
    {
        var customer = await EnsureCustomerAsync(
            new SubscriptionCustomerRequest(signup.UserId, signup.Email, signup.FirstName, signup.LastName),
            cancellationToken);

        var productHandle = signup.ProductHandle.Trim().ToLowerInvariant();

        // 1) Already has a live subscription to this product? Return it (idempotent subscribe).
        var live = await FindLiveSubscriptionByProductAsync(customer.MaxioCustomerId, productHandle, cancellationToken);
        if (live is not null)
        {
            return ToDetails(live, wasExisting: true);
        }

        // 2) Deterministic reference lets us detect subscriptions created by concurrent callers.
        var baseReference = SubscriptionReference(signup.UserId, productHandle);
        var existingByReference = await FindSubscriptionByReferenceAsync(baseReference, cancellationToken);
        if (existingByReference is not null && IsLive(existingByReference.State))
        {
            return ToDetails(existingByReference, wasExisting: true);
        }

        // 3) Create. If the deterministic reference is taken by an ended subscription
        //    (user re-subscribing after cancellation), suffix it to keep references unique.
        var reference = existingByReference is null
            ? baseReference
            : $"{baseReference}-{NewShortUniqueSuffix()}";

        var created = await CreateSubscriptionAsync(customer.MaxioCustomerId, productHandle, reference, cancellationToken);
        return ToDetails(created, wasExisting: false);
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var reference = CustomerReference(userId);
        var customer = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        var subscriptions = new List<SubscriptionWire>();
        var page = 1;
        while (true)
        {
            var batch = await GetListAsync<SubscriptionEnvelope>(
                $"customers/{customer.Id}/subscriptions.json?page={page}&per_page={MaxPerPage}", cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }
            subscriptions.AddRange(batch.Select(b => b.Subscription));
            if (batch.Count < MaxPerPage)
            {
                break;
            }
            page++;
        }

        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(ToSummary)
            .ToList();
    }

    // ----- reference helpers -----

    private static string CustomerReference(string userId) => $"{CustomerReferencePrefix}{userId}";

    private static string SubscriptionReference(string userId, string productHandle) =>
        $"{SubscriptionReferencePrefix}{userId}:{productHandle.ToLowerInvariant()}";

    /// <summary>
    /// Deterministic uniqueness token derived from the subscription reference: identical
    /// retries always carry the same token, so Maxio's duplicate-prevention (409 Conflict)
    /// can be relied on across process restarts, and we can recover by re-reading the resource.
    /// </summary>
    private static string DeterministicUniquenessToken(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16)).ToString("N");
    }

    private static string NewShortUniqueSuffix() => Guid.NewGuid().ToString("N").AsSpan(0, 8).ToString();

    private static bool IsLive(string? state) =>
        state is not null && LiveStates.Contains(state);

    // ----- Maxio operations -----

    private async Task<CustomerWire?> FindCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendJsonAsync<object?, CustomerEnvelope>(HttpMethod.Get, url, null, expectNotFound: true, ct);
        return envelope?.Customer;
    }

    private async Task<SubscriptionWire?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct)
    {
        var url = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendJsonAsync<object?, SubscriptionEnvelope>(HttpMethod.Get, url, null, expectNotFound: true, ct);
        return envelope?.Subscription;
    }

    private async Task<SubscriptionWire?> FindLiveSubscriptionByProductAsync(long customerId, string productHandle, CancellationToken ct)
    {
        var page = 1;
        while (true)
        {
            var batch = await GetListAsync<SubscriptionEnvelope>(
                $"customers/{customerId}/subscriptions.json?page={page}&per_page={MaxPerPage}", ct);
            if (batch.Count == 0)
            {
                return null;
            }

            var match = batch
                .Where(b => IsLive(b.Subscription.State))
                .Where(b => string.Equals(b.Subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
                .Select(b => b.Subscription)
                .FirstOrDefault();
            if (match is not null)
            {
                return match;
            }

            if (batch.Count < MaxPerPage)
            {
                return null;
            }
            page++;
        }
    }

    private async Task<SubscriptionWire> CreateSubscriptionAsync(long customerId, string productHandle, string reference, CancellationToken ct)
    {
        var body = new CreateSubscriptionBody
        {
            Subscription = new CreateSubscriptionSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = "remittance",
                Reference = reference,
                UniquenessToken = DeterministicUniquenessToken(reference)
            }
        };

        try
        {
            var envelope = await SendJsonAsync<CreateSubscriptionBody, SubscriptionEnvelope>(
                HttpMethod.Post, "subscriptions.json", body, expectNotFound: false, ct);
            return envelope.Subscription;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.Conflict
                                           || (ex.StatusCode == 422 && ex.IsDuplicateReferenceError))
        {
            // The subscription was created by a concurrent caller: either Maxio's duplicate
            // prevention fired (409 - same uniqueness token retried) or the deterministic
            // subscription reference was already taken (422). Recover by reading the
            // subscription the winning request created.
            _logger.LogWarning("Maxio reports duplicate subscription for reference {Reference}; re-reading existing subscription.", reference);
            var existing = await FindSubscriptionByReferenceAsync(reference, ct);
            if (existing is not null && IsLive(existing.State))
            {
                return existing;
            }
            throw;
        }
    }

    private async Task<List<ProductWire>> ListProductsInFamilyAsync(string familyHandle, CancellationToken ct)
    {
        var products = new List<ProductWire>();
        var page = 1;
        while (true)
        {
            var batch = await GetListAsync<ProductEnvelope>(
                $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?page={page}&per_page={MaxPerPage}", ct);
            if (batch.Count == 0)
            {
                break;
            }
            products.AddRange(batch.Select(b => b.Product));
            if (batch.Count < MaxPerPage)
            {
                break;
            }
            page++;
        }
        return products;
    }

    private async Task<List<T>> GetListAsync<T>(string url, CancellationToken ct) where T : class
    {
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        await EnsureSuccessAsync(response, url, ct);
        var list = await response.Content.ReadFromJsonAsync<List<T>>(JsonOptions, ct);
        return list ?? new List<T>();
    }

    private async Task<TResult> SendJsonAsync<TRequest, TResult>(
        HttpMethod method, string url, TRequest? body, bool expectNotFound, CancellationToken ct)
        where TResult : class
    {
        HttpResponseMessage? response = null;
        try
        {
            response = await SendWithRetryAsync(() =>
            {
                var request = new HttpRequestMessage(method, url);
                if (body is not null)
                {
                    request.Content = JsonContent.Create(body, options: JsonOptions);
                }
                return request;
            }, ct);

            if (expectNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null!;
            }

            await EnsureSuccessAsync(response, url, ct);
            var result = await response.Content.ReadFromJsonAsync<TResult>(JsonOptions, ct);
            return result ?? throw new MaxioApiException((int)response.StatusCode, url, "Maxio returned an empty response body.");
        }
        finally
        {
            response?.Dispose();
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> createRequest, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                await _concurrencyGate.WaitAsync(ct);
                try
                {
                    using var request = createRequest();
                    response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                }
                finally
                {
                    _concurrencyGate.Release();
                }
            }
            catch (Exception ex) when (attempt < MaxRetryAttempts &&
                                       (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested)))
            {
                _logger.LogWarning(ex, "Transient failure calling Maxio (attempt {Attempt}); retrying.", attempt + 1);
                await Task.Delay(RetryDelaysMs[Math.Min(attempt, RetryDelaysMs.Length - 1)], ct);
                continue;
            }

            if (IsRetryableStatus(response.StatusCode) && attempt < MaxRetryAttempts)
            {
                _logger.LogWarning("Maxio returned retryable status {StatusCode} (attempt {Attempt}); retrying.", (int)response.StatusCode, attempt + 1);
                await Task.Delay(RetryDelaysMs[Math.Min(attempt, RetryDelaysMs.Length - 1)], ct);
                continue;
            }

            return response;
        }
    }

    private static bool IsRetryableStatus(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests
        || statusCode == HttpStatusCode.RequestTimeout
        || (int)statusCode >= 500;

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string url, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = await ReadErrorsAsync(response, ct);
        throw new MaxioApiException((int)response.StatusCode, url, errors);
    }

    /// <summary>
    /// Maxio error bodies are JSON like {"errors": ["..."]} or {"errors": "..."}; fall back to the raw body.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new[] { $"HTTP {(int)response.StatusCode} with empty body." };
            }

            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("errors", out var errors))
                {
                    if (errors.ValueKind == JsonValueKind.Array)
                    {
                        return errors.EnumerateArray()
                            .Select(e => e.ToString())
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList();
                    }
                    return new[] { errors.ToString() };
                }
            }
            catch (JsonException)
            {
                // not JSON - use raw text
            }

            return new[] { raw.Length > 512 ? raw[..512] : raw };
        }
        catch (Exception)
        {
            return new[] { $"HTTP {(int)response.StatusCode} {response.StatusCode}." };
        }
    }

    // ----- mapping -----

    private static SubscriptionPlanInfo ToPlanInfo(ProductWire product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        Price = FromCents(product.PriceInCents),
        Currency = string.Empty,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        Taxable = product.Taxable
    };

    private static SubscriptionDetails ToDetails(SubscriptionWire subscription, bool wasExisting) => new()
    {
        SubscriptionId = (int)subscription.Id,
        State = subscription.State,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        Price = FromCents(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0),
        Currency = subscription.Currency ?? string.Empty,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        MaxioCustomerId = (int)(subscription.Customer?.Id ?? 0),
        WasExisting = wasExisting
    };

    private static SubscriptionSummary ToSummary(SubscriptionWire subscription) => new()
    {
        SubscriptionId = (int)subscription.Id,
        State = subscription.State,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        Price = FromCents(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0),
        Currency = subscription.Currency ?? string.Empty,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt ?? DateTimeOffset.MinValue
    };

    private static decimal FromCents(long cents) => cents / 100m;
}