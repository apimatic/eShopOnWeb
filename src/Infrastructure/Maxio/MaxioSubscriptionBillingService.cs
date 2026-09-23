using System;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>. Maxio is the system
/// of record; the local <see cref="MaxioBillingContext"/> carries the buyer→customer map and the
/// subscription enrollment records used for idempotency, ordering and reconciliation.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int PlansPageSize = 200;
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SubscribeBudget = TimeSpan.FromSeconds(60);
    private static readonly CultureInfo PriceCulture = CultureInfo.GetCultureInfo("en-US");

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioBillingContext _db;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        MaxioBillingContext db,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _db = db;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SubscriptionPlansResult> GetPlansAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ReadBudget);
        var ct = cts.Token;

        try
        {
            var (plans, truncated) = await GetPlansCoreAsync(ct);
            return new SubscriptionPlansResult { Plans = plans, Truncated = truncated };
        }
        catch (Exception ex) when (TranslateBoundary(ex, out var billing))
        {
            throw billing;
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string? planHandle, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(SubscribeBudget);
        var ct = cts.Token;

        try
        {
            var requestedHandle = string.IsNullOrWhiteSpace(planHandle) ? _settings.DefaultPlanHandle : planHandle;
            if (string.IsNullOrWhiteSpace(requestedHandle))
            {
                throw new UnknownSubscriptionPlanException(planHandle ?? "(none provided)");
            }

            // CROSS-OPERATION INVARIANT: the plan must be one the configured family actually offers.
            var (plans, _) = await GetPlansCoreAsync(ct);
            var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, requestedHandle, StringComparison.OrdinalIgnoreCase));
            if (plan is null)
            {
                throw new UnknownSubscriptionPlanException(requestedHandle!);
            }
            var resolvedHandle = plan.Handle;

            var customerId = await EnsureCustomerAsync(subscriber, ct);
            var reference = BuildSubscriptionReference(subscriber.BuyerId, resolvedHandle);

            var enrollment = await _db.Enrollments.FirstOrDefaultAsync(x => x.Reference == reference, ct);

            // Idempotent replay: we already recorded a confirmed subscription for this buyer+plan.
            if (enrollment is { IsConfirmed: true })
            {
                var existing = await TryFindSubscriptionAsync(reference, ct);
                if (existing is not null)
                {
                    return BuildResult(ClassifyExisting(existing.State), resolvedHandle, plan, existing, customerId, reference);
                }
                // Provider no longer has it (purged); fall through and recreate.
            }

            // Provider-side reconciliation: adopt a subscription created by a prior run whose local record was lost.
            var found = await TryFindSubscriptionAsync(reference, ct);
            if (found?.Id is not null)
            {
                await ConfirmEnrollmentAsync(enrollment, subscriber.BuyerId, resolvedHandle, reference, customerId, found.Id.Value, found.State?.Value, ct);
                return BuildResult(ClassifyExisting(found.State), resolvedHandle, plan, found, customerId, reference);
            }

            // WRITE ORDER: the local enrollment row (carrying the reference) exists before the provider call.
            if (enrollment is null)
            {
                enrollment = new SubscriptionEnrollment(subscriber.BuyerId, resolvedHandle, reference, customerId);
                _db.Enrollments.Add(enrollment);
                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // Concurrent double-click: the unique Reference index rejected this second row.
                    _logger.LogWarning("Concurrent subscribe for reference {Reference}; reconciling.", reference);
                    _db.Entry(enrollment).State = EntityState.Detached;
                    enrollment = await _db.Enrollments.FirstAsync(x => x.Reference == reference, ct);
                    var concurrent = await TryFindSubscriptionAsync(reference, ct);
                    if (concurrent?.Id is not null)
                    {
                        return BuildResult(ClassifyExisting(concurrent.State), resolvedHandle, plan, concurrent, customerId, reference);
                    }
                    // The winner has not created it yet; proceed — the Maxio call is the next guard.
                }
            }

            var created = await CreateSubscriptionReconciledAsync(resolvedHandle, customerId, reference, ct);

            var subscriptionId = created.Id
                ?? throw new SubscriptionBillingException("Maxio returned a subscription without an id.");
            var wasNew = !enrollment!.IsConfirmed;
            enrollment.Confirm(subscriptionId, created.State?.Value);
            await SaveEnrollmentAsync(enrollment, ct);

            var outcome = ClassifyFresh(created.State);
            // Gate the "created" log on an actually-new, live subscription (not a replay/adoption).
            if (wasNew && outcome == SubscribeOutcome.Created)
            {
                _logger.LogInformation(
                    "Subscribed buyer {BuyerId} to plan {Plan}: subscription {SubscriptionId}, state {State}.",
                    subscriber.BuyerId, resolvedHandle, subscriptionId, created.State?.Value);
            }

            return BuildResult(outcome, resolvedHandle, plan, created, customerId, reference);
        }
        catch (Exception ex) when (TranslateBoundary(ex, out var billing))
        {
            throw billing;
        }
    }

    public async Task<IReadOnlyList<CustomerSubscriptionInfo>> GetMySubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ReadBudget);
        var ct = cts.Token;

        try
        {
            var customerId = await TryResolveCustomerIdAsync(subscriber, ct);
            if (customerId is null)
            {
                return Array.Empty<CustomerSubscriptionInfo>();
            }

            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId.Value, ct: ct);
            return subscriptions
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => MapCustomerSubscription(s!))
                .ToList();
        }
        catch (Exception ex) when (TranslateBoundary(ex, out var billing))
        {
            throw billing;
        }
    }

    // ---- Plans -------------------------------------------------------------

    private async Task<(List<SubscriptionPlanInfo> Plans, bool Truncated)> GetPlansCoreAsync(CancellationToken ct)
    {
        var familyId = await ResolveFamilyIdAsync(ct);

        IReadOnlyList<ProductResponse> products;
        try
        {
            products = await _client.ProductFamilies.ListProductsForProductFamily(
                familyId.ToString(CultureInfo.InvariantCulture),
                dateField: null, filter: null, startDate: null, endDate: null,
                startDatetime: null, endDatetime: null,
                includeArchived: false, include: null, page: 1, perPage: PlansPageSize, ct: ct);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
            {
                throw new SubscriptionBillingException(
                    $"Configured product family '{_settings.ProductFamilyHandle}' has no products or was not found: {notFound}");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw LogAndTranslate(raw, ex);
            }
            throw new SubscriptionBillingException("Maxio failed to list plans.", null, ex);
        }

        var plans = products
            .Select(p => p.Product)
            .Where(p => p.ArchivedAt is null)
            .Select(MapPlan)
            .ToList();

        return (plans, products.Count >= PlansPageSize);
    }

    private async Task<int> ResolveFamilyIdAsync(CancellationToken ct)
    {
        var families = await _client.ProductFamilies.ListProductFamilies(
            dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct);

        var match = families.FirstOrDefault(f =>
            string.Equals(f.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (match?.ProductFamily?.Id is int id)
        {
            return id;
        }

        throw new SubscriptionBillingException(
            $"Configured product family '{_settings.ProductFamilyHandle}' was not found on the Maxio site.");
    }

    // ---- Customer ----------------------------------------------------------

    private async Task<int> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken ct)
    {
        var link = await _db.CustomerLinks.AsNoTracking()
            .FirstOrDefaultAsync(x => x.BuyerId == subscriber.BuyerId, ct);
        if (link is not null)
        {
            return link.MaxioCustomerId;
        }

        var existingId = await TryReadCustomerIdByReferenceAsync(subscriber.BuyerId, ct);
        if (existingId is int found)
        {
            return await SaveCustomerLinkAsync(subscriber.BuyerId, found, ct);
        }

        var (firstName, lastName) = DeriveName(subscriber.Email);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = subscriber.Email,
                Reference = subscriber.BuyerId // provider-enforced unique claim
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body, ct: ct);
            var id = response.Customer.Id
                ?? throw new SubscriptionBillingException("Maxio returned a customer without an id.");
            _logger.LogInformation("Created Maxio customer {CustomerId} for buyer {BuyerId}.", id, subscriber.BuyerId);
            return await SaveCustomerLinkAsync(subscriber.BuyerId, id, ct);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // 422 is most commonly the reference-uniqueness constraint firing under a concurrent create —
            // reconcile by re-reading the customer for this reference.
            var reconciled = await TryReadCustomerIdByReferenceAsync(subscriber.BuyerId, ct);
            if (reconciled is int reconciledId)
            {
                _logger.LogWarning("Customer create for buyer {BuyerId} reconciled to existing customer {CustomerId}.",
                    subscriber.BuyerId, reconciledId);
                return await SaveCustomerLinkAsync(subscriber.BuyerId, reconciledId, ct);
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new SubscriptionBillingException("Maxio rejected the customer details (HTTP 422).", 422, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw LogAndTranslate(raw, ex);
            }
            throw new SubscriptionBillingException("Maxio rejected the customer creation.", 422, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // Unknown outcome / unreadable response: the create may have landed. Re-read by reference.
            var reconciled = await TryReadCustomerIdByReferenceAsync(subscriber.BuyerId, ct);
            if (reconciled is int reconciledId)
            {
                return await SaveCustomerLinkAsync(subscriber.BuyerId, reconciledId, ct);
            }
            throw new SubscriptionBillingException("Could not confirm the Maxio customer; please retry.", null, ex);
        }
    }

    private async Task<int?> TryResolveCustomerIdAsync(SubscriberIdentity subscriber, CancellationToken ct)
    {
        var link = await _db.CustomerLinks.AsNoTracking()
            .FirstOrDefaultAsync(x => x.BuyerId == subscriber.BuyerId, ct);
        if (link is not null)
        {
            return link.MaxioCustomerId;
        }
        return await TryReadCustomerIdByReferenceAsync(subscriber.BuyerId, ct);
    }

    private async Task<int?> TryReadCustomerIdByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return response.Customer.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw LogAndTranslate(ex.Error, ex);
        }
    }

    private async Task<int> SaveCustomerLinkAsync(string buyerId, int maxioCustomerId, CancellationToken ct)
    {
        var entity = new MaxioCustomerLink(buyerId, maxioCustomerId);
        _db.CustomerLinks.Add(entity);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Concurrent link insert; the buyer already has a row. Provider identity is unchanged.
            _db.Entry(entity).State = EntityState.Detached;
        }
        return maxioCustomerId;
    }

    // ---- Subscription ------------------------------------------------------

    private async Task<Subscription> CreateSubscriptionReconciledAsync(string planHandle, int customerId, string reference, CancellationToken ct)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                Reference = reference,
                // The seeded plans require no payment method; invoice/remittance collection lets a priced
                // plan be subscribed without a card on file (automatic collection would fail for lack of one).
                PaymentCollectionMethod = ResolveCollectionMethod()
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct: ct);
            return response.Subscription
                ?? throw new SubscriptionBillingException("Maxio returned an empty subscription response.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // A duplicate reference (if the site enforces it) or a lost race — see whether it landed.
            var recovered = await TryFindSubscriptionAsync(reference, ct);
            if (recovered is not null)
            {
                return recovered;
            }
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new SubscriptionBillingException(DescribeErrors(errors), 422, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw LogAndTranslate(raw, ex);
            }
            throw new SubscriptionBillingException("Maxio rejected the subscription.", 422, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // UNKNOWN OUTCOME: the POST may have reached Maxio. Re-read by reference before deciding.
            var recovered = await TryFindSubscriptionAsync(reference, ct);
            if (recovered is not null)
            {
                return recovered;
            }
            throw new SubscriptionBillingException("The subscription could not be confirmed; please retry.", null, ex);
        }
    }

    private async Task<Subscription?> TryFindSubscriptionAsync(string reference, CancellationToken ct)
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
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetRawError(out var raw))
            {
                if (raw.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }
                throw LogAndTranslate(raw, ex);
            }
            throw new SubscriptionBillingException("Maxio find-subscription failed.", null, ex);
        }
    }

    private async Task ConfirmEnrollmentAsync(
        SubscriptionEnrollment? existing, string buyerId, string planHandle, string reference,
        int customerId, int subscriptionId, string? state, CancellationToken ct)
    {
        if (existing is null)
        {
            existing = new SubscriptionEnrollment(buyerId, planHandle, reference, customerId);
            _db.Enrollments.Add(existing);
        }
        existing.Confirm(subscriptionId, state);
        await SaveEnrollmentAsync(existing, ct);
    }

    private async Task SaveEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent writer persisted the same reference first; the provider state is authoritative.
            _db.Entry(enrollment).State = EntityState.Detached;
        }
    }

    // ---- Mapping / helpers -------------------------------------------------

    private SubscribeResult BuildResult(SubscribeOutcome outcome, string handle, SubscriptionPlanInfo plan, Subscription sub, int customerId, string reference)
    {
        var priceInCents = sub.ProductPriceInCents ?? plan.PriceInCents;
        return new SubscribeResult
        {
            Outcome = outcome,
            PlanHandle = handle,
            PlanName = sub.Product?.Name ?? plan.Name,
            PriceInCents = priceInCents,
            FormattedPrice = FormatPrice(priceInCents),
            State = sub.State?.Value,
            NextBillingDate = sub.CurrentPeriodEndsAt ?? sub.NextAssessmentAt,
            SubscriptionId = sub.Id ?? 0,
            CustomerId = customerId,
            Reference = reference
        };
    }

    private static CustomerSubscriptionInfo MapCustomerSubscription(Subscription sub)
    {
        var priceInCents = sub.ProductPriceInCents ?? 0;
        return new CustomerSubscriptionInfo
        {
            SubscriptionId = sub.Id ?? 0,
            PlanHandle = sub.Product?.Handle,
            PlanName = sub.Product?.Name,
            PriceInCents = priceInCents,
            FormattedPrice = FormatPrice(priceInCents),
            State = sub.State?.Value,
            NextBillingDate = sub.CurrentPeriodEndsAt ?? sub.NextAssessmentAt,
            Reference = sub.Reference
        };
    }

    private static SubscriptionPlanInfo MapPlan(Product p)
    {
        var priceInCents = p.PriceInCents ?? 0;
        return new SubscriptionPlanInfo
        {
            Handle = p.Handle ?? string.Empty,
            Name = p.Name,
            Description = p.Description,
            PriceInCents = priceInCents,
            FormattedPrice = FormatPrice(priceInCents),
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit?.Value,
            ProductId = p.Id
        };
    }

    private static string FormatPrice(long cents) => (cents / 100m).ToString("C", PriceCulture);

    private string BuildSubscriptionReference(string buyerId, string planHandle) => $"eshop-sub:{buyerId}:{planHandle}";

    private CollectionMethod ResolveCollectionMethod() =>
        string.IsNullOrWhiteSpace(_settings.PaymentCollectionMethod)
            ? CollectionMethod.Remittance
            : CollectionMethod.FromValue(_settings.PaymentCollectionMethod.Trim());

    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var local = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var first = string.IsNullOrWhiteSpace(local) ? "eShop" : local;
        return (first, "eShopOnWeb Subscriber");
    }

    /// <summary>done = active/trialing; not-yet = pending/assessing/awaiting_signup (and null/unreadable); else failed.</summary>
    private static SubscribeOutcome ClassifyFresh(SubscriptionState? state)
    {
        if (state is null)
        {
            return SubscribeOutcome.Pending;
        }
        if (state == SubscriptionState.Active || state == SubscriptionState.Trialing)
        {
            return SubscribeOutcome.Created;
        }
        if (state == SubscriptionState.Pending || state == SubscriptionState.Assessing || state == SubscriptionState.AwaitingSignup)
        {
            return SubscribeOutcome.Pending;
        }
        return SubscribeOutcome.Failed;
    }

    private static SubscribeOutcome ClassifyExisting(SubscriptionState? state)
    {
        var fresh = ClassifyFresh(state);
        return fresh == SubscribeOutcome.Created ? SubscribeOutcome.AlreadySubscribed : fresh;
    }

    private static string DescribeErrors(ErrorListResponse1 errors) =>
        errors.Errors.Count == 0 ? "Maxio rejected the request." : string.Join("; ", errors.Errors);

    private SubscriptionBillingException LogAndTranslate(RawError raw, Exception ex)
    {
        var status = (int)raw.StatusCode;
        string body;
        try
        {
            body = raw.ReadAsString();
        }
        catch
        {
            body = "<unreadable>";
        }
        _logger.LogError(ex, "Maxio request failed: HTTP {Status}. Body: {Body}", status, body);
        return new SubscriptionBillingException($"Maxio request failed (HTTP {status}).", status, ex);
    }

    /// <summary>
    /// Boundary net for anything a helper did not already translate: Case B raw errors, transport failures,
    /// and unreadable 2xx bodies. Domain exceptions pass straight through.
    /// </summary>
    private bool TranslateBoundary(Exception ex, out SubscriptionBillingException billing)
    {
        switch (ex)
        {
            case UnknownSubscriptionPlanException:
            case SubscriptionBillingException:
                billing = null!;
                return false; // rethrow original
            case SdkException<RawError> raw:
                billing = LogAndTranslate(raw.Error, raw);
                return true;
            case JsonException:
                _logger.LogError(ex, "Maxio returned a response that could not be processed.");
                billing = new SubscriptionBillingException("The billing provider returned a response that could not be processed.", null, ex);
                return true;
            case HttpRequestException:
                _logger.LogError(ex, "Maxio provider is unreachable.");
                billing = new SubscriptionBillingException("The billing provider is currently unreachable.", null, ex);
                return true;
            default:
                billing = null!;
                return false; // OperationCanceledException and anything else propagate untouched
        }
    }
}
