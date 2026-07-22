using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using MeteredComponent = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.MeteredComponent;
using Subscription = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The one class in eShopOnWeb that talks to Maxio Advanced Billing (plan.md §2.2).
/// </summary>
/// <remarks>
/// <para>
/// Everything provider-specific is confined here: the SDK types, Maxio's cents-vs-dollars split,
/// its open enums, its per-operation error shapes, and its manual pagination. Callers see only
/// domain types and <see cref="BillingProviderException"/>.
/// </para>
/// <para>
/// The outbound host is decided by <see cref="MaxioSettings.ResolveBaseUrl"/> at registration time,
/// so pointing this build at production, a dev tenant, or a local mock is a configuration change
/// (plan.md §2.3).
/// </para>
/// </remarks>
public class MaxioBillingClient : IBillingClient
{
    /// <summary>Maxio's maximum page size for list operations.</summary>
    private const int PageSize = 200;

    /// <summary>Stops a pathological response from looping forever; far above any real catalog.</summary>
    private const int MaxPages = 50;

    private readonly MaxioAdvancedBillingClient _maxio;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    /// <summary>
    /// The configured family's numeric id, resolved from its handle on first use. Maxio reassigns
    /// these whenever the catalog is re-created, so the handle is the only durable identifier.
    /// </summary>
    private int? _productFamilyId;

    public MaxioBillingClient(MaxioAdvancedBillingClient maxio,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingClient> logger)
    {
        _maxio = maxio;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var plans = new List<BillingPlan>();

        for (var page = 1; page <= MaxPages; page++)
        {
            IReadOnlyList<ProductResponse> responses;
            try
            {
                responses = await _maxio.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: PageSize,
                    ct: cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFound))
                {
                    throw new BillingConfigurationException(
                        $"Maxio could not list the plans in product family '{_settings.ProductFamilyHandle}': {notFound}");
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw Failure(ListPlansAction, raw);
                }

                throw Unexpected(ListPlansAction, ex);
            }
            catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
            {
                throw Boundary(ListPlansAction, ex);
            }

            foreach (var response in responses)
            {
                var plan = MaxioModelMapper.TryMapPlan(response.Product);

                // A plan with no handle could never be subscribed to reliably, and an archived one
                // must not be offered (UC1 preconditions).
                if (plan is not null && !plan.IsArchived)
                {
                    plans.Add(plan);
                }
            }

