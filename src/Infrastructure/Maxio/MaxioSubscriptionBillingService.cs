using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using global::Maxio;
using global::Maxio.Core.ErrorResponse;
using global::Maxio.Core.Exceptions;
using global::Maxio.Errors;
using global::Maxio.Models;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by the Maxio Advanced Billing .NET SDK. Every SDK
/// call is bounded by a total per-call deadline and wrapped in an error boundary that classifies
/// provider, transport and deserialization failures into a single <see cref="SubscriptionBillingException"/>,
/// so no SDK type or raw provider message ever escapes this class.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Subscription states in which a subscription is considered live/active — used to decide whether a
    // subscriber already has a subscription to a plan (idempotent subscribe).
    private static readonly HashSet<string> LiveStates =
        new(StringComparer.OrdinalIgnoreCase) { "active", "trialing", "assessing", "pending", "paused" };

    // Serializes a single user's concurrent subscribe calls within this process, so a double-click
    // cannot race two customer/subscription creates. Cross-process races are out of scope (single host).
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new(StringComparer.Ordinal);

    private readonly MaxioClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken ct)
    {
        using var cts = Bound(ct);
        var products = await ListFamilyPlansAsync(cts.Token, ct);
        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle) && p.ArchivedAt is null)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string? planHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(subscriber.UserName))
        {
            throw new SubscriptionBillingException(BillingErrorKind.Validation, "No authenticated subscriber.");
        }

        // Resolve the target plan handle: request value, else the configured default. Never hard-coded.
        var handle = string.IsNullOrWhiteSpace(planHandle) ? _settings.DefaultPlanHandle : planHandle!.Trim();
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new SubscriptionBillingException(BillingErrorKind.Validation,
                "No plan was specified and no default plan is configured.");
        }

        var gate = _userLocks.GetOrAdd(subscriber.UserName, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            using var cts = Bound(ct);
            var token = cts.Token;

            // Validate the requested plan belongs to the configured family, and keep it for the response.
            var plans = await ListFamilyPlansAsync(token, ct);
            var plan = plans.FirstOrDefault(p =>
                string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase) && p.ArchivedAt is null);
            if (plan is null)
            {
                throw new SubscriptionBillingException(BillingErrorKind.Validation,
                    $"Plan '{handle}' is not an available plan.");
            }

            // 1. Ensure the Maxio customer exists (idempotent on the customer reference = user name).
            var customerId = await EnsureCustomerAsync(subscriber, token, ct);

            // 2. Idempotency: if a live subscription to this plan already exists, return it — no duplicate.
            var existing = await FindLiveSubscriptionAsync(customerId, plan.Handle!, token, ct);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Maxio subscribe no-op: user {User} already has live subscription {SubscriptionId} to plan {Plan}",
                    subscriber.UserName, existing.Id, plan.Handle);
                return new SubscribeResult { Subscription = existing, AlreadySubscribed = true };
            }

            // 3. Create the subscription. Plans require no payment method, so no card/3-DS is captured.
            var created = await CreateSubscriptionAsync(subscriber, customerId, plan.Handle!, token, ct);
            _logger.LogInformation(
                "Maxio subscription {SubscriptionId} created for user {User} on plan {Plan} (state {State})",
                created.Id, subscriber.UserName, created.PlanHandle, created.State);
            return new SubscribeResult { Subscription = created, AlreadySubscribed = false };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken ct)
    {
        using var cts = Bound(ct);
        var token = cts.Token;

        var customer = await ReadCustomerByReferenceAsync(subscriber.UserName, token, ct);
        if (customer?.Id is not int customerId)
        {
            return Array.Empty<BillingSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, token, ct);
        return subscriptions
            .Select(r => r.Subscription)
            .Where(s => s is { Id: not null })
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    // --- SDK call wrappers (each owns its error boundary) -------------------------------------------

    private async Task<IReadOnlyList<Product>> ListFamilyPlansAsync(CancellationToken token, CancellationToken outerCt)
    {
        try
        {
            var responses = await _client.ProductFamilies.ListProductsForProductFamily(
                // productFamilyId accepts either the numeric id or the handle prefixed with "handle:"
                // (per the SDK's own param docs). We key on the stable handle.
                productFamilyId: $"handle:{_settings.ProductFamilyHandle}",
                dateField: null, filter: null,
                startDate: null, endDate: null, startDatetime: null, endDatetime: null,
                includeArchived: false, include: null,
                perPage: 200,
                ct: token);
            // Each item wraps the product under `.Product` (required, non-null).
            return responses.Select(r => r.Product).ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var body))
            {
                _logger.LogWarning("Maxio product family '{Family}' not found: {Body}",
                    _settings.ProductFamilyHandle, Truncate(body));
                throw new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                    "The billing catalog is not available.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRaw(raw, "list plans", ex);
            }

            throw new SubscriptionBillingException(BillingErrorKind.Unknown,
                "The billing provider returned an unrecognized error.", ex);
        }
        catch (OperationCanceledException) when (outerCt.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw TranslateGeneric(ex, "list plans"); }
    }

    private async Task<int> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken token, CancellationToken outerCt)
    {
        var existing = await ReadCustomerByReferenceAsync(subscriber.UserName, token, outerCt);
        if (existing?.Id is int id)
        {
            return id;
        }

        var (firstName, lastName) = DeriveName(subscriber);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = string.IsNullOrWhiteSpace(subscriber.Email) ? subscriber.UserName : subscriber.Email,
                Reference = subscriber.UserName,
            },
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body, ct: token);
            var newId = response.Customer.Id;
            if (newId is not int created)
            {
                throw new SubscriptionBillingException(BillingErrorKind.Unknown,
                    "The billing provider created a customer without an id.");
            }

            _logger.LogInformation("Maxio customer {CustomerId} created for user {User}", created, subscriber.UserName);
            return created;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A 422 here is most likely a duplicate reference from a concurrent (cross-process) creator —
            // re-read by reference and use the existing customer if it now resolves.
            if (ex.Error.TryGetCustomerErrorResponse1(out var typed))
            {
                var reread = await ReadCustomerByReferenceAsync(subscriber.UserName, token, outerCt);
                if (reread?.Id is int recovered)
                {
                    return recovered;
                }

                throw new SubscriptionBillingException(BillingErrorKind.Validation,
                    DescribeCustomerError(typed), ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRaw(raw, "create customer", ex);
            }

            throw new SubscriptionBillingException(BillingErrorKind.Unknown,
                "The billing provider returned an unrecognized error.", ex);
        }
        catch (SubscriptionBillingException) { throw; }
        catch (OperationCanceledException) when (outerCt.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw TranslateGeneric(ex, "create customer"); }
    }

    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken token, CancellationToken outerCt)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: token);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // A genuine miss — the customer does not exist yet. Distinct from an unreadable response.
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, "read customer", ex);
        }
        catch (OperationCanceledException) when (outerCt.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw TranslateGeneric(ex, "read customer"); }
    }

    private async Task<BillingSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken token, CancellationToken outerCt)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, token, outerCt);
        var match = subscriptions
            .Select(r => r.Subscription)
            .FirstOrDefault(s =>
                s is { Id: not null }
                && string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
                && s.State?.Value is string state && LiveStates.Contains(state));
        return match is null ? null : MapSubscription(match);
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken token, CancellationToken outerCt)
    {
        try
        {
            return await _client.Customers.ListCustomerSubscriptions(customerId, ct: token);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, "list customer subscriptions", ex);
        }
        catch (OperationCanceledException) when (outerCt.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw TranslateGeneric(ex, "list customer subscriptions"); }
    }

    private async Task<BillingSubscription> CreateSubscriptionAsync(SubscriberIdentity subscriber, int customerId, string planHandle, CancellationToken token, CancellationToken outerCt)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                // Bill by invoice/remittance so signup does not require a stored card (the plans are
                // configured payment-method-not-required). Configurable per site architecture.
                PaymentCollectionMethod = global::Maxio.Models.Enums.CollectionMethod.FromValue(_settings.PaymentCollectionMethod),
                // Deterministic subscription reference ties this subscription to (user, plan).
                Reference = $"{subscriber.UserName}:{planHandle}",
            },
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct: token);
            if (response.Subscription is not { } subscription || subscription.Id is null)
            {
                throw new SubscriptionBillingException(BillingErrorKind.Unknown,
                    "The billing provider created a subscription without an id.");
            }

            return MapSubscription(subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var typed))
            {
                var message = typed.Errors is { Count: > 0 }
                    ? string.Join("; ", typed.Errors)
                    : "The subscription request was rejected.";
                _logger.LogWarning("Maxio create subscription rejected for user {User}: {Message}",
                    subscriber.UserName, Truncate(message));
                throw new SubscriptionBillingException(BillingErrorKind.Validation, message, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRaw(raw, "create subscription", ex);
            }

            throw new SubscriptionBillingException(BillingErrorKind.Unknown,
                "The billing provider returned an unrecognized error.", ex);
        }
        catch (SubscriptionBillingException) { throw; }
        catch (OperationCanceledException) when (outerCt.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw TranslateGeneric(ex, "create subscription"); }
    }

    // --- Mapping -----------------------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(Product p) => new()
    {
        Handle = p.Handle!,
        Name = p.Name,
        Description = p.Description,
        PriceInCents = p.PriceInCents,
        Interval = p.Interval,
        IntervalUnit = p.IntervalUnit?.Value,
    };

    private static BillingSubscription MapSubscription(Subscription s) => new()
    {
        Id = s.Id!.Value,
        PlanHandle = s.Product?.Handle,
        PlanName = s.Product?.Name,
        PriceInCents = s.ProductPriceInCents ?? s.Product?.PriceInCents,
        State = s.State?.Value,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        NextBillingDate = s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        CreatedAt = s.CreatedAt,
    };

    // --- Error translation -------------------------------------------------------------------------

    private SubscriptionBillingException TranslateRaw(RawError raw, string operation, Exception inner)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning("Maxio {Operation} failed: HTTP {Status} {Body}",
            operation, status, Truncate(SafeReadBody(raw)));

        return status switch
        {
            401 or 403 => new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                "The billing provider rejected our credentials.", inner),
            429 => new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                "The billing provider is temporarily unavailable. Please try again shortly.", inner),
            404 => new SubscriptionBillingException(BillingErrorKind.NotFound,
                "The requested billing resource was not found.", inner),
            >= 400 and < 500 => new SubscriptionBillingException(BillingErrorKind.Validation,
                "The billing provider rejected the request.", inner),
            _ => new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                "The billing provider is currently unavailable.", inner),
        };
    }

    private SubscriptionBillingException TranslateGeneric(Exception ex, string operation)
    {
        // A 2xx body that no longer matches the model, or a non-2xx body that does not match the
        // operation's typed error shape, both surface here as JsonException (never as SdkException).
        if (ex is JsonException)
        {
            _logger.LogError(ex, "Maxio {Operation} returned an unprocessable response", operation);
            return new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                "The billing provider returned a response that could not be processed.", ex);
        }

        // Our budget elapsed (not the caller's cancellation — that is rethrown before reaching here).
        if (ex is OperationCanceledException)
        {
            _logger.LogWarning(ex, "Maxio {Operation} timed out", operation);
            return new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                "The billing provider did not respond in time. Please try again.", ex);
        }

        if (ex is HttpRequestException)
        {
            _logger.LogError(ex, "Maxio {Operation} could not reach the provider", operation);
            return new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                "The billing provider is unreachable. Please try again shortly.", ex);
        }

        _logger.LogError(ex, "Maxio {Operation} failed unexpectedly", operation);
        return new SubscriptionBillingException(BillingErrorKind.Unknown,
            "An unexpected billing error occurred.", ex);
    }

    private static string DescribeCustomerError(CustomerErrorResponse1 typed)
    {
        if (typed.Errors is { } errors)
        {
            if (errors.TryGetListOfString(out var list) && list is { Count: > 0 })
            {
                return string.Join("; ", list);
            }
        }

        return "The customer could not be created.";
    }

    // --- Helpers -----------------------------------------------------------------------------------

    private CancellationTokenSource Bound(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));
        return cts;
    }

    private static (string FirstName, string LastName) DeriveName(SubscriberIdentity subscriber)
    {
        var source = string.IsNullOrWhiteSpace(subscriber.Email) ? subscriber.UserName : subscriber.Email;
        var local = source.Contains('@') ? source[..source.IndexOf('@')] : source;
        var first = string.IsNullOrWhiteSpace(local) ? "eShop" : local;
        return (first, "Subscriber");
    }

    private static string SafeReadBody(RawError raw)
    {
        try { return raw.ReadAsString(); }
        catch { return "<unreadable>"; }
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= 500 ? value : value[..500];
    }
}
