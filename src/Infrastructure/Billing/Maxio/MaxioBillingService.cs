using System;
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
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by the Maxio Advanced Billing SDK. Owns all
/// provider communication, maps SDK models to SDK-free DTOs, and translates every SDK/transport
/// failure into <see cref="SubscriptionBillingException"/>.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    // Whole-operation budget. Applied once per public method (linked to the caller's token) so the
    // per-attempt SDK timeouts of the several calls a subscribe makes cannot sum past this.
    private static readonly TimeSpan OperationBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        using var scope = Deadline(cancellationToken, out var ct);
        try
        {
            var products = await ListFamilyProductsAsync(ct);
            return products
                .Select(p => p.Product)
                .Where(p => p is not null)
                .Select(p => MapPlan(p!))
                .ToList();
        }
        catch (Exception ex) when (Translate(ex, "listing subscription plans", cancellationToken, out var billing))
        {
            throw billing;
        }
    }

    public async Task<CustomerSubscriptionDto> SubscribeAsync(
        BillingAppUser user, SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);

        using var scope = Deadline(cancellationToken, out var ct);
        try
        {
            // Resolve the target plan against the live catalog (also validates the handle and lets us
            // default catalog-agnostically to a configured or first-available plan).
            var plans = (await ListFamilyProductsAsync(ct))
                .Select(p => p.Product)
                .Where(p => p is not null && !string.IsNullOrEmpty(p!.Handle))
                .Select(p => p!)
                .ToList();

            var plan = ResolveTargetPlan(plans, request.PlanHandle);
            var planHandle = plan.Handle!;

            // 1) Ensure a single Maxio customer exists for this eShop user (idempotent by reference).
            var customerId = await EnsureCustomerAsync(user, ct);

            // 2) Ensure a single subscription exists for this (user, plan) pair (idempotent by reference).
            var subscriptionReference = SubscriptionReference(user.UserId, planHandle);

            var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, ct);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Reusing existing Maxio subscription {SubscriptionId} for user {UserId} on plan {PlanHandle}",
                    existing.Id, user.UserId, planHandle);
                return MapSubscription(existing);
            }

            var created = await CreateSubscriptionAsync(customerId, planHandle, subscriptionReference, ct);
            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for user {UserId} on plan {PlanHandle} (state {State})",
                created.Id, user.UserId, planHandle, created.State?.Value);
            return MapSubscription(created);
        }
        catch (Exception ex) when (Translate(ex, "creating the subscription", cancellationToken, out var billing))
        {
            throw billing;
        }
    }

    public async Task<IReadOnlyList<CustomerSubscriptionDto>> GetMySubscriptionsAsync(
        BillingAppUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        using var scope = Deadline(cancellationToken, out var ct);
        try
        {
            var customer = await ReadCustomerByReferenceAsync(user.UserId, ct);
            if (customer?.Id is not int customerId)
            {
                // No Maxio customer yet — the user has never subscribed.
                return Array.Empty<CustomerSubscriptionDto>();
            }

            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
            return subscriptions
                .Select(s => s.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!))
                .ToList();
        }
        catch (Exception ex) when (Translate(ex, "listing your subscriptions", cancellationToken, out var billing))
        {
            throw billing;
        }
    }

    // ----- Per-operation SDK calls (grounded on the contract sheet) -----

    private Task<IReadOnlyList<ProductResponse>> ListFamilyProductsAsync(CancellationToken ct) =>
        // productFamilyId accepts an id OR a handle prefixed with "handle:" (per the operation's remarks).
        _client.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: "handle:" + _settings.ProductFamilyHandle,
            dateField: null,
            filter: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            includeArchived: false,
            include: null,
            ct: ct);

    private async Task<int> EnsureCustomerAsync(BillingAppUser user, CancellationToken ct)
    {
        var existing = await ReadCustomerByReferenceAsync(user.UserId, ct);
        if (existing?.Id is int existingId)
        {
            return existingId;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = string.IsNullOrWhiteSpace(user.FirstName) ? "eShop" : user.FirstName,
                LastName = string.IsNullOrWhiteSpace(user.LastName) ? "Customer" : user.LastName,
                Email = user.Email,
                Reference = user.UserId
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body, ct: ct);
            var id = response.Customer.Id
                ?? throw new SubscriptionBillingException("Maxio did not return a customer id.", 502);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}", id, user.UserId);
            return id;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A concurrent subscribe (double-click) may have created the customer between our read and
            // this create. Reconcile by reference before surfacing the validation error.
            var reconciled = await ReadCustomerByReferenceAsync(user.UserId, ct);
            if (reconciled?.Id is int reconciledId)
            {
                return reconciledId;
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out var typed))
            {
                throw new SubscriptionBillingException(
                    DescribeCustomerErrors(typed), 422, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new SubscriptionBillingException(
                    $"Maxio rejected the customer creation (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode, ex);
            }
            throw new SubscriptionBillingException("Maxio rejected the customer creation.", 502, ex);
        }
    }

    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference, ct: ct);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null;
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(
        int customerId, string productHandle, string reference, CancellationToken ct)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference,
                // These plans require no payment method. Collect by remittance (invoice) so Maxio does
                // not attempt an automatic card charge at signup — which otherwise fails with
                // "No payment method was on file" even though the product allows card-less signup.
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct: ct);
            return response.Subscription
                ?? throw new SubscriptionBillingException("Maxio did not return the created subscription.", 502);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // Reconcile a possible concurrent create before surfacing the error.
            var reconciled = await FindSubscriptionByReferenceAsync(reference, ct);
            if (reconciled is not null)
            {
                return reconciled;
            }

            if (ex.Error.TryGetErrorListResponse1(out var typed))
            {
                throw new SubscriptionBillingException(DescribeErrorList(typed.Errors), 422, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new SubscriptionBillingException(
                    $"Maxio rejected the subscription creation (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode, ex);
            }
            throw new SubscriptionBillingException("Maxio rejected the subscription creation.", 502, ex);
        }
    }

    // ----- Plan resolution -----

    private Product ResolveTargetPlan(IReadOnlyList<Product> plans, string? requestedHandle)
    {
        if (plans.Count == 0)
        {
            throw new SubscriptionBillingException(
                $"No subscription plans are available in product family '{_settings.ProductFamilyHandle}'.", 404);
        }

        var handle = requestedHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            handle = _settings.DefaultProductHandle;
        }

        if (string.IsNullOrWhiteSpace(handle))
        {
            // Catalog-agnostic default: the first plan in the family.
            return plans[0];
        }

        var match = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            throw new SubscriptionBillingException(
                $"Plan '{handle}' was not found in product family '{_settings.ProductFamilyHandle}'.", 404);
        }

        return match;
    }

    // ----- Mapping -----

    private static SubscriptionPlanDto MapPlan(Product product) => new()
    {
        Id = product.Id ?? 0,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        Price = ToDecimal(product.PriceInCents),
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.Value,
        RequiresPaymentMethod = product.RequireCreditCard ?? false
    };

    private static CustomerSubscriptionDto MapSubscription(Subscription subscription) => new()
    {
        SubscriptionId = subscription.Id ?? 0,
        Reference = subscription.Reference,
        State = subscription.State?.Value ?? "unknown",
        CustomerId = subscription.Customer?.Id ?? 0,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
        Price = ToDecimal(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit?.Value,
        NextBillingDate = subscription.CurrentPeriodEndsAt
    };

    private static decimal ToDecimal(long? cents) => (cents ?? 0) / 100m;

    private static string SubscriptionReference(string userId, string planHandle) => $"{userId}:{planHandle}";

    private static string DescribeCustomerErrors(CustomerErrorResponse1 typed)
    {
        if (typed.Errors is { } errors)
        {
            if (errors.TryGetListOfString(out var list) && list is { Count: > 0 })
            {
                return $"Maxio rejected the customer details: {string.Join("; ", list)}";
            }
            if (errors.TryGetCustomerError(out var customerError))
            {
                return $"Maxio rejected the customer details: {JsonSerializer.Serialize(customerError)}";
            }
        }
        return "Maxio rejected the customer details.";
    }

    private static string DescribeErrorList(IReadOnlyList<string> errors) =>
        errors is { Count: > 0 }
            ? $"Maxio rejected the request: {string.Join("; ", errors)}"
            : "Maxio rejected the request.";

    // ----- Boundaries: total-call deadline + failure translation -----

    private static DeadlineScope Deadline(CancellationToken caller, out CancellationToken deadline)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(caller);
        cts.CancelAfter(OperationBudget);
        deadline = cts.Token;
        return new DeadlineScope(cts);
    }

    /// <summary>
    /// Exception filter that converts any non-billing failure into a caller-safe
    /// <see cref="SubscriptionBillingException"/>. Returns <c>true</c> (with the translated exception
    /// in <paramref name="billing"/>) for failures we own; returns <c>false</c> to let a genuine
    /// caller-cancellation and already-translated billing exceptions propagate unchanged.
    /// </summary>
    private bool Translate(Exception ex, string action, CancellationToken callerToken, out SubscriptionBillingException billing)
    {
        switch (ex)
        {
            case SubscriptionBillingException:
                billing = null!;
                return false; // already translated at the call site — rethrow as-is

            case OperationCanceledException when callerToken.IsCancellationRequested:
                billing = null!;
                return false; // the caller (e.g. a disconnected client) cancelled — not our failure

            case OperationCanceledException:
                // Our own deadline elapsed.
                _logger.LogWarning(ex, "Maxio call timed out while {Action}", action);
                billing = new SubscriptionBillingException(
                    "The billing provider did not respond in time. Please try again.", 504, ex);
                return true;

            case JsonException:
                // A 2xx body that could not be deserialized, or a non-2xx body that did not match its
                // generated error shape (which destroys the status). Either way: a provider failure,
                // never treated as a domain absence.
                _logger.LogError(ex, "Maxio returned a response that could not be processed while {Action}", action);
                billing = new SubscriptionBillingException(
                    "The billing provider returned a response that could not be processed.", 502, ex);
                return true;

            case HttpRequestException:
                _logger.LogError(ex, "Could not reach the billing provider while {Action}", action);
                billing = new SubscriptionBillingException("The billing provider is unreachable.", 502, ex);
                return true;

            case SdkException<RawError> raw:
                _logger.LogError(ex, "Maxio error (HTTP {Status}) while {Action}: {Body}",
                    (int)raw.Error.StatusCode, action, SafeBody(raw.Error));
                billing = new SubscriptionBillingException(
                    $"The billing provider returned an error (HTTP {(int)raw.Error.StatusCode}).",
                    (int)raw.Error.StatusCode, ex);
                return true;

            // Case A read operations whose non-expected statuses reach the boundary: recover the real
            // HTTP status from the typed error's RawError fallback rather than collapsing to 500.
            case SdkException<ListProductsForProductFamilyError> pe:
                billing = FromTypedError(pe.Error.TryGetRawError(out var peRaw) ? peRaw : null, action, "listing plans", ex);
                return true;

            case SdkException<FindSubscriptionError> fe:
                billing = FromTypedError(fe.Error.TryGetRawError(out var feRaw) ? feRaw : null, action, "looking up the subscription", ex);
                return true;

            default:
                _logger.LogError(ex, "Unexpected billing failure while {Action}", action);
                billing = new SubscriptionBillingException("An unexpected billing error occurred.", 500, ex);
                return true;
        }
    }

    private SubscriptionBillingException FromTypedError(RawError? raw, string action, string what, Exception ex)
    {
        if (raw is not null)
        {
            _logger.LogError(ex, "Maxio error (HTTP {Status}) while {Action}: {Body}",
                (int)raw.StatusCode, action, SafeBody(raw));
            return new SubscriptionBillingException(
                $"The billing provider returned an error (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode, ex);
        }

        _logger.LogError(ex, "Maxio error (no status) while {Action}", action);
        return new SubscriptionBillingException($"The billing provider returned an error while {what}.", 502, ex);
    }

    private static string SafeBody(RawError error)
    {
        try
        {
            return error.ReadAsString();
        }
        catch (Exception)
        {
            return "<unreadable>";
        }
    }

    private sealed class DeadlineScope : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        public DeadlineScope(CancellationTokenSource cts) => _cts = cts;
        public void Dispose() => _cts.Dispose();
    }
}