            if (responses.Count < PageSize)
            {
                break;
            }
        }

        return plans;
    }

    public async Task<MeteredComponent> GetMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        var handle = _settings.MeteredComponentHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingConfigurationException("'Maxio:MeteredComponentHandle' is not configured.");
        }

        ComponentResponse response;
        try
        {
            response = await _maxio.Components.FindComponent(handle: handle, ct: cancellationToken);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingConfigurationException(
                $"No component with handle '{handle}' exists on this Maxio site.");
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure($"look up the metered component '{handle}'", ex.Error);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
        {
            throw Boundary($"look up the metered component '{handle}'", ex);
        }

        var component = MaxioModelMapper.MapComponent(response.Component);

        // UC2 refuses to record usage unless the configured handle really is a metered component
        // on the configured family. Each of these is a seed problem to fix (UC0), not a retry.
        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"Component '{handle}' is of kind '{component.Kind}', not metered, so usage cannot be recorded against it. " +
                "A component's kind cannot be changed in place — archive it and recreate it as metered.");
        }

        if (component.IsArchived)
        {
            throw new BillingConfigurationException($"Component '{handle}' is archived.");
        }

        var familyHandle = response.Component?.ProductFamilyHandle;
        if (!string.IsNullOrWhiteSpace(familyHandle) &&
            !string.Equals(familyHandle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingConfigurationException(
                $"Component '{handle}' lives on product family '{familyHandle}', not the configured " +
                $"'{_settings.ProductFamilyHandle}', so it is not available to these subscriptions.");
        }

        return component;
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _maxio.Customers.ReadCustomerByReference(
                reference: reference, ct: cancellationToken);

            return MaxioModelMapper.TryMapCustomer(response.Customer);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // A clean miss: this eShopOnWeb user has never been enrolled.
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("look up the billing customer", ex.Error);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
        {
            throw Boundary("look up the billing customer", ex);
        }
    }

    public async Task<BillingCustomer> CreateCustomerAsync(string reference,
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken = default)
    {
        const string Action = "create the billing customer";

        CustomerResponse response;
        try
        {
            response = await _maxio.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = reference
                    }
                },
                ct: cancellationToken);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // Maxio's generated 422 payload for customers does not model per-field messages, so it
            // is read best-effort and never used to build user-facing validation text.
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new BillingProviderException(
                    "Maxio rejected the customer details for this account.", 422, providerMessages: null, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Failure(Action, raw);
            }

            throw Unexpected(Action, ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
        {
            throw Boundary(Action, ex);
        }

        return MaxioModelMapper.TryMapCustomer(response.Customer)
               ?? throw new BillingProviderException("Maxio created a customer but returned no usable record.");
    }

    public async Task<IReadOnlyList<Subscription>> ListSubscriptionsForCustomerAsync(BillingCustomer customer,
        CancellationToken cancellationToken = default)
    {
        const string Action = "list the customer's subscriptions";

        try
        {
            var responses = await _maxio.Customers.ListCustomerSubscriptions(
                customerId: customer.Id, ct: cancellationToken);

            return responses
                .Select(r => MaxioModelMapper.MapSubscription(r.Subscription))
                .ToArray();
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<Subscription>();
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure(Action, ex.Error);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
        {
            throw Boundary(Action, ex);
        }
    }

    public async Task<Subscription?> FindSubscriptionByIdAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ReadSubscriptionAsync(subscriptionId, cancellationToken);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("read the subscription", ex.Error);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
        {
            throw Boundary("read the subscription", ex);
        }
    }

    public async Task<Subscription> CreateSubscriptionAsync(BillingCustomer customer,
        BillingPlan plan,
        CancellationToken cancellationToken = default)
    {
        const string Action = "create the subscription";

        SubscriptionResponse response;
        try
        {
            response = await _maxio.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customer.Id,

                        // eShopOnWeb captures no payment instrument, so there is no payment profile
                        // for Maxio to charge. Remittance bills the customer by invoice instead;
                        // without it Maxio refuses the enrolment outright ("no payment method was on
                        // file for the balance"), which is the same reason the plans are seeded with
                        // require_credit_card off.
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                },
                ct: cancellationToken);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw Failure(Action, errors);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Failure(Action, raw);
            }

            throw Unexpected(Action, ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
        {
            throw Boundary(Action, ex);
        }

        return MaxioModelMapper.MapSubscription(response.Subscription);
    }

    public async Task<UsageRecord> RecordUsageAsync(int subscriptionId,
        MeteredComponent component,
        int quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        const string Action = "record the usage";

        try
        {
            var response = await _maxio.SubscriptionComponents.CreateUsage(
                subscriptionIdOrReference: SubscriptionIdOrReference.Int(subscriptionId),
                componentId: ComponentIdModel.Int(component.Id),
                body: new CreateUsageRequest
                {
                    Usage = new CreateUsage
                    {
                        Quantity = quantity,
                        Memo = memo
                    }
                },
                ct: cancellationToken);

            return MaxioModelMapper.MapUsage(response.Usage);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw Failure(Action, errors);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Failure(Action, raw);
            }

            throw Unexpected(Action, ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
        {
            // Recording usage is a POST and is deliberately not retried: it is additive, so a blind
            // resend would double-bill. The caller re-reads the running total instead (UC2).
            throw Boundary(Action, ex);
        }
    }

    public async Task<int> GetPeriodToDateUsageAsync(Subscription subscription,
        MeteredComponent component,
        CancellationToken cancellationToken = default)
    {
        const string Action = "read the period-to-date usage";

        // Usage recorded before the current period belongs to an invoice that has already been
        // issued, so the running total starts at the period boundary.
        var since = subscription.CurrentPeriodStartedAt;
        var total = 0;

        for (var page = 1; page <= MaxPages; page++)
        {
            IReadOnlyList<UsageResponse> responses;
            try
            {
                responses = await _maxio.SubscriptionComponents.ListUsages(
                    subscriptionIdOrReference: SubscriptionIdOrReference.Int(subscription.Id),
                    componentId: ComponentIdModel.Int(component.Id),
                    sinceId: null,
                    maxId: null,
                    sinceDate: since,
                    untilDate: null,
                    page: page,
                    perPage: PageSize,
                    ct: cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                throw Failure(Action, ex.Error);
            }
            catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
            {
                throw Boundary(Action, ex);
            }

            foreach (var response in responses)
            {
                total += MaxioModelMapper.MapUsage(response.Usage).Quantity;
            }

            if (responses.Count < PageSize)
            {
                break;
            }
        }

        return total;
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(Subscription subscription,
        BillingPlan targetPlan,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            // Nothing is prorated when the change waits for the period boundary: the customer
            // simply starts the next period on the new plan's price. There is nothing to ask
            // Maxio, and asking would return an immediate-proration figure that is not what the
            // customer would be charged.
            return new PlanChangePreview(
                subscriptionId: subscription.Id,
                currentPlan: subscription.Plan,
                targetPlan: targetPlan,
                timing: timing,
                proratedCharge: 0m,
                proratedCredit: 0m,
                amountDueNow: 0m,
                effectiveAt: subscription.CurrentPeriodEndsAt);
        }

        const string Action = "preview the plan change";

        SubscriptionMigrationPreviewResponse response;
        try
        {
            response = await _maxio.SubscriptionProducts.PreviewSubscriptionProductMigration(
                subscriptionId: subscription.Id,
                body: new SubscriptionMigrationPreviewRequest
                {
                    Migration = new SubscriptionMigrationPreviewOptions
                    {
                        ProductHandle = targetPlan.Handle
                    }
                },
                ct: cancellationToken);
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw Failure(Action, errors);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Failure(Action, raw);
            }

            throw Unexpected(Action, ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
        {
            throw Boundary(Action, ex);
        }

        var migration = response.Migration;

        return new PlanChangePreview(
            subscriptionId: subscription.Id,
            currentPlan: subscription.Plan,
            targetPlan: targetPlan,
            timing: timing,
            proratedCharge: MaxioModelMapper.CentsToDollars(migration.ChargeInCents),

            // Maxio signs a credit negatively; the domain holds magnitudes, so the sign is dropped
            // here and the credit's direction is carried by the field it lands in.
            proratedCredit: MaxioModelMapper.CentsToDollars(migration.CreditAppliedInCents),

            // What the customer is actually billed on confirming, straight from Maxio, rather than
            // re-derived — a downgrade nets to an account credit, not a refund.
            amountDueNow: MaxioModelMapper.CentsToDollars(migration.PaymentDueInCents),
            effectiveAt: DateTimeOffset.UtcNow);
    }

    public async Task<Subscription> ChangePlanAsync(Subscription subscription,
        BillingPlan targetPlan,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        return timing == PlanChangeTiming.AtNextRenewal
            ? await ScheduleDelayedPlanChangeAsync(subscription.Id, targetPlan, cancellationToken)
            : await MigrateNowAsync(subscription.Id, targetPlan, cancellationToken);
    }

    public async Task<Subscription> PauseSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        const string Action = "pause the subscription";

        try
        {
            // An empty hold body means an indefinite hold: the customer decides when to resume.
            var response = await _maxio.SubscriptionStatus.PauseSubscription(
                subscriptionId: subscriptionId,
                body: new PauseRequest(),
                ct: cancellationToken);

            return MaxioModelMapper.MapSubscription(response.Subscription);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw Failure(Action, errors);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Failure(Action, raw);
            }

            throw Unexpected(Action, ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
        {
            throw Boundary(Action, ex);
        }
    }

    public async Task<Subscription> ResumeSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        const string Action = "resume the subscription";

        try
        {
            var response = await _maxio.SubscriptionStatus.ResumeSubscription(
                subscriptionId: subscriptionId,
                calendarBillingResumptionCharge: null,
                ct: cancellationToken);

            return MaxioModelMapper.MapSubscription(response.Subscription);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw Failure(Action, errors);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Failure(Action, raw);
            }

            throw Unexpected(Action, ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
        {
            throw Boundary(Action, ex);
        }
    }

    public async Task<Subscription> CancelSubscriptionAsync(int subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        return timing == CancellationTiming.EndOfPeriod
            ? await CancelAtEndOfPeriodAsync(subscriptionId, reason, cancellationToken)
            : await CancelNowAsync(subscriptionId, reason, cancellationToken);
    }

    public async Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        const string Action = "reactivate the subscription";

        try
        {
            var response = await _maxio.SubscriptionStatus.ReactivateSubscription(
                subscriptionId: subscriptionId,
                body: new ReactivateSubscriptionRequest(),
                ct: cancellationToken);

            return MaxioModelMapper.MapSubscription(response.Subscription);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw Failure(Action, errors);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Failure(Action, raw);
            }

            throw Unexpected(Action, ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
        {
            throw Boundary(Action, ex);
        }
    }

    private async Task<Subscription> MigrateNowAsync(int subscriptionId,
        BillingPlan targetPlan,
        CancellationToken cancellationToken)
    {
        const string Action = "change the plan";

        try
        {
            var response = await _maxio.SubscriptionProducts.MigrateSubscriptionProduct(
                subscriptionId: subscriptionId,
                body: new SubscriptionProductMigrationRequest
                {
                    Migration = new SubscriptionProductMigration
                    {
                        ProductHandle = targetPlan.Handle
                    }
                },
                ct: cancellationToken);

            return MaxioModelMapper.MapSubscription(response.Subscription);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw Failure(Action, errors);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Failure(Action, raw);
            }

            throw Unexpected(Action, ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
        {
            throw Boundary(Action, ex);
        }
    }

    private async Task<Subscription> ScheduleDelayedPlanChangeAsync(int subscriptionId,
        BillingPlan targetPlan,
        CancellationToken cancellationToken)
    {
        const string Action = "schedule the plan change for the next renewal";

        try
        {
            var response = await _maxio.Subscriptions.UpdateSubscription(
                subscriptionId: subscriptionId,
                body: new UpdateSubscriptionRequest
                {
                    Subscription = new UpdateSubscription
                    {
                        ProductHandle = targetPlan.Handle,
                        ProductChangeDelayed = true
                    }
                },
                ct: cancellationToken);

            return MaxioModelMapper.MapSubscription(response.Subscription);
        }
        catch (SdkException<UpdateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw Failure(Action, errors);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Failure(Action, raw);
            }

            throw Unexpected(Action, ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
        {
            throw Boundary(Action, ex);
        }
    }

    private async Task<Subscription> CancelNowAsync(int subscriptionId,
        string? reason,
        CancellationToken cancellationToken)
    {
        const string Action = "cancel the subscription";

        try
        {
            var response = await _maxio.SubscriptionStatus.CancelSubscription(
                subscriptionId: subscriptionId,
                body: new CancellationRequest
                {
                    Subscription = new CancellationOptions
                    {
                        CancellationMessage = reason
                    }
                },
                ct: cancellationToken);

            return MaxioModelMapper.MapSubscription(response.Subscription);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            if (ex.Error.TryGetNoContent(out var notFound))
            {
                throw Failure(Action, notFound);
            }

            if (ex.Error.TryGetCancelSubscriptionErrorResponse(out var rejected))
            {
                throw FromCancellationRejection(Action, rejected);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Failure(Action, raw);
            }

            throw Unexpected(Action, ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
        {
            throw Boundary(Action, ex);
        }
    }

    private async Task<Subscription> CancelAtEndOfPeriodAsync(int subscriptionId,
        string? reason,
        CancellationToken cancellationToken)
    {
        const string Action = "schedule the cancellation for the end of the period";

        try
        {
            await _maxio.SubscriptionStatus.InitiateDelayedCancellation(
                subscriptionId: subscriptionId,
                body: new CancellationRequest
                {
                    Subscription = new CancellationOptions
                    {
                        CancellationMessage = reason
                    }
                },
                ct: cancellationToken);
        }
        catch (SdkException<InitiateDelayedCancellationError> ex)
        {
            if (ex.Error.TryGetNoContent(out var notFound))
            {
                throw Failure(Action, notFound);
            }

            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw Failure(Action, errors);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Failure(Action, raw);
            }

            throw Unexpected(Action, ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
        {
            throw Boundary(Action, ex);
        }

        // Maxio answers a delayed cancellation with a message only, so the subscription is
        // re-read to report the state and the effective date the customer will actually see.
        try
        {
            return await ReadSubscriptionAsync(subscriptionId, cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("re-read the subscription after scheduling its cancellation", ex.Error);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
        {
            throw Boundary("re-read the subscription after scheduling its cancellation", ex);
        }
    }

    /// <summary>
    /// Reads a subscription without translating errors, so callers can decide whether a 404 means
    /// "absent" or "failed".
    /// </summary>
    private async Task<Subscription> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        var response = await _maxio.Subscriptions.ReadSubscription(
            subscriptionId: subscriptionId,
            include: null,
            ct: cancellationToken);

        return MaxioModelMapper.MapSubscription(response.Subscription);
    }

    /// <summary>
    /// Finds the configured product family by handle. Maxio's generated client cannot read a family
    /// by handle — only by numeric id — so the families are listed and matched here.
    /// </summary>
    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_productFamilyId is int cached)
        {
            return cached;
        }

        var handle = _settings.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingConfigurationException("'Maxio:ProductFamilyHandle' is not configured.");
        }

        const string Action = "list the product families";

        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _maxio.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure(Action, ex.Error);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex, cancellationToken))
        {
            throw Boundary(Action, ex);
        }

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null &&
                                 string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase) &&
                                 !f.ArchivedAt.HasValue);

        if (match?.Id is not int id || id <= 0)
        {
            throw new BillingConfigurationException(
                $"No active product family with handle '{handle}' exists on this Maxio site.");
        }

        _logger.LogInformation("Resolved Maxio product family '{0}' to id {1}.", handle, id);
        _productFamilyId = id;
        return id;
    }

    private static BillingProviderException FromCancellationRejection(string action,
        CancelSubscriptionErrorResponse rejected)
    {
        if (rejected.TryGetErrorListResponse1(out var errors))
        {
            return Failure(action, errors);
        }

        if (rejected.TryGetSingleErrorResponse1(out var single))
        {
            return new BillingProviderException(
                $"Maxio rejected the request to {action}.", 422, new[] { single.Error });
        }

        return new BillingProviderException($"Maxio rejected the request to {action}.", 422, providerMessages: null);
    }

    private static BillingProviderException Failure(string action, ErrorListResponse1 errors) =>
        new($"Maxio rejected the request to {action}.", 422, errors.Errors);

    private static BillingProviderException Failure(string action, RawError raw) =>
        new($"Maxio could not {action}.", (int)raw.StatusCode, new[] { ReadBody(raw) });

    /// <summary>
    /// Translates a failure that escaped the SDK's own error typing — see
    /// <see cref="IsProviderBoundaryFailure"/> — into the seam's one exception type.
    /// </summary>
    private static BillingProviderException Boundary(string action, Exception ex) =>
        ex is System.Text.Json.JsonException
            ? new BillingProviderException(
                $"Maxio returned an error response that could not be read while trying to {action}.", ex)
            : new BillingProviderException($"Maxio could not be reached to {action}.", ex);

    private static BillingProviderException Unexpected(string action, Exception ex) =>
        new($"Maxio returned an unrecognized error while trying to {action}.", ex);

    /// <summary>
    /// Reads an error body as text. Maxio does not always answer with JSON, and the SDK's JSON
    /// reader throws on a non-JSON body, so the raw text is the only safe thing to read.
    /// </summary>
    private static string ReadBody(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            return string.IsNullOrWhiteSpace(body) ? raw.StatusCode.ToString() : body;
        }
        catch (Exception)
        {
            return raw.StatusCode.ToString();
        }
    }

    /// <summary>
    /// Failures that never reach the caller as a typed SDK error and so must be translated here.
    /// </summary>
    /// <remarks>
    /// Two kinds. A network fault or an HTTP timeout never becomes an
    /// <c>SdkException</c> at all. And the SDK deserializes each operation's error body into the
    /// shape it was generated for — where Maxio's real payload differs (its customer 422 is the
    /// known case), deserialization throws <see cref="System.Text.Json.JsonException"/> from inside
    /// the SDK. Letting either escape would break the seam's promise that callers see only
    /// <see cref="BillingProviderException"/>.
    /// <para>
    /// A cancellation the caller actually asked for is deliberately excluded: that is the caller's
    /// own decision, not a provider failure, and it should propagate as cancellation.
    /// </para>
    /// </remarks>
    private static bool IsProviderBoundaryFailure(Exception ex, CancellationToken cancellationToken)
    {
        if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return ex is HttpRequestException
            or OperationCanceledException
            or System.Text.Json.JsonException;
    }

    private const string ListPlansAction = "list the available plans";
}
