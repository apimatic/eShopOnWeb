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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="IMaxioBillingService"/> implementation over the Maxio Advanced Billing
/// (Chargify) REST API using a typed <see cref="HttpClient"/>.
///
/// Idempotency design (Maxio does not honour an Idempotency-Key header, verified against
/// the sandbox):
///  * Customer: the eShopOnWeb user id is used as a unique Maxio customer <c>reference</c>,
///    with a lookup-then-create pattern (Maxio enforces reference uniqueness with a 422).
///  * Subscription: before creating, we check the customer's existing subscriptions for a
///    live one on the same plan and return it instead of creating a duplicate.
///  * A per-reference in-process lock serialises concurrent subscribe calls (e.g. a
///    double-click) so the check-then-create sequence cannot race within a run.
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    private const string PaymentCollectionMethod = "remittance"; // card-less: customer is invoiced
    private const string ReferencePrefix = "eshoponweb:";
    private static readonly TimeSpan FamilyCacheDuration = TimeSpan.FromMinutes(30);

    // Subscription states that mean the user is still enrolled; anything else (canceled /
    // expired) is terminal and allows re-subscribing.
    private static readonly HashSet<string> TerminalStates =
        new(StringComparer.OrdinalIgnoreCase) { "canceled", "cancelled", "expired" };

    // Per-reference locks shared process-wide across the transient service instances.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioBillingService> _logger;
    private readonly MaxioOptions _options;

    public MaxioBillingService(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        IMemoryCache cache,
        ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
        _options = options.Value;
        _options.Validate();

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(_options.ResolveBaseUrl() + "/");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (_httpClient.Timeout == TimeSpan.FromSeconds(100)) // default; only set if untouched
                _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        var envelopes = await GetAsync<List<MaxioProductEnvelope>>(
            $"product_families/{familyId}/products.json", cancellationToken) ?? new();

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && p!.ArchivedAt is null)
            .Select(p => MapPlan(p!))
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new PlanNotFoundException(planHandle ?? string.Empty);

        // Validate the plan against the configured family so callers cannot subscribe to
        // arbitrary products, and so a bad handle yields a clean 400 rather than a 422.
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
            throw new PlanNotFoundException(planHandle);

        var reference = BuildReference(subscriber.UserId);
        var gate = SubscribeLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(subscriber, reference, cancellationToken);

            var existing = (await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                .FirstOrDefault(s =>
                    string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                    !IsTerminal(s.State));

            if (existing is not null)
            {
                _logger.LogInformation(
                    "Maxio subscribe is a no-op: customer {CustomerId} already has live subscription {SubscriptionId} on plan {PlanHandle}.",
                    customer.Id, existing.Id, plan.Handle);
                return new SubscribeResult(MapSubscription(existing), alreadySubscribed: true);
            }

            var request = new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioSubscriptionAttributes
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    PaymentCollectionMethod = PaymentCollectionMethod
                }
            };

            var envelope = await PostAsync<MaxioSubscriptionEnvelope>("subscriptions.json", request, cancellationToken);
            var created = envelope?.Subscription
                ?? throw new MaxioBillingException("Maxio returned an empty subscription response.");

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} ({State}) for customer {CustomerId} on plan {PlanHandle}.",
                created.Id, created.State, customer.Id, plan.Handle);

            return new SubscribeResult(MapSubscription(created), alreadySubscribed: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        var reference = BuildReference(subscriber.UserId);
        var customer = await LookupCustomerAsync(reference, cancellationToken);
        if (customer is null)
            return Array.Empty<CustomerSubscription>();

        var subs = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subs
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(MapSubscription)
            .ToList();
    }

    // ---------------------------------------------------------------- customers

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        SubscriberIdentity subscriber, string reference, CancellationToken cancellationToken)
    {
        var existing = await LookupCustomerAsync(reference, cancellationToken);
        if (existing is not null)
            return existing;

        var (firstName, lastName) = DeriveName(subscriber);
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = subscriber.Email,
                Reference = reference
            }
        };

        try
        {
            var envelope = await PostAsync<MaxioCustomerEnvelope>("customers.json", request, cancellationToken);
            var created = envelope?.Customer
                ?? throw new MaxioBillingException("Maxio returned an empty customer response.");
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.", created.Id, reference);
            return created;
        }
        catch (MaxioBillingException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent create (reference must be unique -> 422).
            // Re-lookup and use the customer the other request created.
            var raced = await LookupCustomerAsync(reference, cancellationToken);
            if (raced is not null)
                return raced;
            throw;
        }
    }

    private async Task<MaxioCustomer?> LookupCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    private async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken) ?? new();
        return envelopes.Select(e => e.Subscription).Where(s => s is not null).Select(s => s!).ToList();
    }

    // ---------------------------------------------------------------- product family

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        var handle = _options.ProductFamilyHandle;
        var cacheKey = $"maxio:family-id:{_options.Subdomain}:{handle}";
        if (_cache.TryGetValue(cacheKey, out int cached))
            return cached;

        var envelopes = await GetAsync<List<MaxioProductFamilyEnvelope>>("product_families.json", cancellationToken) ?? new();
        var match = envelopes
            .Select(e => e.ProductFamily)
            .FirstOrDefault(f => f is not null && string.Equals(f!.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            throw new MaxioBillingException(
                $"No Maxio product family with handle '{handle}' was found on site '{_options.Subdomain}'.");

        _cache.Set(cacheKey, match.Id, FamilyCacheDuration);
        return match.Id;
    }

    // ---------------------------------------------------------------- HTTP plumbing

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private async Task<T?> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await SendAsync(HttpMethod.Post, path, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    /// <summary>
    /// Sends a request, retrying transient failures (network errors / timeouts / 5xx) for
    /// safe GET requests only. POSTs are never retried to avoid duplicate side effects.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        var isRetryable = method == HttpMethod.Get;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, path);
                if (content is not null)
                    request.Content = content;

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (isRetryable && attempt < maxAttempts && (int)response.StatusCode >= 500)
                {
                    response.Dispose();
                    await DelayBeforeRetryAsync(attempt, cancellationToken);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException ex) when (isRetryable && attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "Transient error calling Maxio {Method} {Path} (attempt {Attempt}); retrying.", method, path, attempt);
                await DelayBeforeRetryAsync(attempt, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && isRetryable && attempt < maxAttempts)
            {
                _logger.LogWarning("Timeout calling Maxio {Method} {Path} (attempt {Attempt}); retrying.", method, path, attempt);
                await DelayBeforeRetryAsync(attempt, cancellationToken);
            }
        }
    }

    private static Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ParseErrors(body);
        var status = (int)response.StatusCode;
        var summary = errors.Count > 0 ? string.Join("; ", errors) : response.ReasonPhrase;

        _logger.LogError("Maxio API call failed with {StatusCode}: {Errors}", status, summary);
        throw new MaxioBillingException($"Maxio API request failed ({status}): {summary}", status, errors);
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        if (stream.CanSeek && stream.Length == 0)
            return default;
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    /// <summary>
    /// Parses Maxio error bodies. Legacy endpoints return {"errors":["msg", ...]}; some newer
    /// endpoints return {"errors":{"field":["msg"]}}. Both shapes are handled defensively.
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string body)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
            return messages;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
                return messages;

            switch (errors.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in errors.EnumerateArray())
                        AddError(messages, item);
                    break;
                case JsonValueKind.Object:
                    foreach (var property in errors.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.Array)
                            foreach (var item in property.Value.EnumerateArray())
                                messages.Add($"{property.Name}: {item}");
                        else
                            messages.Add($"{property.Name}: {property.Value}");
                    }
                    break;
                case JsonValueKind.String:
                    messages.Add(errors.GetString()!);
                    break;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body (e.g. an HTML 404/401 page) — status code carries the meaning.
        }

        return messages;
    }

    private static void AddError(List<string> messages, JsonElement item)
    {
        var value = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
        if (!string.IsNullOrWhiteSpace(value))
            messages.Add(value!);
    }

    // ---------------------------------------------------------------- mapping / helpers

    private static string BuildReference(string userId) => ReferencePrefix + userId;

    private static (string FirstName, string LastName) DeriveName(SubscriberIdentity subscriber)
    {
        var localPart = subscriber.Email.Split('@')[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;
        return (firstName, "eShopOnWeb");
    }

    private static bool IsTerminal(string? state) => state is not null && TerminalStates.Contains(state);

    private SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? _options.ProductFamilyHandle,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        ProductPriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency ?? "USD",
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference ?? string.Empty,
        CreatedAt = subscription.CreatedAt
    };
}
