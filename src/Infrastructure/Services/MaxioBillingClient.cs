using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single Infrastructure seam onto Maxio Advanced Billing (§2.2). Implements
/// <see cref="IBillingClient"/> via the generated <c>AsadAli.AdvancedBilling.Sdk</c> client,
/// normalizes every result into ApplicationCore-owned DTOs, and translates SDK errors into
/// <see cref="BillingProviderException"/> / <see cref="BillingConfigurationException"/> so that
/// ApplicationCore never sees a Maxio type.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;

    public MaxioBillingClient(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> options)
    {
        _client = client;
        _settings = options.Value;
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var responses = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _settings.ProductFamilyId.ToString(CultureInfo.InvariantCulture),
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

            return responses
                .Select(r => r.Product)
                .Where(p => p is not null)
                .Select(p => ToBillingPlan(p!))
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFoundMessage))
            {
                throw new BillingConfigurationException(
                    $"Product family '{_settings.ProductFamilyHandle}' (id {_settings.ProductFamilyId}) was not found in Maxio. Re-run UC0 seeding or correct Maxio:ProductFamilyId. Provider message: {notFoundMessage}");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToProviderException(raw, "listing plans");
            }
            throw new BillingProviderException("Failed to list plans.", ex);
        }
    }

    public async Task ValidateMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        Component component;
        try
        {
            var response = await _client.Components.FindComponent(handle: _settings.MeteredComponentHandle, ct: cancellationToken);
            component = response.Component;
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingConfigurationException(
                $"Metered component '{_settings.MeteredComponentHandle}' could not be read from Maxio (HTTP {(int)ex.Error.StatusCode}). Re-run UC0 seeding or correct Maxio:MeteredComponentHandle.");
        }

        if (component.Kind != ComponentKind.MeteredComponent)
        {
            throw new BillingConfigurationException(
                $"Component '{_settings.MeteredComponentHandle}' is of kind '{component.Kind}', not metered. UC2 requires a metered component — re-run UC0 seeding.");
        }
    }

    public async Task<long?> TryFindCustomerIdAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: customerReference, ct: cancellationToken);
            return response.Customer.Id;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            throw ToProviderException(ex.Error, "finding customer");
        }
    }

    public async Task<long> FindOrCreateCustomerAsync(string customerReference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var existingId = await TryFindCustomerIdAsync(customerReference, cancellationToken);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        try
        {
            var response = await _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = customerReference
                    }
                },
                ct: cancellationToken);

            return response.Customer.Id
                ?? throw new BillingProviderException("Maxio returned no customer id after CreateCustomer.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // The reference likely collided with a customer created concurrently by another
                // request (Reference is unique on Maxio's side) — read it back rather than failing.
                var racedId = await TryFindCustomerIdAsync(customerReference, cancellationToken);
                if (racedId is not null)
                {
                    return racedId.Value;
                }
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToProviderException(raw, "creating customer");
            }
            throw new BillingProviderException("Failed to create Maxio customer.", ex);
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(customerId: (int)customerId, ct: cancellationToken);
            return responses
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => ToBillingSubscription(s!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex.Error, "listing customer subscriptions");
        }
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        CustomerId = (int)customerId,
                        ProductHandle = productHandle,
                        // The demo plans have RequireCreditCard=false, but Maxio still rejects
                        // subscription creation with "No payment method was on file" unless the
                        // collection method is explicitly non-card — Invoice bills without a card
                        // capture or 3-DS step, matching UC1/UC0's "no card capture" requirement.
                        PaymentCollectionMethod = CollectionMethod.Invoice
                    }
                },
                ct: cancellationToken);

            return ToBillingSubscriptionOrThrow(response, 0);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new BillingProviderException($"Maxio rejected subscription creation: {string.Join("; ", errorList.Errors)}");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToProviderException(raw, "creating subscription");
            }
            throw new BillingProviderException("Failed to create subscription.", ex);
        }
    }

    public async Task<BillingSubscription> GetSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(
                subscriptionId: (int)subscriptionId,
                include: null,
                ct: cancellationToken);

            return ToBillingSubscriptionOrThrow(response, subscriptionId);
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }
            throw ToProviderException(ex.Error, "reading subscription");
        }
    }

    public async Task<long> RecordUsageAsync(long subscriptionId, double quantity, string? memo, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionComponents.CreateUsage(
                subscriptionIdOrReference: (int)subscriptionId,
                componentId: (int)_settings.MeteredComponentId,
                body: new CreateUsageRequest
                {
                    Usage = new CreateUsage
                    {
                        Quantity = quantity,
                        Memo = memo
                    }
                },
                ct: cancellationToken);

            return response.Usage.Id
                ?? throw new BillingProviderException("Maxio returned no usage id after CreateUsage.");
        }
        catch (SdkException<CreateUsageError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new BillingProviderException($"Maxio rejected usage recording: {string.Join("; ", errorList.Errors)}");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToProviderException(raw, "recording usage");
            }
            throw new BillingProviderException("Failed to record usage.", ex);
        }
    }

    public async Task<int?> TryGetPeriodToDateUsageAsync(long subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionComponents.ReadSubscriptionComponent(
                subscriptionId: (int)subscriptionId,
                componentId: (int)_settings.MeteredComponentId,
                ct: cancellationToken);

            return response.Component?.UnitBalance;
        }
        catch (SdkException<ReadSubscriptionComponentError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToProviderException(raw, "reading period-to-date usage");
            }
            throw new BillingProviderException("Failed to read period-to-date usage.", ex);
        }
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(long subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default)
    {
        var current = await GetSubscriptionAsync(subscriptionId, cancellationToken);

        try
        {
            var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(
                subscriptionId: (int)subscriptionId,
                body: new SubscriptionMigrationPreviewRequest
                {
                    Migration = new SubscriptionMigrationPreviewOptions
                    {
                        ProductHandle = targetProductHandle,
                        Proration = new Proration { PreservePeriod = true }
                    }
                },
                ct: cancellationToken);

            var preview = response.Migration;
            return new PlanChangePreview(
                subscriptionId,
                current.ProductHandle,
                targetProductHandle,
                preview.ProratedAdjustmentInCents ?? 0,
                preview.ChargeInCents ?? 0,
                preview.PaymentDueInCents ?? 0,
                preview.CreditAppliedInCents ?? 0);
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new BillingProviderException($"Maxio rejected the plan-change preview: {string.Join("; ", errorList.Errors)}");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToProviderException(raw, "previewing plan change");
            }
            throw new BillingProviderException("Failed to preview plan change.", ex);
        }
    }

    public async Task<BillingSubscription> CommitPlanChangeAsync(long subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(
                subscriptionId: (int)subscriptionId,
                body: new SubscriptionProductMigrationRequest
                {
                    Migration = new SubscriptionProductMigration
                    {
                        ProductHandle = targetProductHandle,
                        Proration = new Proration { PreservePeriod = true }
                    }
                },
                ct: cancellationToken);

            return ToBillingSubscriptionOrThrow(response, subscriptionId);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new BillingProviderException($"Maxio rejected the plan change: {string.Join("; ", errorList.Errors)}");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToProviderException(raw, "committing plan change");
            }
            throw new BillingProviderException("Failed to commit plan change.", ex);
        }
    }

    public async Task<BillingSubscription> PauseSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.PauseSubscription(
                subscriptionId: (int)subscriptionId,
                body: null,
                ct: cancellationToken);

            return ToBillingSubscriptionOrThrow(response, subscriptionId);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new BillingProviderException($"Maxio rejected pausing the subscription: {string.Join("; ", errorList.Errors)}");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToProviderException(raw, "pausing subscription");
            }
            throw new BillingProviderException("Failed to pause subscription.", ex);
        }
    }

    public async Task<BillingSubscription> ResumeSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ResumeSubscription(
                subscriptionId: (int)subscriptionId,
                calendarBillingResumptionCharge: null,
                ct: cancellationToken);

            return ToBillingSubscriptionOrThrow(response, subscriptionId);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new BillingProviderException($"Maxio rejected resuming the subscription: {string.Join("; ", errorList.Errors)}");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToProviderException(raw, "resuming subscription");
            }
            throw new BillingProviderException("Failed to resume subscription.", ex);
        }
    }

    public async Task<BillingSubscription> CancelSubscriptionAsync(long subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.CancelSubscription(
                subscriptionId: (int)subscriptionId,
                body: new CancellationRequest
                {
                    Subscription = new CancellationOptions
                    {
                        CancelAtEndOfPeriod = endOfPeriod,
                        CancellationMessage = reason
                    }
                },
                ct: cancellationToken);

            return ToBillingSubscriptionOrThrow(response, subscriptionId);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }
            if (ex.Error.TryGetCancelSubscriptionErrorResponse(out var errorResponse))
            {
                throw new BillingProviderException($"Maxio rejected cancellation of subscription {subscriptionId}: {errorResponse}");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToProviderException(raw, "cancelling subscription");
            }
            throw new BillingProviderException("Failed to cancel subscription.", ex);
        }
    }

    public async Task<BillingSubscription> ReactivateSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ReactivateSubscription(
                subscriptionId: (int)subscriptionId,
                body: null,
                ct: cancellationToken);

            return ToBillingSubscriptionOrThrow(response, subscriptionId);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new BillingProviderException($"Maxio rejected reactivation: {string.Join("; ", errorList.Errors)}");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToProviderException(raw, "reactivating subscription");
            }
            throw new BillingProviderException("Failed to reactivate subscription.", ex);
        }
    }

    private static BillingSubscription ToBillingSubscriptionOrThrow(SubscriptionResponse response, long fallbackSubscriptionId)
    {
        if (response.Subscription is null)
        {
            throw new SubscriptionNotFoundException(fallbackSubscriptionId);
        }
        return ToBillingSubscription(response.Subscription);
    }

    private static BillingSubscription ToBillingSubscription(Subscription subscription)
    {
        var customer = subscription.Customer;
        var product = subscription.Product;

        return new BillingSubscription(
            id: subscription.Id ?? 0,
            customerId: customer?.Id ?? 0,
            customerReference: customer?.Reference ?? string.Empty,
            productId: product?.Id ?? 0,
            productHandle: product?.Handle ?? string.Empty,
            productName: product?.Name ?? string.Empty,
            productPriceInCents: product?.PriceInCents ?? subscription.ProductPriceInCents ?? 0,
            state: ToLifecycleState(subscription.State),
            balanceInCents: subscription.BalanceInCents ?? 0,
            currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            nextAssessmentAt: subscription.NextAssessmentAt);
    }

    private static BillingPlan ToBillingPlan(Product product) =>
        new(
            product.Id ?? 0,
            product.Handle ?? string.Empty,
            product.Name ?? string.Empty,
            product.PriceInCents ?? 0,
            product.RequireCreditCard ?? false);

    private static SubscriptionLifecycleState ToLifecycleState(SubscriptionState? state)
    {
        if (state == SubscriptionState.Pending) return SubscriptionLifecycleState.Pending;
        if (state == SubscriptionState.FailedToCreate) return SubscriptionLifecycleState.FailedToCreate;
        if (state == SubscriptionState.Trialing) return SubscriptionLifecycleState.Trialing;
        if (state == SubscriptionState.Assessing) return SubscriptionLifecycleState.Assessing;
        if (state == SubscriptionState.Active) return SubscriptionLifecycleState.Active;
        if (state == SubscriptionState.SoftFailure) return SubscriptionLifecycleState.SoftFailure;
        if (state == SubscriptionState.PastDue) return SubscriptionLifecycleState.PastDue;
        if (state == SubscriptionState.Suspended) return SubscriptionLifecycleState.Suspended;
        if (state == SubscriptionState.Canceled) return SubscriptionLifecycleState.Canceled;
        if (state == SubscriptionState.Expired) return SubscriptionLifecycleState.Expired;
        if (state == SubscriptionState.Paused) return SubscriptionLifecycleState.Paused;
        if (state == SubscriptionState.Unpaid) return SubscriptionLifecycleState.Unpaid;
        if (state == SubscriptionState.TrialEnded) return SubscriptionLifecycleState.TrialEnded;
        if (state == SubscriptionState.OnHold) return SubscriptionLifecycleState.OnHold;
        if (state == SubscriptionState.AwaitingSignup) return SubscriptionLifecycleState.AwaitingSignup;

        throw new BillingProviderException($"Unrecognized Maxio subscription state '{state}'.");
    }

    private static BillingProviderException ToProviderException(RawError raw, string action)
    {
        string body;
        try
        {
            body = raw.ReadAsString();
        }
        catch (Exception)
        {
            body = "(no response body)";
        }

        return new BillingProviderException($"Maxio request failed while {action} (HTTP {(int)raw.StatusCode}): {body}");
    }
}
