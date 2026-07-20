using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single Infrastructure seam that talks to Maxio Advanced Billing. Implements the
/// provider-agnostic <see cref="IBillingClient"/> via the maxio-sdk-clone SDK
/// (<c>AsadAli.AdvancedBilling.Sdk</c>). Nothing else in this codebase touches the provider.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;

    // Maxio assigns numeric ids and reassigns them whenever the catalog is re-seeded; only
    // handles are stable (plan.md §1.3). Every numeric id this client needs is therefore
    // resolved from its handle at call time, never trusted from configuration, and cached
    // in-memory for the lifetime of this (scoped) instance to avoid a lookup per call.
    private BillingProductFamily? _cachedFamily;
    private BillingMeteredComponent? _cachedComponent;

    public MaxioBillingClient(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> options)
    {
        _client = client;
        _settings = options.Value;
    }

    public async Task<BillingProductFamily> GetProductFamilyAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedFamily is not null)
        {
            return _cachedFamily;
        }

        try
        {
            var families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: cancellationToken);

            var match = families
                .Select(f => f.ProductFamily)
                .FirstOrDefault(f => f is not null && string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

            if (match?.Id is null || match.Handle is null)
            {
                throw new BillingConfigurationException($"Product family handle '{_settings.ProductFamilyHandle}' did not resolve at the provider. Verify UC0 seed state.");
            }

            _cachedFamily = new BillingProductFamily(match.Id.Value, match.Handle, match.Name);
            return _cachedFamily;
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapConfigError($"product family handle '{_settings.ProductFamilyHandle}'", ex.Error);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = await GetProductFamilyAsync(cancellationToken);

        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: family.Id.ToString(),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 50,
                ct: cancellationToken);

            return products
                .Select(p => p.Product)
                .Where(p => p is not null && p.Id.HasValue && p.Handle is not null)
                .Select(p => MapPlan(p!))
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
            {
                throw new BillingConfigurationException($"Product family '{family.Handle}' (id {family.Id}) did not resolve while listing plans: {notFound}. Verify UC0 seed state.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw WrapRawFallback(raw);
            }

            throw new BillingProviderException($"Maxio rejected listing plans for product family '{family.Handle}' (id {family.Id}).", ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<BillingPlan> GetPlanByHandleAsync(string productHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Products.ReadProductByHandle(productHandle, ct: cancellationToken);
            var product = response.Product;
            if (product?.Id is null)
            {
                throw new BillingConfigurationException($"Product handle '{productHandle}' did not resolve at the provider. Verify UC0 seed state.");
            }

            return MapPlan(product);
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapConfigError($"product handle '{productHandle}'", ex.Error);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<BillingMeteredComponent> GetMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedComponent is not null)
        {
            return _cachedComponent;
        }

        try
        {
            var response = await _client.Components.FindComponent(_settings.MeteredComponentHandle, ct: cancellationToken);
            var component = response.Component;
            if (component?.Id is null)
            {
                throw new BillingConfigurationException($"Metered component handle '{_settings.MeteredComponentHandle}' did not resolve at the provider. Verify UC0 seed state.");
            }

            var isMetered = component.Kind == ComponentKind.MeteredComponent;
            if (!isMetered)
            {
                throw new BillingConfigurationException($"Component handle '{_settings.MeteredComponentHandle}' resolved but is not of metered kind (kind: {component.Kind}). Verify UC0 seed state.");
            }

            _cachedComponent = new BillingMeteredComponent(component.Id.Value, component.Handle ?? _settings.MeteredComponentHandle, isMetered, ParsePricePerUnitInCents(component));
            return _cachedComponent;
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapConfigError($"metered component handle '{_settings.MeteredComponentHandle}'", ex.Error);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: cancellationToken);
            var customer = response.Customer;
            return customer?.Id is null ? null : MapCustomer(customer);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapRawFallback(ex.Error);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var request = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = reference
                }
            };

            var response = await _client.Customers.CreateCustomer(request, ct: cancellationToken);
            var customer = response.Customer;
            if (customer?.Id is null)
            {
                throw new BillingProviderException($"Maxio accepted the customer create call for reference '{reference}' but returned no customer id.");
            }

            return MapCustomer(customer);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var typed))
            {
                var details = string.Join("; ",
                    (typed.Errors?.PerPage ?? Array.Empty<string>())
                        .Concat(typed.Errors?.PricePoint ?? Array.Empty<string>()));

                if (!string.IsNullOrWhiteSpace(details))
                {
                    throw new BillingProviderException($"Maxio rejected customer creation for reference '{reference}': {details}", ex);
                }
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Maxio rejected customer creation for reference '{reference}' (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}", ex);
            }

            throw new BillingProviderException($"Maxio rejected customer creation for reference '{reference}'.", ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<BillingSubscription?> FindLiveSubscriptionAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await ListSubscriptionsForCustomerAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s => s.IsLive);
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct: cancellationToken);
            return subscriptions
                .Select(s => s.Subscription)
                .Where(s => s is not null && s.Id.HasValue)
                .Select(s => MapSubscription(s!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapRawFallback(ex.Error);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    // The sandbox's products require a payment method at signup — see the
                    // TestCreditCardNumber doc-comment on MaxioSettings for why this is a
                    // configurable, non-secret test value rather than a card-free call.
                    CreditCardAttributes = new PaymentProfileAttributes
                    {
                        FullNumber = _settings.TestCreditCardNumber,
                        ExpirationMonth = ExpirationMonth2.Int(_settings.TestCreditCardExpirationMonth),
                        ExpirationYear = ExpirationYear2.Int(_settings.TestCreditCardExpirationYear),
                        Cvv = _settings.TestCreditCardCvv
                    }
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(request, ct: cancellationToken);
            var subscription = response.Subscription;
            if (subscription?.Id is null)
            {
                throw new BillingProviderException($"Maxio accepted the subscription create call for customer {customerId} but returned no subscription id.");
            }

            return MapSubscription(subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw WrapErrorListResponse(ex.Error, $"create subscription for customer {customerId} on plan '{productHandle}'");
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: cancellationToken);
            var subscription = response.Subscription;
            if (subscription?.Id is null)
            {
                throw new BillingProviderException($"Subscription {subscriptionId} was not found.");
            }

            return MapSubscription(subscription);
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapRawFallback(ex.Error);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<UsageRecord> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        var component = await GetMeteredComponentAsync(cancellationToken);

        try
        {
            var request = new CreateUsageRequest
            {
                Usage = new CreateUsage
                {
                    Quantity = (double)quantity,
                    Memo = memo
                }
            };

            var response = await _client.SubscriptionComponents.CreateUsage(
                SubscriptionIdOrReference.Int(subscriptionId),
                ComponentIdModel.Int(component.Id),
                request,
                ct: cancellationToken);

            var usage = response.Usage;
            if (usage?.Id is null)
            {
                throw new BillingProviderException($"Maxio accepted the usage record for subscription {subscriptionId} but returned no usage id.");
            }

            return new UsageRecord(usage.Id.Value, quantity, usage.Memo);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            throw WrapErrorListResponse(ex.Error, $"record usage for subscription {subscriptionId}");
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<int> GetMeteredUsageBalanceAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var component = await GetMeteredComponentAsync(cancellationToken);

        try
        {
            var response = await _client.SubscriptionComponents.ReadSubscriptionComponent(subscriptionId, component.Id, ct: cancellationToken);
            return response.Component?.UnitBalance ?? 0;
        }
        catch (SdkException<ReadSubscriptionComponentError> ex)
        {
            if (ex.Error.TryGetNoContent(out var notFound))
            {
                throw new BillingProviderException($"Metered component balance for subscription {subscriptionId} could not be read (HTTP {(int)notFound.StatusCode}): {notFound.ReadAsString()}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw WrapRawFallback(raw);
            }

            throw new BillingProviderException($"Maxio rejected reading the metered component balance for subscription {subscriptionId}.", ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyNow, CancellationToken cancellationToken = default)
    {
        // Resolve/validate the target handle first in both branches, so an unknown handle fails
        // fast as a BillingConfigurationException pointing back at UC0, rather than surfacing as
        // a generic provider rejection from the migration-preview endpoint.
        var targetPlan = await GetPlanByHandleAsync(targetProductHandle, cancellationToken);

        if (applyNow)
        {
            try
            {
                var request = new SubscriptionMigrationPreviewRequest
                {
                    Migration = new SubscriptionMigrationPreviewOptions
                    {
                        ProductHandle = targetProductHandle,
                        PreservePeriod = true
                    }
                };

                var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(subscriptionId, request, ct: cancellationToken);
                var migration = response.Migration;

                return new PlanChangePreview(
                    applyNow: true,
                    proratedAdjustmentInCents: migration?.ProratedAdjustmentInCents,
                    chargeInCents: migration?.ChargeInCents,
                    paymentDueInCents: migration?.PaymentDueInCents,
                    creditAppliedInCents: migration?.CreditAppliedInCents,
                    targetPriceInCents: targetPlan.PriceInCents,
                    effectiveAt: null,
                    note: null);
            }
            catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
            {
                throw WrapErrorListResponse(ex.Error, $"preview prorated plan change for subscription {subscriptionId} to '{targetProductHandle}'");
            }
            catch (Exception ex) when (IsConnectionFailure(ex))
            {
                throw Unreachable(ex);
            }
        }

        // No SDK preview exists for the "apply at next renewal, no proration" path (confirmed
        // gap in the contract sheet). The only accurate number is the target plan's flat price,
        // effective at the subscription's next renewal date.
        var subscription = await GetSubscriptionAsync(subscriptionId, cancellationToken);
        var plan = targetPlan;

        return new PlanChangePreview(
            applyNow: false,
            proratedAdjustmentInCents: null,
            chargeInCents: null,
            paymentDueInCents: null,
            creditAppliedInCents: null,
            targetPriceInCents: plan.PriceInCents,
            effectiveAt: subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            note: "No proration preview is available for a delayed (at-renewal) plan change; the amount shown is the target plan's flat price, which will apply at the next renewal with no proration.");
    }

    public async Task<BillingSubscription> CommitPlanChangeNowAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new SubscriptionProductMigrationRequest
            {
                Migration = new SubscriptionProductMigration
                {
                    ProductHandle = targetProductHandle,
                    PreservePeriod = true
                }
            };

            var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(subscriptionId, request, ct: cancellationToken);
            var subscription = response.Subscription;
            if (subscription?.Id is null)
            {
                throw new BillingProviderException($"Maxio accepted the plan-change call for subscription {subscriptionId} but returned no subscription.");
            }

            return MapSubscription(subscription);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            throw WrapErrorListResponse(ex.Error, $"apply prorated plan change for subscription {subscriptionId} to '{targetProductHandle}'");
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<BillingSubscription> SchedulePlanChangeAtRenewalAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new UpdateSubscriptionRequest
            {
                Subscription = new UpdateSubscription
                {
                    ProductHandle = targetProductHandle,
                    ProductChangeDelayed = true
                }
            };

            var response = await _client.Subscriptions.UpdateSubscription(subscriptionId, request, ct: cancellationToken);
            var subscription = response.Subscription;
            if (subscription?.Id is null)
            {
                throw new BillingProviderException($"Maxio accepted the delayed plan-change call for subscription {subscriptionId} but returned no subscription.");
            }

            return MapSubscription(subscription);
        }
        catch (SdkException<UpdateSubscriptionError> ex)
        {
            throw WrapErrorListResponse(ex.Error, $"schedule delayed plan change for subscription {subscriptionId} to '{targetProductHandle}'");
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.PauseSubscription(subscriptionId, body: null, ct: cancellationToken);
            return RequireSubscription(response.Subscription, subscriptionId, "pause");
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            throw WrapErrorListResponse(ex.Error, $"pause subscription {subscriptionId}");
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ResumeSubscription(subscriptionId, calendarBillingResumptionCharge: null, ct: cancellationToken);
            return RequireSubscription(response.Subscription, subscriptionId, "resume");
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            throw WrapErrorListResponse(ex.Error, $"resume subscription {subscriptionId}");
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default)
    {
        var request = new CancellationRequest
        {
            Subscription = new CancellationOptions
            {
                CancellationMessage = reason
            }
        };

        if (endOfPeriod)
        {
            try
            {
                await _client.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId, request, ct: cancellationToken);
            }
            catch (SdkException<InitiateDelayedCancellationError> ex)
            {
                if (ex.Error.TryGetNoContent(out var notFound))
                {
                    throw new BillingProviderException($"Subscription {subscriptionId} was not found while scheduling end-of-period cancellation (HTTP {(int)notFound.StatusCode}): {notFound.ReadAsString()}", ex);
                }

                throw WrapErrorListResponse(ex.Error, $"schedule end-of-period cancellation for subscription {subscriptionId}");
            }
            catch (Exception ex) when (IsConnectionFailure(ex))
            {
                throw Unreachable(ex);
            }

            return await GetSubscriptionAsync(subscriptionId, cancellationToken);
        }

        try
        {
            var response = await _client.SubscriptionStatus.CancelSubscription(subscriptionId, request, ct: cancellationToken);
            return RequireSubscription(response.Subscription, subscriptionId, "cancel");
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            if (ex.Error.TryGetNoContent(out var notFound))
            {
                throw new BillingProviderException($"Subscription {subscriptionId} was not found while cancelling (HTTP {(int)notFound.StatusCode}): {notFound.ReadAsString()}", ex);
            }

            if (ex.Error.TryGetCancelSubscriptionErrorResponse(out var union))
            {
                if (union.TryGetErrorListResponse1(out var list))
                {
                    throw new BillingProviderException($"Maxio rejected cancelling subscription {subscriptionId}: {string.Join("; ", list.Errors ?? Array.Empty<string>())}", ex);
                }

                if (union.TryGetSingleErrorResponse1(out var single))
                {
                    throw new BillingProviderException($"Maxio rejected cancelling subscription {subscriptionId}: {single.Error}", ex);
                }
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Maxio rejected cancelling subscription {subscriptionId} (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}", ex);
            }

            throw new BillingProviderException($"Maxio rejected cancelling subscription {subscriptionId}.", ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new ReactivateSubscriptionRequest
            {
                Resume = Resume.Bool(true)
            };

            var response = await _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, request, ct: cancellationToken);
            return RequireSubscription(response.Subscription, subscriptionId, "reactivate");
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            throw WrapErrorListResponse(ex.Error, $"reactivate subscription {subscriptionId}");
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unreachable(ex);
        }
    }

    private static BillingSubscription RequireSubscription(Subscription? subscription, int subscriptionId, string action)
    {
        if (subscription?.Id is null)
        {
            throw new BillingProviderException($"Maxio accepted the {action} call for subscription {subscriptionId} but returned no subscription.");
        }

        return MapSubscription(subscription);
    }

    private static BillingPlan MapPlan(Product product)
    {
        return new BillingPlan(
            product.Id!.Value,
            product.Handle ?? string.Empty,
            product.Name,
            product.PriceInCents ?? 0,
            product.IntervalUnit?.Value,
            product.Interval);
    }

    // Component.UnitPrice ("unit_price") carries this doc-comment in the SDK's own source
    // (Models/Component.cs): "The amount the customer will be charged per unit. This field
    // is only populated for 'per_unit' pricing schemes, otherwise it may be null." Its sibling
    // Component.PricePerUnitInCents ("price_per_unit_in_cents") is documented in the same file
    // as "deprecated - use unit_price instead" and is observed null on live per_unit metered
    // components (e.g. handle "api-call"). UnitPrice is therefore the canonical source for
    // per-unit price; PricePerUnitInCents is only a legacy fallback when UnitPrice is absent.
    private static long ParsePricePerUnitInCents(Component component)
    {
        if (!string.IsNullOrWhiteSpace(component.UnitPrice) &&
            decimal.TryParse(component.UnitPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var unitPrice))
        {
            return (long)Math.Round(unitPrice * 100m, MidpointRounding.AwayFromZero);
        }

        return component.PricePerUnitInCents ?? 0;
    }

    private static BillingCustomer MapCustomer(Customer customer)
    {
        return new BillingCustomer(customer.Id!.Value, customer.Reference, customer.Email);
    }

    private static BillingSubscription MapSubscription(Subscription subscription)
    {
        return new BillingSubscription(
            subscription.Id!.Value,
            subscription.Customer?.Id ?? 0,
            subscription.Customer?.Reference,
            subscription.Product?.Id,
            subscription.Product?.Handle,
            subscription.ProductPriceInCents,
            MapState(subscription.State),
            subscription.NextAssessmentAt,
            subscription.CurrentPeriodEndsAt,
            subscription.CancelAtEndOfPeriod ?? false,
            subscription.DelayedCancelAt,
            subscription.NextProductHandle);
    }

    private static SubscriptionLifecycleState MapState(SubscriptionState? state)
    {
        if (state is null) return SubscriptionLifecycleState.Other;
        if (state == SubscriptionState.Pending || state == SubscriptionState.AwaitingSignup) return SubscriptionLifecycleState.Pending;
        if (state == SubscriptionState.Trialing || state == SubscriptionState.TrialEnded) return SubscriptionLifecycleState.Trialing;
        if (state == SubscriptionState.Active || state == SubscriptionState.Assessing) return SubscriptionLifecycleState.Active;
        if (state == SubscriptionState.PastDue || state == SubscriptionState.SoftFailure) return SubscriptionLifecycleState.PastDue;
        if (state == SubscriptionState.Paused || state == SubscriptionState.OnHold) return SubscriptionLifecycleState.Paused;
        if (state == SubscriptionState.Canceled) return SubscriptionLifecycleState.Canceled;
        if (state == SubscriptionState.Expired) return SubscriptionLifecycleState.Expired;
        if (state == SubscriptionState.Unpaid) return SubscriptionLifecycleState.Unpaid;
        return SubscriptionLifecycleState.Other;
    }

    private static BillingConfigurationException WrapConfigError(string what, RawError raw)
    {
        return new BillingConfigurationException($"Could not resolve {what} at the provider (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}. Verify UC0 seed state.");
    }

    private static BillingProviderException WrapRawFallback(RawError raw)
    {
        return new BillingProviderException($"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}");
    }

    private static BillingProviderException DescribeErrorList(ErrorListResponse1? list, RawError? raw, string action)
    {
        if (list is not null)
        {
            return new BillingProviderException($"Maxio rejected an attempt to {action}: {string.Join("; ", list.Errors ?? Array.Empty<string>())}");
        }

        if (raw is not null)
        {
            return new BillingProviderException($"Maxio rejected an attempt to {action} (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}");
        }

        return new BillingProviderException($"Maxio rejected an attempt to {action}.");
    }

    private static BillingProviderException WrapErrorListResponse(CreateSubscriptionError error, string action)
    {
        return DescribeErrorList(
            error.TryGetErrorListResponse1(out var list) ? list : null,
            error.TryGetRawError(out var raw) ? raw : null,
            action);
    }

    private static BillingProviderException WrapErrorListResponse(CreateUsageError error, string action)
    {
        return DescribeErrorList(
            error.TryGetErrorListResponse1(out var list) ? list : null,
            error.TryGetRawError(out var raw) ? raw : null,
            action);
    }

    private static BillingProviderException WrapErrorListResponse(PreviewSubscriptionProductMigrationError error, string action)
    {
        return DescribeErrorList(
            error.TryGetErrorListResponse1(out var list) ? list : null,
            error.TryGetRawError(out var raw) ? raw : null,
            action);
    }

    private static BillingProviderException WrapErrorListResponse(MigrateSubscriptionProductError error, string action)
    {
        return DescribeErrorList(
            error.TryGetErrorListResponse1(out var list) ? list : null,
            error.TryGetRawError(out var raw) ? raw : null,
            action);
    }

    private static BillingProviderException WrapErrorListResponse(UpdateSubscriptionError error, string action)
    {
        return DescribeErrorList(
            error.TryGetErrorListResponse1(out var list) ? list : null,
            error.TryGetRawError(out var raw) ? raw : null,
            action);
    }

    private static BillingProviderException WrapErrorListResponse(PauseSubscriptionError error, string action)
    {
        return DescribeErrorList(
            error.TryGetErrorListResponse1(out var list) ? list : null,
            error.TryGetRawError(out var raw) ? raw : null,
            action);
    }

    private static BillingProviderException WrapErrorListResponse(ResumeSubscriptionError error, string action)
    {
        return DescribeErrorList(
            error.TryGetErrorListResponse1(out var list) ? list : null,
            error.TryGetRawError(out var raw) ? raw : null,
            action);
    }

    private static BillingProviderException WrapErrorListResponse(InitiateDelayedCancellationError error, string action)
    {
        return DescribeErrorList(
            error.TryGetErrorListResponse1(out var list) ? list : null,
            error.TryGetRawError(out var raw) ? raw : null,
            action);
    }

    private static BillingProviderException WrapErrorListResponse(ReactivateSubscriptionError error, string action)
    {
        return DescribeErrorList(
            error.TryGetErrorListResponse1(out var list) ? list : null,
            error.TryGetRawError(out var raw) ? raw : null,
            action);
    }

    private static bool IsConnectionFailure(Exception ex) => ex is HttpRequestException or TaskCanceledException;

    private static BillingProviderException Unreachable(Exception ex) => new("Maxio unreachable", ex);
}
