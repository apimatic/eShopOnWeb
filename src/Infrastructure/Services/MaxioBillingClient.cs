using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using MaxioModels = MaxioAdvancedBilling.Models;
using MaxioEnums = MaxioAdvancedBilling.Models.Enums;
using MaxioAnyOf = MaxioAdvancedBilling.Models.AnyOf;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single concrete Maxio Advanced Billing client (plan.md §2.2/§4.2). Implements the
/// provider-agnostic <see cref="IBillingClient"/> seam using the generated
/// <see cref="MaxioAdvancedBillingClient"/> SDK client (registered via
/// <see cref="MaxioBillingClientServiceCollectionExtensions.AddMaxioBillingClient"/>). Every SDK
/// exception (typed <c>SdkException&lt;TError&gt;</c> and raw connection failures alike) is translated
/// into ApplicationCore's own exception vocabulary here, so nothing above this class ever sees a
/// Maxio SDK type.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;

    public MaxioBillingClient(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            try
            {
                var products = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: FamilyIdAsString(),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: 1,
                    perPage: 50,
                    ct: cancellationToken);

                IReadOnlyList<BillingPlan> plans = products
                    .Where(p => p.Product is not null)
                    .Select(p => MapPlan(p.Product!))
                    .ToList();

                return plans;
            }
            catch (SdkException<RawError> ex)
            {
                throw new BillingProviderException($"Failed to list plans for product family {_settings.ProductFamilyId}: {DescribeRawError(ex)}", ex);
            }
        });

    public Task<IReadOnlyList<Subscription>> ListSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            MaxioModels.Customer? customer;
            try
            {
                customer = (await _client.Customers.ReadCustomerByReference(reference: customerReference, ct: cancellationToken)).Customer;
            }
            catch (SdkException<RawError> ex) when (ex.Error?.StatusCode == HttpStatusCode.NotFound)
            {
                // No billing-provider customer yet for this eShopOnWeb user -> no subscriptions.
                return (IReadOnlyList<Subscription>)Array.Empty<Subscription>();
            }
            catch (SdkException<RawError> ex)
            {
                throw new BillingProviderException($"Failed to look up billing customer '{customerReference}': {DescribeRawError(ex)}", ex);
            }

            if (customer?.Id is not int customerId)
            {
                return Array.Empty<Subscription>();
            }

            try
            {
                var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: cancellationToken);
                return subscriptions
                    .Where(s => s.Subscription is not null)
                    .Select(s => MapSubscription(s.Subscription!))
                    .ToList();
            }
            catch (SdkException<RawError> ex)
            {
                throw new BillingProviderException($"Failed to list subscriptions for customer '{customerReference}': {DescribeRawError(ex)}", ex);
            }
        });

    public Task<Subscription> CreateSubscriptionAsync(string customerReference, string customerEmail, string productHandle, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var customer = await FindOrCreateCustomerAsync(customerReference, customerEmail, cancellationToken);

            try
            {
                var response = await _client.Subscriptions.CreateSubscription(
                    body: new MaxioModels.CreateSubscriptionRequest
                    {
                        Subscription = new MaxioModels.CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerId = customer.Id,
                            // The configured demo products carry a non-zero balance due at signup and no
                            // trial; plan.md's "requires payment method off" only waives storing/validating
                            // a card, not the need for something to bill against. Invoice collection bills
                            // the customer later instead of attempting an immediate card charge, which is
                            // what lets the demo enroll with no payment method at all (verified live).
                            PaymentCollectionMethod = MaxioEnums.CollectionMethod.Invoice
                        }
                    },
                    ct: cancellationToken);

                return MapSubscription(response.Subscription
                    ?? throw new BillingProviderException($"Empty create-subscription response for product '{productHandle}'."));
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                throw new BillingProviderException($"Failed to create subscription for product '{productHandle}': {DescribeCreateSubscriptionError(ex.Error)}", ex);
            }
        });

    public Task<Subscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            try
            {
                var response = await _client.Subscriptions.ReadSubscription(subscriptionId: subscriptionId, include: null, ct: cancellationToken);
                return MapSubscription(response.Subscription ?? throw new SubscriptionNotFoundException(subscriptionId));
            }
            catch (SdkException<RawError> ex) when (ex.Error?.StatusCode == HttpStatusCode.NotFound)
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }
            catch (SdkException<RawError> ex)
            {
                throw new BillingProviderException($"Failed to read subscription {subscriptionId}: {DescribeRawError(ex)}", ex);
            }
        });

    public Task EnsureMeteredComponentAsync(CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            MaxioModels.ComponentResponse response;
            try
            {
                response = await _client.Components.ReadComponent(
                    productFamilyId: _settings.ProductFamilyId,
                    componentId: $"handle:{_settings.MeteredComponentHandle}",
                    ct: cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                throw new BillingConfigurationException(
                    $"Configured metered component handle '{_settings.MeteredComponentHandle}' does not resolve on product family {_settings.ProductFamilyId}: {DescribeRawError(ex)}. Check the seed (UC0).", ex);
            }

            if (response.Component is null || response.Component.Kind != MaxioEnums.ComponentKind.MeteredComponent)
            {
                throw new BillingConfigurationException(
                    $"Configured component handle '{_settings.MeteredComponentHandle}' is not a metered-kind component. Check the seed (UC0).");
            }
        });

    public Task<UsageReport> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            try
            {
                await _client.SubscriptionComponents.CreateUsage(
                    subscriptionIdOrReference: MaxioAnyOf.SubscriptionIdOrReference.Int(subscriptionId),
                    // ComponentIdModel.String is inserted into the {component_id} path segment verbatim
                    // (no automatic "handle:" prefixing by the union/route-builder) — a bare handle like
                    // "api-call" is looked up as a literal numeric id and 404s. Use the numeric id we
                    // already have configured instead of re-deriving a "handle:"-prefixed string.
                    componentId: MaxioAnyOf.ComponentIdModel.Int(_settings.MeteredComponentId),
                    body: new MaxioModels.CreateUsageRequest
                    {
                        Usage = new MaxioModels.CreateUsage
                        {
                            Quantity = quantity,
                            Memo = memo
                        }
                    },
                    ct: cancellationToken);
            }
            catch (SdkException<CreateUsageError> ex)
            {
                throw new BillingProviderException($"Failed to record usage on subscription {subscriptionId}: {DescribeCreateUsageError(ex.Error)}", ex);
            }

            int? periodToDateTotal = null;
            var totalAvailable = false;
            try
            {
                var componentResponse = await _client.SubscriptionComponents.ReadSubscriptionComponent(
                    subscriptionId: subscriptionId,
                    componentId: _settings.MeteredComponentId,
                    ct: cancellationToken);
                periodToDateTotal = componentResponse.Component?.UnitBalance;
                totalAvailable = periodToDateTotal.HasValue;
            }
            catch (Exception)
            {
                // UC2 failure scenario: a failed read-back must not fail an already-recorded usage —
                // report success with the total marked unavailable instead.
                totalAvailable = false;
            }

            return new UsageReport(subscriptionId, quantity, periodToDateTotal, totalAvailable);
        });

    public Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string currentProductHandle, string targetProductHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            if (timing == PlanChangeTiming.AtNextRenewal)
            {
                // No preview op exists for the delayed path; its own documented behavior is "no
                // proration applies" (UC3), so the meaningful preview is the flat target price and
                // the effective (next renewal) date.
                var targetPriceInCents = await ResolvePlanPriceInCentsAsync(targetProductHandle, cancellationToken);
                var subscription = await GetSubscriptionAsync(subscriptionId, cancellationToken);

                return new PlanChangePreview(
                    subscriptionId, currentProductHandle, targetProductHandle, timing,
                    proratedAdjustmentInCents: null,
                    chargeInCents: null,
                    paymentDueInCents: null,
                    creditAppliedInCents: null,
                    newPlanPriceInCents: targetPriceInCents,
                    effectiveAt: subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt);
            }

            try
            {
                var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(
                    subscriptionId: subscriptionId,
                    body: new MaxioModels.SubscriptionMigrationPreviewRequest
                    {
                        Migration = new MaxioModels.SubscriptionMigrationPreviewOptions
                        {
                            ProductHandle = targetProductHandle,
                            Proration = new MaxioModels.Proration { PreservePeriod = false }
                        }
                    },
                    ct: cancellationToken);

                var migration = response.Migration
                    ?? throw new BillingProviderException($"Empty migration preview response for subscription {subscriptionId}.");

                var targetPriceInCents = await ResolvePlanPriceInCentsAsync(targetProductHandle, cancellationToken);

                return new PlanChangePreview(
                    subscriptionId, currentProductHandle, targetProductHandle, timing,
                    proratedAdjustmentInCents: migration.ProratedAdjustmentInCents,
                    chargeInCents: migration.ChargeInCents,
                    paymentDueInCents: migration.PaymentDueInCents,
                    creditAppliedInCents: migration.CreditAppliedInCents,
                    newPlanPriceInCents: targetPriceInCents,
                    effectiveAt: DateTimeOffset.UtcNow);
            }
            catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
            {
                throw new BillingProviderException($"Failed to preview a plan change for subscription {subscriptionId}: {DescribePreviewSubscriptionProductMigrationError(ex.Error)}", ex);
            }
        });

    public Task<Subscription> CommitPlanChangeAsync(int subscriptionId, string targetProductHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            if (timing == PlanChangeTiming.Now)
            {
                try
                {
                    var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(
                        subscriptionId: subscriptionId,
                        body: new MaxioModels.SubscriptionProductMigrationRequest
                        {
                            Migration = new MaxioModels.SubscriptionProductMigration
                            {
                                ProductHandle = targetProductHandle,
                                Proration = new MaxioModels.Proration { PreservePeriod = false }
                            }
                        },
                        ct: cancellationToken);

                    return MapSubscription(response.Subscription
                        ?? throw new BillingProviderException($"Empty migration response for subscription {subscriptionId}."));
                }
                catch (SdkException<MigrateSubscriptionProductError> ex)
                {
                    throw new BillingProviderException($"Failed to migrate subscription {subscriptionId} to '{targetProductHandle}': {DescribeMigrateSubscriptionProductError(ex.Error)}", ex);
                }
            }

            try
            {
                var response = await _client.Subscriptions.UpdateSubscription(
                    subscriptionId: subscriptionId,
                    body: new MaxioModels.UpdateSubscriptionRequest
                    {
                        Subscription = new MaxioModels.UpdateSubscription
                        {
                            ProductHandle = targetProductHandle,
                            ProductChangeDelayed = true
                        }
                    },
                    ct: cancellationToken);

                return MapSubscription(response.Subscription
                    ?? throw new BillingProviderException($"Empty update response for subscription {subscriptionId}."));
            }
            catch (SdkException<UpdateSubscriptionError> ex)
            {
                throw new BillingProviderException($"Failed to schedule a delayed plan change for subscription {subscriptionId}: {DescribeUpdateSubscriptionError(ex.Error)}", ex);
            }
        });

    public Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            try
            {
                var response = await _client.SubscriptionStatus.PauseSubscription(
                    subscriptionId: subscriptionId,
                    body: new MaxioModels.PauseRequest { Hold = new MaxioModels.AutoResume() },
                    ct: cancellationToken);
                return MapSubscription(response.Subscription ?? throw new BillingProviderException($"Empty pause response for subscription {subscriptionId}."));
            }
            catch (SdkException<PauseSubscriptionError> ex)
            {
                throw new BillingProviderException($"Failed to pause subscription {subscriptionId}: {DescribePauseSubscriptionError(ex.Error)}", ex);
            }
        });

    public Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            try
            {
                var response = await _client.SubscriptionStatus.ResumeSubscription(
                    subscriptionId: subscriptionId,
                    calendarBillingResumptionCharge: null,
                    ct: cancellationToken);
                return MapSubscription(response.Subscription ?? throw new BillingProviderException($"Empty resume response for subscription {subscriptionId}."));
            }
            catch (SdkException<ResumeSubscriptionError> ex)
            {
                throw new BillingProviderException($"Failed to resume subscription {subscriptionId}: {DescribeResumeSubscriptionError(ex.Error)}", ex);
            }
        });

    public Task<Subscription> CancelSubscriptionAsync(int subscriptionId, CancellationTiming timing, string? reason, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var body = new MaxioModels.CancellationRequest
            {
                Subscription = new MaxioModels.CancellationOptions
                {
                    CancellationMessage = reason
                }
            };

            if (timing == CancellationTiming.Immediate)
            {
                try
                {
                    var response = await _client.SubscriptionStatus.CancelSubscription(subscriptionId: subscriptionId, body: body, ct: cancellationToken);
                    return MapSubscription(response.Subscription ?? throw new BillingProviderException($"Empty cancel response for subscription {subscriptionId}."));
                }
                catch (SdkException<CancelSubscriptionApiError> ex)
                {
                    throw new BillingProviderException($"Failed to cancel subscription {subscriptionId}: {DescribeCancelSubscriptionApiError(ex.Error)}", ex);
                }
            }

            try
            {
                await _client.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId: subscriptionId, body: body, ct: cancellationToken);
            }
            catch (SdkException<InitiateDelayedCancellationError> ex)
            {
                throw new BillingProviderException($"Failed to schedule an end-of-period cancellation for subscription {subscriptionId}: {DescribeInitiateDelayedCancellationError(ex.Error)}", ex);
            }

            // InitiateDelayedCancellation returns only a confirmation message, not the subscription — re-read it.
            return await GetSubscriptionAsync(subscriptionId, cancellationToken);
        });

    public Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            try
            {
                var response = await _client.SubscriptionStatus.ReactivateSubscription(
                    subscriptionId: subscriptionId,
                    body: new MaxioModels.ReactivateSubscriptionRequest(),
                    ct: cancellationToken);
                return MapSubscription(response.Subscription ?? throw new BillingProviderException($"Empty reactivate response for subscription {subscriptionId}."));
            }
            catch (SdkException<ReactivateSubscriptionError> ex)
            {
                throw new BillingProviderException($"Failed to reactivate subscription {subscriptionId}: {DescribeReactivateSubscriptionError(ex.Error)}", ex);
            }
        });

    private async Task<MaxioModels.Customer> FindOrCreateCustomerAsync(string reference, string email, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(reference: reference, ct: cancellationToken);
            if (existing.Customer is not null)
            {
                return existing.Customer;
            }
        }
        catch (SdkException<RawError> ex) when (ex.Error?.StatusCode == HttpStatusCode.NotFound)
        {
            // Falls through to create below.
        }

        var (firstName, lastName) = SplitDisplayName(reference, email);
        try
        {
            var created = await _client.Customers.CreateCustomer(
                body: new MaxioModels.CreateCustomerRequest
                {
                    Customer = new MaxioModels.CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = reference
                    }
                },
                ct: cancellationToken);

            return created.Customer ?? throw new BillingProviderException($"Empty create-customer response for reference '{reference}'.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // No SDK-level atomic upsert for customers: a concurrent first-time call for the same
            // reference may have already won and created the customer (422) — look it up once more.
            var retry = await _client.Customers.ReadCustomerByReference(reference: reference, ct: cancellationToken);
            if (retry.Customer is not null)
            {
                return retry.Customer;
            }

            throw new BillingProviderException($"Failed to create or find a billing customer for reference '{reference}': {DescribeCreateCustomerError(ex.Error)}", ex);
        }
    }

    private async Task<long> ResolvePlanPriceInCentsAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => p.Handle == productHandle);
        return plan?.PriceInCents
            ?? throw new BillingConfigurationException($"Target product handle '{productHandle}' does not resolve to a plan in the billing provider.");
    }

    private string FamilyIdAsString() => _settings.ProductFamilyId.ToString(CultureInfo.InvariantCulture);

    private static BillingPlan MapPlan(MaxioModels.Product product) => new(
        id: product.Id ?? 0,
        handle: product.Handle ?? string.Empty,
        name: product.Name ?? string.Empty,
        priceInCents: product.PriceInCents ?? 0,
        interval: product.Interval ?? 0,
        intervalUnit: product.IntervalUnit?.Value ?? string.Empty);

    private static Subscription MapSubscription(MaxioModels.Subscription subscription)
    {
        var ownerReference = subscription.Customer?.Reference
            ?? throw new BillingProviderException($"Subscription {subscription.Id} has no owning customer reference.");

        return new Subscription(
            id: subscription.Id ?? 0,
            ownerReference: ownerReference,
            productHandle: subscription.Product?.Handle ?? string.Empty,
            productId: subscription.Product?.Id ?? 0,
            productPriceInCents: subscription.Product?.PriceInCents ?? 0,
            state: MapState(subscription.State),
            currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            nextAssessmentAt: subscription.NextAssessmentAt,
            createdAt: subscription.CreatedAt);
    }

    private static SubscriptionState MapState(MaxioEnums.SubscriptionState? providerState)
    {
        if (providerState == MaxioEnums.SubscriptionState.Pending) return SubscriptionState.Pending;
        if (providerState == MaxioEnums.SubscriptionState.FailedToCreate) return SubscriptionState.FailedToCreate;
        if (providerState == MaxioEnums.SubscriptionState.Trialing) return SubscriptionState.Trialing;
        if (providerState == MaxioEnums.SubscriptionState.Assessing) return SubscriptionState.Assessing;
        if (providerState == MaxioEnums.SubscriptionState.Active) return SubscriptionState.Active;
        if (providerState == MaxioEnums.SubscriptionState.SoftFailure) return SubscriptionState.SoftFailure;
        if (providerState == MaxioEnums.SubscriptionState.PastDue) return SubscriptionState.PastDue;
        if (providerState == MaxioEnums.SubscriptionState.Suspended) return SubscriptionState.Suspended;
        if (providerState == MaxioEnums.SubscriptionState.Canceled) return SubscriptionState.Canceled;
        if (providerState == MaxioEnums.SubscriptionState.Expired) return SubscriptionState.Expired;
        if (providerState == MaxioEnums.SubscriptionState.Paused) return SubscriptionState.Paused;
        if (providerState == MaxioEnums.SubscriptionState.Unpaid) return SubscriptionState.Unpaid;
        if (providerState == MaxioEnums.SubscriptionState.TrialEnded) return SubscriptionState.TrialEnded;
        if (providerState == MaxioEnums.SubscriptionState.OnHold) return SubscriptionState.OnHold;
        if (providerState == MaxioEnums.SubscriptionState.AwaitingSignup) return SubscriptionState.AwaitingSignup;

        throw new BillingProviderException($"Unrecognized billing-provider subscription state '{providerState}'.");
    }

    private static string DescribeRawError(SdkException<RawError> ex) => ex.Error?.ReadAsString() ?? ex.Message;

    private static string DescribeRawError(RawError raw) => $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}";

    private static string DescribeErrorListResponse1(MaxioModels.ErrorListResponse1 body) => string.Join("; ", body.Errors);

    /// <summary>
    /// Every Case-A operation below shares the identical <c>ErrorListResponse1</c> (422) +
    /// <c>RawError</c> (fallback) error shape per its map row (sdk-map.md → map/operations), but each
    /// <c>{Operation}Error</c> is its own sealed type under <c>Errors/</c> with its own non-shared
    /// <c>TryGetErrorListResponse1</c>/<c>TryGetRawError</c> methods, so one small wrapper per operation
    /// is required rather than a single generic helper.
    /// </summary>
    private static string DescribeCreateSubscriptionError(CreateSubscriptionError error)
    {
        if (error.TryGetErrorListResponse1(out var body)) return DescribeErrorListResponse1(body);
        if (error.TryGetRawError(out var raw)) return DescribeRawError(raw);
        return "(no error detail returned)";
    }

    private static string DescribePreviewSubscriptionProductMigrationError(PreviewSubscriptionProductMigrationError error)
    {
        if (error.TryGetErrorListResponse1(out var body)) return DescribeErrorListResponse1(body);
        if (error.TryGetRawError(out var raw)) return DescribeRawError(raw);
        return "(no error detail returned)";
    }

    private static string DescribeMigrateSubscriptionProductError(MigrateSubscriptionProductError error)
    {
        if (error.TryGetErrorListResponse1(out var body)) return DescribeErrorListResponse1(body);
        if (error.TryGetRawError(out var raw)) return DescribeRawError(raw);
        return "(no error detail returned)";
    }

    private static string DescribeUpdateSubscriptionError(UpdateSubscriptionError error)
    {
        if (error.TryGetErrorListResponse1(out var body)) return DescribeErrorListResponse1(body);
        if (error.TryGetRawError(out var raw)) return DescribeRawError(raw);
        return "(no error detail returned)";
    }

    private static string DescribePauseSubscriptionError(PauseSubscriptionError error)
    {
        if (error.TryGetErrorListResponse1(out var body)) return DescribeErrorListResponse1(body);
        if (error.TryGetRawError(out var raw)) return DescribeRawError(raw);
        return "(no error detail returned)";
    }

    private static string DescribeResumeSubscriptionError(ResumeSubscriptionError error)
    {
        if (error.TryGetErrorListResponse1(out var body)) return DescribeErrorListResponse1(body);
        if (error.TryGetRawError(out var raw)) return DescribeRawError(raw);
        return "(no error detail returned)";
    }

    private static string DescribeReactivateSubscriptionError(ReactivateSubscriptionError error)
    {
        if (error.TryGetErrorListResponse1(out var body)) return DescribeErrorListResponse1(body);
        if (error.TryGetRawError(out var raw)) return DescribeRawError(raw);
        return "(no error detail returned)";
    }

    private static string DescribeCreateUsageError(CreateUsageError error)
    {
        if (error.TryGetErrorListResponse1(out var body)) return DescribeErrorListResponse1(body);
        if (error.TryGetRawError(out var raw)) return DescribeRawError(raw);
        return "(no error detail returned)";
    }

    // InitiateDelayedCancellationError additionally carries a status-specific 404 RawError accessor
    // ahead of the 422 typed body (map row: TryGetNoContent [404] · TryGetErrorListResponse1 [422] ·
    // TryGetRawError [fallback]).
    private static string DescribeInitiateDelayedCancellationError(InitiateDelayedCancellationError error)
    {
        if (error.TryGetNoContent(out var noContent)) return DescribeRawError(noContent);
        if (error.TryGetErrorListResponse1(out var body)) return DescribeErrorListResponse1(body);
        if (error.TryGetRawError(out var raw)) return DescribeRawError(raw);
        return "(no error detail returned)";
    }

    // CancelSubscriptionApiError's 422 body is itself a union (ErrorListResponse1 | SingleErrorResponse1)
    // per map/models/unions.md — unwrap it one level further.
    private static string DescribeCancelSubscriptionApiError(CancelSubscriptionApiError error)
    {
        if (error.TryGetNoContent(out var noContent)) return DescribeRawError(noContent);
        if (error.TryGetCancelSubscriptionErrorResponse(out var union))
        {
            if (union.TryGetErrorListResponse1(out var body)) return DescribeErrorListResponse1(body);
            if (union.TryGetSingleErrorResponse1(out var single)) return single.Error;
        }
        if (error.TryGetRawError(out var raw)) return DescribeRawError(raw);
        return "(no error detail returned)";
    }

    // CreateCustomerError's 422 body (CustomerErrorResponse1.Errors) is the generated SDK's generic
    // "Errors" model (Models/Errors.cs: PerPage/PricePoint string lists) rather than a customer-specific
    // field map — surface whatever non-null lists it carries.
    private static string DescribeCreateCustomerError(CreateCustomerError error)
    {
        if (error.TryGetCustomerErrorResponse1(out var body))
        {
            var messages = new List<string>();
            if (body.Errors?.PerPage is { } perPage) messages.AddRange(perPage);
            if (body.Errors?.PricePoint is { } pricePoint) messages.AddRange(pricePoint);
            if (messages.Count > 0) return string.Join("; ", messages);
        }
        if (error.TryGetRawError(out var raw)) return DescribeRawError(raw);
        return "(no error detail returned)";
    }

    /// <summary>
    /// Wraps every provider call: translates connection-level failures (not covered by
    /// <c>SdkException&lt;T&gt;</c>) into <see cref="BillingProviderException"/>, while letting
    /// ApplicationCore's own exceptions (already-translated by the inner call) pass through untouched.
    /// </summary>
    private static async Task<T> GuardAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (BillingProviderException) { throw; }
        catch (BillingConfigurationException) { throw; }
        catch (SubscriptionNotFoundException) { throw; }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException("Could not reach the billing provider.", ex);
        }
        catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException("The billing provider request timed out.", ex);
        }
    }

    private static async Task GuardAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (BillingProviderException) { throw; }
        catch (BillingConfigurationException) { throw; }
        catch (SubscriptionNotFoundException) { throw; }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException("Could not reach the billing provider.", ex);
        }
        catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException("The billing provider request timed out.", ex);
        }
    }

    private static (string FirstName, string LastName) SplitDisplayName(string reference, string email)
    {
        // eShopOnWeb Identity carries only a username/email, never separate first/last names —
        // derive something reasonable for Maxio's required CreateCustomer fields.
        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : reference;
        var separatorIndex = localPart.IndexOfAny(new[] { '.', '_', '-' });
        if (separatorIndex > 0 && separatorIndex < localPart.Length - 1)
        {
            return (localPart[..separatorIndex], localPart[(separatorIndex + 1)..]);
        }

        return (localPart, "Customer");
    }
}
