using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by the Maxio Advanced Billing .NET SDK. It keeps the SDK
/// entirely behind the ApplicationCore abstraction: every provider call is bounded by a whole-operation
/// deadline and every failure is translated to a <see cref="MaxioBillingException"/> carrying a caller-safe
/// message.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Subscription lifecycle states after which a fresh subscription to the same plan is allowed again.
    // Anything else is treated as an existing enrollment, so a repeat subscribe is idempotent.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended",
    };

    // A whole-operation deadline. RetryOptions.Timeout is per-attempt; only a CancellationToken bounds the
    // full call (customer lookup + create + list + subscribe), so it is enforced here, not on the SDK options.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // Per-customer-reference gate: serializes concurrent subscribe attempts for the same user within the
    // process, so a double-click cannot race two customer/subscription creates. This is the in-process half
    // of the idempotency story; the read-before-create checks are the cross-restart half.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _customerGates = new(StringComparer.OrdinalIgnoreCase);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        MaxioSettings settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        using var budget = CreateBudget(cancellationToken);
        var token = budget.Token;

        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: RequireFamilySelector(),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 200,
                ct: token);

            var plans = products
                .Select(p => p.Product)
                .Where(p => p is { ArchivedAt: null } && !string.IsNullOrWhiteSpace(p.Handle))
                .Select(MapPlan)
                .OrderBy(p => p.PriceInCents)
                .ToList();

            _logger.LogInformation("Listed {Count} Maxio subscription plan(s) for family {Family}.",
                plans.Count, _settings.ProductFamilyHandle);
            return plans;
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw new MaxioBillingException(
                    $"The configured product family '{_settings.ProductFamilyHandle}' was not found in the billing system.",
                    MaxioBillingErrorKind.NotFound, (int)HttpStatusCode.NotFound, innerException: ex);
            }

            throw TranslateTypedFallback(ex.Error, ex);
        }
        catch (Exception ex)
        {
            throw Translate(ex, cancellationToken, "listing subscription plans");
        }
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        SubscriberIdentity subscriber, string? planHandle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        var reference = NormalizeReference(subscriber.Reference);

        using var budget = CreateBudget(cancellationToken);
        var token = budget.Token;

        var gate = _customerGates.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var customerId = await EnsureCustomerAsync(subscriber, reference, token).ConfigureAwait(false);

            var handle = string.IsNullOrWhiteSpace(planHandle)
                ? await ResolveDefaultPlanHandleAsync(token).ConfigureAwait(false)
                : planHandle.Trim();

            // Idempotent repeat: return an existing non-terminal subscription to the same plan rather than
            // creating a duplicate.
            var existing = await FindActiveSubscriptionAsync(customerId, handle, reference, token).ConfigureAwait(false);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Reusing existing Maxio subscription {SubscriptionId} for customer {CustomerId} on plan {Plan}.",
                    existing.Id, customerId, handle);
                return existing;
            }

            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = handle,
                    CustomerId = customerId,
                },
            };

            var response = await _client.Subscriptions.CreateSubscription(body, ct: token).ConfigureAwait(false);
            var created = MapSubscription(response.Subscription, customerId, reference)
                ?? throw new MaxioBillingException(
                    "The billing system accepted the subscription but returned no subscription details.",
                    MaxioBillingErrorKind.Upstream);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} (state {State}) for customer {CustomerId} on plan {Plan}.",
                created.Id, created.State, customerId, handle);
            return created;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                var messages = errors.Errors ?? (IReadOnlyList<string>)Array.Empty<string>();
                _logger.LogWarning("Maxio rejected subscription creation: {Messages}", string.Join("; ", messages));
                throw new MaxioBillingException(
                    "The billing system rejected the subscription request.",
                    MaxioBillingErrorKind.Validation, (int)HttpStatusCode.UnprocessableEntity, messages, ex);
            }

            throw TranslateTypedFallback(ex.Error, ex);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            throw TranslateCreateCustomer(ex);
        }
        catch (Exception ex)
        {
            throw Translate(ex, cancellationToken, "creating the subscription");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        var reference = NormalizeReference(subscriber.Reference);

        using var budget = CreateBudget(cancellationToken);
        var token = budget.Token;

        try
        {
            var customerId = await FindCustomerIdAsync(reference, token).ConfigureAwait(false);
            if (customerId is null)
            {
                return Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await _client.Customers
                .ListCustomerSubscriptions(customerId.Value, ct: token)
                .ConfigureAwait(false);

            return subscriptions
                .Select(s => MapSubscription(s.Subscription, customerId.Value, reference))
                .Where(s => s is not null)
                .Select(s => s!)
                .OrderByDescending(s => s.NextBillingAt ?? DateTimeOffset.MinValue)
                .ToList();
        }
        catch (Exception ex)
        {
            throw Translate(ex, cancellationToken, "listing subscriptions");
        }
    }

    // ----- customer helpers -------------------------------------------------------------------------------

    private async Task<int> EnsureCustomerAsync(SubscriberIdentity subscriber, string reference, CancellationToken token)
    {
        var existingId = await FindCustomerIdAsync(reference, token).ConfigureAwait(false);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        var (firstName, lastName) = ResolveName(subscriber);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = subscriber.Email,
                Reference = reference,
            },
        };

        var response = await _client.Customers.CreateCustomer(body, ct: token).ConfigureAwait(false);
        var id = response.Customer?.Id
            ?? throw new MaxioBillingException(
                "The billing system created a customer without an id.", MaxioBillingErrorKind.Upstream);

        _logger.LogInformation("Ensured Maxio customer {CustomerId} for reference {Reference}.", id, reference);
        return id;
    }

    /// <summary>
    /// Looks up the billing customer id for a reference, treating a 404 as "no customer yet". A malformed or
    /// unreadable body is NOT treated as absence — it propagates so we never create a duplicate on a read
    /// that merely failed to parse.
    /// </summary>
    private async Task<int?> FindCustomerIdAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: token).ConfigureAwait(false);
            return response.Customer?.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<CustomerSubscription?> FindActiveSubscriptionAsync(
        int customerId, string handle, string reference, CancellationToken token)
    {
        var subscriptions = await _client.Customers
            .ListCustomerSubscriptions(customerId, ct: token)
            .ConfigureAwait(false);

        foreach (var wrapper in subscriptions)
        {
            var subscription = wrapper.Subscription;
            if (subscription is null)
            {
                continue;
            }

            var matchesPlan = string.Equals(subscription.Product?.Handle, handle, StringComparison.OrdinalIgnoreCase);
            var state = subscription.State?.Value;
            var isTerminal = state is not null && TerminalStates.Contains(state);
            if (matchesPlan && !isTerminal)
            {
                return MapSubscription(subscription, customerId, reference);
            }
        }

        return null;
    }

    private async Task<string> ResolveDefaultPlanHandleAsync(CancellationToken token)
    {
        var plans = await _client.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: RequireFamilySelector(),
            dateField: null,
            filter: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            includeArchived: false,
            include: null,
            page: 1,
            perPage: 200,
            ct: token).ConfigureAwait(false);

        var handle = plans
            .Select(p => p.Product)
            .Where(p => p is { ArchivedAt: null } && !string.IsNullOrWhiteSpace(p.Handle))
            .OrderBy(p => p.PriceInCents ?? long.MaxValue)
            .Select(p => p.Handle)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new MaxioBillingException(
                $"No subscribable plans are available in product family '{_settings.ProductFamilyHandle}'.",
                MaxioBillingErrorKind.NotFound);
        }

        return handle!;
    }

    // ----- mapping ----------------------------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle!,
        Name = string.IsNullOrWhiteSpace(product.Name) ? product.Handle! : product.Name!,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        IntervalUnit = product.IntervalUnit?.Value,
        IntervalCount = product.Interval,
    };

    private static CustomerSubscription? MapSubscription(Subscription? subscription, int customerId, string reference)
    {
        if (subscription is null)
        {
            return null;
        }

        return new CustomerSubscription
        {
            Id = subscription.Id ?? 0,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            State = subscription.State?.Value,
            // "when the next regularly scheduled attempted charge will occur"
            NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            CustomerId = subscription.Customer?.Id ?? customerId,
            CustomerReference = subscription.Customer?.Reference ?? reference,
        };
    }

    // ----- error translation ------------------------------------------------------------------------------

    private MaxioBillingException Translate(Exception ex, CancellationToken callerToken, string action)
    {
        switch (ex)
        {
            case MaxioBillingException billing:
                return billing;

            // Case B operations (read customer, list subscriptions, list plans fallback) carry the status.
            case SdkException<RawError> raw:
                var status = (int)raw.Error.StatusCode;
                var kind = ClassifyStatus(status);
                _logger.LogWarning("Maxio returned HTTP {Status} while {Action}.", status, action);
                return new MaxioBillingException(
                    kind == MaxioBillingErrorKind.Upstream
                        ? "The billing system is currently unavailable."
                        : $"The billing system rejected the request ({status}).",
                    kind, status, innerException: raw);

            // Auth application failure (a scheme could not be applied) — our credentials, not the caller's.
            case AuthSchemeException:
                _logger.LogError(ex, "Maxio authentication could not be applied while {Action}.", action);
                return new MaxioBillingException(
                    "The billing system is currently unavailable.", MaxioBillingErrorKind.Upstream, innerException: ex);

            // A malformed body — from a drifted 2xx (deserialization) or from error-object construction on a
            // non-2xx. Either way it is caller-safe to report as an upstream processing failure, and it must
            // never be read as a domain "absence".
            case JsonException:
                _logger.LogError(ex, "Maxio returned an unprocessable response while {Action}.", action);
                return new MaxioBillingException(
                    "The billing system returned a response that could not be processed.",
                    MaxioBillingErrorKind.Upstream, innerException: ex);

            case HttpRequestException:
            case OperationCanceledException when !callerToken.IsCancellationRequested:
                _logger.LogError(ex, "Maxio was unreachable or timed out while {Action}.", action);
                return new MaxioBillingException(
                    "The billing system did not respond in time.", MaxioBillingErrorKind.Upstream, innerException: ex);

            case OperationCanceledException:
                // Caller aborted — surface as cancellation, not a billing failure.
                throw ex;

            default:
                _logger.LogError(ex, "Unexpected failure while {Action}.", action);
                return new MaxioBillingException(
                    "An unexpected billing error occurred.", MaxioBillingErrorKind.Unknown, innerException: ex);
        }
    }

    private MaxioBillingException TranslateCreateCustomer(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var payload))
        {
            var messages = ExtractCustomerMessages(payload);
            _logger.LogWarning("Maxio rejected customer creation: {Messages}", string.Join("; ", messages));
            return new MaxioBillingException(
                "The billing system rejected the customer details.",
                MaxioBillingErrorKind.Validation, (int)HttpStatusCode.UnprocessableEntity, messages, ex);
        }

        return TranslateTypedFallback(ex.Error, ex);
    }

    // Fallback for a typed (Case A) error whose specific accessor did not match: read the raw body/status.
    private MaxioBillingException TranslateTypedFallback(MaxioAdvancedBilling.Core.ErrorResponse.ApiError error, Exception ex)
    {
        if (error.TryGetRawError(out var raw))
        {
            var status = (int)raw.StatusCode;
            var kind = ClassifyStatus(status);
            _logger.LogWarning("Maxio returned HTTP {Status} (typed fallback).", status);
            return new MaxioBillingException(
                kind == MaxioBillingErrorKind.Upstream
                    ? "The billing system is currently unavailable."
                    : $"The billing system rejected the request ({status}).",
                kind, status, innerException: ex);
        }

        return new MaxioBillingException(
            "The billing system returned an unrecognised error.", MaxioBillingErrorKind.Unknown, innerException: ex);
    }

    private static IReadOnlyList<string> ExtractCustomerMessages(CustomerErrorResponse1 payload)
    {
        if (payload.Errors is { } errors && errors.TryGetListOfString(out var list) && list is not null)
        {
            return list;
        }

        return Array.Empty<string>();
    }

    private static MaxioBillingErrorKind ClassifyStatus(int status) => status switch
    {
        // Our credentials / our quota — the caller did nothing wrong and cannot fix it.
        401 or 403 or 429 => MaxioBillingErrorKind.Upstream,
        404 => MaxioBillingErrorKind.NotFound,
        422 or 400 or 409 => MaxioBillingErrorKind.Validation,
        >= 400 and < 500 => MaxioBillingErrorKind.Validation,
        _ => MaxioBillingErrorKind.Upstream,
    };

    // ----- misc -------------------------------------------------------------------------------------------

    private CancellationTokenSource CreateBudget(CancellationToken callerToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    // The list-products-for-family endpoint's product_family_id path segment accepts either the numeric id or
    // the handle prefixed with "handle:". The configured value is a handle, so prefix it — unless it is
    // already numeric or already prefixed (so a deployment may configure any of the three forms).
    private string RequireFamilySelector()
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new MaxioBillingException(
                "Maxio:ProductFamilyHandle is not configured.", MaxioBillingErrorKind.Upstream);
        }

        var value = _settings.ProductFamilyHandle!.Trim();
        if (value.StartsWith("handle:", StringComparison.OrdinalIgnoreCase) || value.All(char.IsDigit))
        {
            return value;
        }

        return "handle:" + value;
    }

    private static string NormalizeReference(string reference) => reference.Trim().ToLowerInvariant();

    private static (string FirstName, string LastName) ResolveName(SubscriberIdentity subscriber)
    {
        var first = string.IsNullOrWhiteSpace(subscriber.FirstName) ? null : subscriber.FirstName.Trim();
        var last = string.IsNullOrWhiteSpace(subscriber.LastName) ? null : subscriber.LastName.Trim();

        if (first is null && last is null)
        {
            // Derive a best-effort name from the email local part so CreateCustomer's required fields are met.
            var local = subscriber.Email.Split('@')[0];
            first = string.IsNullOrWhiteSpace(local) ? "eShop" : local;
            last = "Customer";
        }

        return (first ?? "eShop", last ?? "Customer");
    }
}
