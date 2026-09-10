using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Talks to the Maxio (Chargify) HTTP API. Registered as a typed <see cref="HttpClient"/>; the
/// base address and Basic-auth credentials are configured at registration time
/// (see <c>MaxioServiceCollectionExtensions</c>).
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    // Subscription states that represent a shopper who is currently enrolled. Used to decide,
    // when subscribing, whether an equivalent subscription already exists (idempotency).
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "past_due", "soft_failure", "paused"
    };

    // Serializes subscribe operations per subscriber so a double-submit cannot race the
    // "already subscribed?" check into creating two customers/subscriptions. Process-local,
    // which matches this app's single-process, single-run deployment model.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriberLocks = new();

    private const int MaxAttempts = 3;

    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;
    private readonly JsonSerializerOptions _json;

    public MaxioBillingService(
        HttpClient http,
        MaxioSettings settings,
        ILogger<MaxioBillingService> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(_settings.ProductFamilyHandle)}/products.json?per_page=200";
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);
        await EnsureSuccessAsync(response, "list subscription plans", cancellationToken);

        var envelopes = await ReadAsync<List<MaxioProductEnvelope>>(response, cancellationToken) ?? new();

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan!)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        BillingSubscriber subscriber,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.Validation, "A plan handle is required to subscribe.");
        }

        // Validate the requested plan against the configured catalog so an unknown/archived
        // handle yields a clean not-found rather than an opaque Maxio validation error.
        var plans = await GetPlansAsync(cancellationToken);
        if (plans.All(p => !string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new MaxioBillingException(
                MaxioBillingErrorKind.NotFound,
                $"Subscription plan '{planHandle}' was not found in product family '{_settings.ProductFamilyHandle}'.");
        }

        var gate = SubscriberLocks.GetOrAdd(subscriber.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customerId = await EnsureCustomerAsync(subscriber, cancellationToken);

            // Idempotency: if the subscriber already has a live subscription to this plan, return it.
            var existing = (await ListCustomerSubscriptionsAsync(customerId, cancellationToken))
                .FirstOrDefault(s =>
                    string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
                    s.State is not null && LiveStates.Contains(s.State));
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Subscriber {UserId} already subscribed to {PlanHandle} (subscription {SubscriptionId}); returning existing.",
                    subscriber.UserId, planHandle, existing.Id);
                return MapSubscription(existing, alreadyExisted: true);
            }

            // Fresh token per call (reused across this call's transient retries) so a timed-out
            // create that actually succeeded is recovered as a duplicate rather than duplicated.
            var uniquenessToken = Guid.NewGuid().ToString("N");
            var payload = new
            {
                subscription = new
                {
                    productHandle = planHandle,
                    customerId,
                    paymentCollectionMethod = _settings.PaymentCollectionMethod,
                    uniquenessToken
                }
            };

            using var response = await SendAsync(
                () => JsonRequest(HttpMethod.Post, "subscriptions.json", payload),
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                // Duplicate-prevention: the original request succeeded. Recover the created subscription.
                _logger.LogWarning(
                    "Duplicate subscription submission detected for subscriber {UserId} / {PlanHandle}; recovering existing subscription.",
                    subscriber.UserId, planHandle);
                var recovered = (await ListCustomerSubscriptionsAsync(customerId, cancellationToken))
                    .FirstOrDefault(s => string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
                if (recovered is not null)
                {
                    return MapSubscription(recovered, alreadyExisted: true);
                }
            }

            await EnsureSuccessAsync(response, "create subscription", cancellationToken);

            var created = await ReadAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
            if (created?.Subscription is null)
            {
                throw new MaxioBillingException(
                    MaxioBillingErrorKind.Upstream,
                    "Maxio returned an empty response when creating the subscription.");
            }

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for subscriber {UserId} on plan {PlanHandle}.",
                created.Subscription.Id, subscriber.UserId, planHandle);
            return MapSubscription(created.Subscription, alreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        BillingSubscriber subscriber,
        CancellationToken cancellationToken = default)
    {
        var customerId = await FindCustomerIdAsync(subscriber.UserId, cancellationToken);
        if (customerId is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId.Value, cancellationToken);
        return subscriptions.Select(s => MapSubscription(s, alreadyExisted: true)).ToList();
    }

    // -- Customer provisioning -------------------------------------------------------------

    private async Task<long> EnsureCustomerAsync(BillingSubscriber subscriber, CancellationToken cancellationToken)
    {
        var existingId = await FindCustomerIdAsync(subscriber.UserId, cancellationToken);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        var (firstName, lastName) = ResolveName(subscriber);
        var payload = new
        {
            customer = new
            {
                firstName,
                lastName,
                email = subscriber.Email,
                reference = subscriber.UserId,
                uniquenessToken = DeterministicToken("customer", subscriber.UserId)
            }
        };

        using var response = await SendAsync(
            () => JsonRequest(HttpMethod.Post, "customers.json", payload),
            cancellationToken);

        // A 409 (duplicate submission) or 422 (reference already taken) both mean a customer
        // for this reference now exists — recover it rather than failing.
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            var recoveredId = await FindCustomerIdAsync(subscriber.UserId, cancellationToken);
            if (recoveredId is not null)
            {
                _logger.LogInformation(
                    "Concurrent customer creation for subscriber {UserId}; using existing customer {CustomerId}.",
                    subscriber.UserId, recoveredId.Value);
                return recoveredId.Value;
            }
        }

        await EnsureSuccessAsync(response, "create customer", cancellationToken);

        var created = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        if (created?.Customer is null || created.Customer.Id == 0)
        {
            throw new MaxioBillingException(
                MaxioBillingErrorKind.Upstream,
                "Maxio returned an empty response when creating the customer.");
        }

        _logger.LogInformation(
            "Created Maxio customer {CustomerId} for subscriber {UserId}.",
            created.Customer.Id, subscriber.UserId);
        return created.Customer.Id;
    }

    private async Task<long?> FindCustomerIdAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "look up customer", cancellationToken);

        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer?.Id;
    }

    private async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);
        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);

        var envelopes = await ReadAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken) ?? new();
        return envelopes.Select(e => e.Subscription).Where(s => s is not null).Select(s => s!).ToList();
    }

    // -- HTTP plumbing ---------------------------------------------------------------------

    private HttpRequestMessage JsonRequest(HttpMethod method, string path, object body)
        => new(method, path) { Content = JsonContent.Create(body, options: _json) };

    /// <summary>
    /// Sends a request with bounded retries for transient failures (HTTP 429 and 5xx). The
    /// request is rebuilt on each attempt because <see cref="HttpRequestMessage"/> is single-use.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 1; ; attempt++)
        {
            HttpRequestMessage request = requestFactory();
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "Maxio request to {Path} failed (attempt {Attempt}); retrying.", request.RequestUri, attempt);
                await DelayForRetryAsync(null, attempt, cancellationToken);
                request.Dispose();
                continue;
            }
            catch (HttpRequestException ex)
            {
                request.Dispose();
                throw new MaxioBillingException(
                    MaxioBillingErrorKind.Upstream,
                    "Unable to reach the Maxio billing service.",
                    innerException: ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                request.Dispose();
                throw new MaxioBillingException(
                    MaxioBillingErrorKind.Upstream,
                    "The Maxio billing service did not respond in time.",
                    innerException: ex);
            }

            request.Dispose();

            var transient = response.StatusCode == HttpStatusCode.TooManyRequests ||
                            (int)response.StatusCode >= 500;
            if (transient && attempt < MaxAttempts)
            {
                _logger.LogWarning(
                    "Maxio returned {StatusCode} for {Path} (attempt {Attempt}); retrying.",
                    (int)response.StatusCode, response.RequestMessage?.RequestUri, attempt);
                var retryAfter = response.Headers.RetryAfter?.Delta;
                response.Dispose();
                await DelayForRetryAsync(retryAfter, attempt, cancellationToken);
                continue;
            }

            return response;
        }
    }

    private static Task DelayForRetryAsync(TimeSpan? retryAfter, int attempt, CancellationToken cancellationToken)
    {
        var delay = retryAfter ?? TimeSpan.FromMilliseconds(200 * attempt);
        return Task.Delay(delay, cancellationToken);
    }

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(_json, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException(
                MaxioBillingErrorKind.Upstream,
                "Maxio returned a response that could not be understood.",
                (int)response.StatusCode,
                innerException: ex);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = await TryReadErrorsAsync(response, cancellationToken);
        var status = (int)response.StatusCode;
        var kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => MaxioBillingErrorKind.Configuration,
            HttpStatusCode.NotFound => MaxioBillingErrorKind.NotFound,
            HttpStatusCode.UnprocessableEntity => MaxioBillingErrorKind.Validation,
            _ => MaxioBillingErrorKind.Upstream
        };

        var detail = errors.Count > 0 ? string.Join("; ", errors) : response.ReasonPhrase;
        _logger.LogError(
            "Maxio request to {Operation} failed with {StatusCode}: {Detail}", operation, status, detail);

        throw new MaxioBillingException(
            kind,
            $"Failed to {operation} (Maxio returned {status}).",
            status,
            errors);
    }

    private async Task<IReadOnlyList<string>> TryReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<MaxioErrorEnvelope>(_json, cancellationToken);
            return envelope?.Errors ?? new List<string>();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return Array.Empty<string>();
        }
    }

    // -- Mapping ---------------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        IntervalUnit = product.IntervalUnit ?? "month",
        IntervalCount = product.Interval,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription, bool alreadyExisted) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? "unknown",
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.Product?.PriceInCents ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit,
        IntervalCount = subscription.Product?.Interval ?? 0,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        AlreadyExisted = alreadyExisted
    };

    private static (string FirstName, string LastName) ResolveName(BillingSubscriber subscriber)
    {
        var first = subscriber.FirstName;
        var last = subscriber.LastName;
        if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(last))
        {
            return (first!, last!);
        }

        // eShopOnWeb users have no name fields; derive a reasonable display name from the email
        // so Maxio's required first/last name fields are satisfied deterministically.
        var localPart = subscriber.Email.Split('@')[0];
        first = string.IsNullOrWhiteSpace(first) ? localPart : first;
        last = string.IsNullOrWhiteSpace(last) ? "eShopOnWeb" : last;
        return (first!, last!);
    }

    private static string DeterministicToken(string scope, string value)
    {
        // Stable per (scope, value) UUID so a retried create for the same logical entity is
        // recognised by Maxio's duplicate prevention within its 60-minute window.
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{scope}:{value}"));
        return new Guid(bytes).ToString("N");
    }
}
