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
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;
using Subscription = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single Infrastructure class that talks to Maxio Advanced Billing (§2.2). Implements
/// <see cref="IBillingClient"/> via the generated <see cref="MaxioAdvancedBillingClient"/> SDK,
/// normalizes provider models into ApplicationCore's Subscription domain shapes, and wraps every
/// provider failure into a single <see cref="BillingProviderException"/>.
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

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var plans = new List<BillingPlan>();

        foreach (var handle in new[] { _settings.DefaultProductHandle, _settings.AlternateProductHandle })
        {
            if (string.IsNullOrWhiteSpace(handle))
            {
                continue;
            }

            try
            {
                var response = await _client.Products.ReadProductByHandle(handle, cancellationToken);
                if (response.Product.ArchivedAt is null)
                {
                    plans.Add(MapPlan(response.Product));
                }
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                // Configured handle no longer resolves — skip it rather than failing the whole listing.
            }
            catch (SdkException<RawError> ex)
            {
                throw new BillingProviderException($"Maxio returned HTTP {(int)ex.Error.StatusCode} reading plan '{handle}': {ex.Error.ReadAsString()}", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new BillingProviderException("Maxio is unreachable.", ex);
            }
        }

        return plans;
    }

    public async Task EnsureMeteredComponentConfiguredAsync(CancellationToken cancellationToken = default)
    {
        Component component;
        try
        {
            var response = await _client.Components.FindComponent(_settings.MeteredComponentHandle, cancellationToken);
            component = response.Component;
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException(
                $"Configured metered component handle '{_settings.MeteredComponentHandle}' does not resolve (HTTP {(int)ex.Error.StatusCode}). Verify the sandbox seed (UC0).", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }

        if (component.Kind != ComponentKind.MeteredComponent)
        {
            throw new BillingProviderException(
                $"Component '{_settings.MeteredComponentHandle}' is kind '{component.Kind}', not metered. Verify the sandbox seed (UC0).");
        }
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(string customerReference, string email, CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(customerReference, cancellationToken);
            return MapCustomer(existing.Customer);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // No customer yet for this reference — fall through and create one.
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Maxio returned HTTP {(int)ex.Error.StatusCode} looking up customer: {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }

        var localPart = email.Split('@')[0];
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart,
                LastName = "Customer",
                Email = email,
                Reference = customerReference
            }
        };

        try
        {
            var created = await _client.Customers.CreateCustomer(request, cancellationToken);
            return MapCustomer(created.Customer);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            throw new BillingProviderException(DescribeCreateCustomerError(ex.Error), ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        Customer customer;
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(customerReference, cancellationToken);
            customer = response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<Subscription>();
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Maxio returned HTTP {(int)ex.Error.StatusCode} looking up customer: {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }

        if (customer.Id is not int customerId)
        {
            throw new BillingProviderException("Maxio customer record is missing an id.");
        }

        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, cancellationToken);
            return subscriptions
                .Where(s => s.Subscription is not null)
                .Select(s => MapSubscription(s.Subscription!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Maxio returned HTTP {(int)ex.Error.StatusCode} listing subscriptions: {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<Subscription> CreateSubscriptionAsync(string customerReference, string productHandle, CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                // The demo plans have RequireCreditCard = false, but the site still assesses a
                // balance on signup, which the default "automatic" (card) collection method
                // rejects with no payment method on file. Remittance bills off-system (no card),
                // matching "subscribes without card capture or 3-DS" (§1.3).
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(request, cancellationToken);
            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            string message = ex.Error.TryGetErrorListResponse1(out var errs)
                ? string.Join("; ", errs.Errors)
                : ex.Error.TryGetRawError(out var raw)
                    ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}"
                    : "Maxio rejected the subscription.";
            throw new BillingProviderException(message, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<Subscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId, include: null, cancellationToken);
            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Maxio returned HTTP {(int)ex.Error.StatusCode} reading subscription {subscriptionId}: {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<UsageSummary> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default)
    {
        var request = new CreateUsageRequest
        {
            Usage = new CreateUsage { Quantity = quantity, Memo = memo }
        };

        try
        {
            await _client.SubscriptionComponents.CreateUsage(
                SubscriptionIdOrReference.Int(subscriptionId),
                ComponentIdModel.Int(_settings.MeteredComponentId),
                request,
                cancellationToken);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            string message = ex.Error.TryGetErrorListResponse1(out var errs)
                ? string.Join("; ", errs.Errors)
                : ex.Error.TryGetRawError(out var raw)
                    ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}"
                    : "Maxio rejected the usage record.";
            throw new BillingProviderException(message, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }

        int? periodToDateTotal = null;
        try
        {
            var componentResponse = await _client.SubscriptionComponents.ReadSubscriptionComponent(subscriptionId, _settings.MeteredComponentId, cancellationToken);
            periodToDateTotal = componentResponse.Component?.UnitBalance;
        }
        catch
        {
            // The usage record above already succeeded — report success with the total unavailable
            // rather than failing the whole operation (§UC2 failure scenario).
        }

        return new UsageSummary(subscriptionId, _settings.MeteredComponentHandle, quantity, memo, periodToDateTotal);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string currentProductHandle, string targetProductHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        if (timing == PlanChangeTiming.Now)
        {
            var request = new SubscriptionMigrationPreviewRequest
            {
                Migration = new SubscriptionMigrationPreviewOptions { ProductHandle = targetProductHandle }
            };

            try
            {
                var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(subscriptionId, request, cancellationToken);
                var migration = response.Migration;
                return new PlanChangePreview(
                    subscriptionId,
                    currentProductHandle,
                    targetProductHandle,
                    timing,
                    comparableAmountInCents: migration.ProratedAdjustmentInCents ?? 0,
                    proratedAdjustmentInCents: migration.ProratedAdjustmentInCents,
                    chargeInCents: migration.ChargeInCents,
                    creditAppliedInCents: migration.CreditAppliedInCents,
                    effectiveAt: DateTimeOffset.UtcNow);
            }
            catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
            {
                string message = ex.Error.TryGetErrorListResponse1(out var errs)
                    ? string.Join("; ", errs.Errors)
                    : ex.Error.TryGetRawError(out var raw)
                        ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}"
                        : "Maxio rejected the plan-change preview.";
                throw new BillingProviderException(message, ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new BillingProviderException("Maxio is unreachable.", ex);
            }
        }

        // AtRenewal: no proration applies — the comparable amount is simply the target plan's
        // price, effective at the current period's end.
        ProductResponse targetProductResponse;
        try
        {
            targetProductResponse = await _client.Products.ReadProductByHandle(targetProductHandle, cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Configured product handle '{targetProductHandle}' does not resolve. Verify the sandbox seed (UC0).", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }

        var currentSubscription = await GetSubscriptionAsync(subscriptionId, cancellationToken);
        var targetPriceInCents = targetProductResponse.Product.PriceInCents ?? 0;

        return new PlanChangePreview(
            subscriptionId,
            currentProductHandle,
            targetProductHandle,
            timing,
            comparableAmountInCents: targetPriceInCents,
            proratedAdjustmentInCents: null,
            chargeInCents: targetPriceInCents,
            creditAppliedInCents: null,
            effectiveAt: currentSubscription.CurrentPeriodEndsAt ?? DateTimeOffset.UtcNow);
    }

    public async Task<Subscription> ApplyPlanChangeNowAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default)
    {
        var request = new SubscriptionProductMigrationRequest
        {
            Migration = new SubscriptionProductMigration { ProductHandle = targetProductHandle }
        };

        try
        {
            var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(subscriptionId, request, cancellationToken);
            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            string message = ex.Error.TryGetErrorListResponse1(out var errs)
                ? string.Join("; ", errs.Errors)
                : ex.Error.TryGetRawError(out var raw)
                    ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}"
                    : "Maxio rejected the plan change.";
            throw new BillingProviderException(message, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<Subscription> SchedulePlanChangeAtRenewalAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default)
    {
        var request = new UpdateSubscriptionRequest
        {
            Subscription = new UpdateSubscription { ProductHandle = targetProductHandle, ProductChangeDelayed = true }
        };

        try
        {
            var response = await _client.Subscriptions.UpdateSubscription(subscriptionId, request, cancellationToken);
            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<UpdateSubscriptionError> ex)
        {
            string message = ex.Error.TryGetErrorListResponse1(out var errs)
                ? string.Join("; ", errs.Errors)
                : ex.Error.TryGetRawError(out var raw)
                    ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}"
                    : "Maxio rejected the scheduled plan change.";
            throw new BillingProviderException(message, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.PauseSubscription(subscriptionId, new PauseRequest(), cancellationToken);
            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            string message = ex.Error.TryGetErrorListResponse1(out var errs)
                ? string.Join("; ", errs.Errors)
                : ex.Error.TryGetRawError(out var raw)
                    ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}"
                    : "Maxio rejected the pause request.";
            throw new BillingProviderException(message, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ResumeSubscription(subscriptionId, calendarBillingResumptionCharge: null, cancellationToken);
            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            string message = ex.Error.TryGetErrorListResponse1(out var errs)
                ? string.Join("; ", errs.Errors)
                : ex.Error.TryGetRawError(out var raw)
                    ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}"
                    : "Maxio rejected the resume request.";
            throw new BillingProviderException(message, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<Subscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default)
    {
        if (endOfPeriod)
        {
            var delayedRequest = new CancellationRequest
            {
                Subscription = new CancellationOptions { CancelAtEndOfPeriod = true, CancellationMessage = reason }
            };

            try
            {
                await _client.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId, delayedRequest, cancellationToken);
            }
            catch (SdkException<InitiateDelayedCancellationError> ex)
            {
                string message = ex.Error.TryGetNoContent(out _)
                    ? $"Subscription {subscriptionId} was not found."
                    : ex.Error.TryGetErrorListResponse1(out var errs)
                        ? string.Join("; ", errs.Errors)
                        : ex.Error.TryGetRawError(out var raw)
                            ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}"
                            : "Maxio rejected the scheduled cancellation.";
                throw new BillingProviderException(message, ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new BillingProviderException("Maxio is unreachable.", ex);
            }

            return await GetSubscriptionAsync(subscriptionId, cancellationToken);
        }

        var immediateRequest = new CancellationRequest
        {
            Subscription = new CancellationOptions { CancellationMessage = reason }
        };

        try
        {
            var response = await _client.SubscriptionStatus.CancelSubscription(subscriptionId, immediateRequest, cancellationToken);
            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            throw new BillingProviderException(DescribeCancelSubscriptionError(subscriptionId, ex.Error), ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, new ReactivateSubscriptionRequest(), cancellationToken);
            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            string message = ex.Error.TryGetErrorListResponse1(out var errs)
                ? string.Join("; ", errs.Errors)
                : ex.Error.TryGetRawError(out var raw)
                    ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}"
                    : "Maxio rejected the reactivation.";
            throw new BillingProviderException(message, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    private static string DescribeCreateCustomerError(CreateCustomerError error)
    {
        if (error.TryGetCustomerErrorResponse1(out var body))
        {
            var messages = new List<string>();
            if (body.Errors?.PerPage is { Count: > 0 } perPage) messages.AddRange(perPage);
            if (body.Errors?.PricePoint is { Count: > 0 } pricePoint) messages.AddRange(pricePoint);
            return messages.Count > 0 ? string.Join("; ", messages) : "Customer could not be created due to a validation error.";
        }

        return error.TryGetRawError(out var raw)
            ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}"
            : "Maxio rejected the customer creation.";
    }

    private static string DescribeCancelSubscriptionError(int subscriptionId, CancelSubscriptionApiError error)
    {
        if (error.TryGetNoContent(out _))
        {
            return $"Subscription {subscriptionId} was not found.";
        }

        if (error.TryGetCancelSubscriptionErrorResponse(out var union))
        {
            if (union.TryGetErrorListResponse1(out var errs))
            {
                return string.Join("; ", errs.Errors);
            }

            if (union.TryGetSingleErrorResponse1(out var single))
            {
                return single.Error;
            }
        }

        return error.TryGetRawError(out var raw)
            ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}"
            : "Maxio rejected the cancellation.";
    }

    private static BillingCustomer MapCustomer(Customer customer)
        => new(customer.Id ?? 0, customer.Reference ?? string.Empty, customer.Email ?? string.Empty);

    private static BillingPlan MapPlan(Product product)
        => new(
            product.Handle ?? string.Empty,
            product.Name ?? string.Empty,
            product.PriceInCents ?? 0,
            product.Interval ?? 1,
            MapIntervalUnit(product.IntervalUnit),
            // RequireCreditCard is the flag that actually blocks subscription creation without a
            // payment method; RequestCreditCard only solicits one without making it mandatory.
            product.RequireCreditCard ?? false);

    private static BillingIntervalUnit MapIntervalUnit(IntervalUnit? unit)
        => unit == IntervalUnit.Day ? BillingIntervalUnit.Day : BillingIntervalUnit.Month;

    private static Subscription MapSubscription(MaxioSubscription subscription)
    {
        var buyerId = subscription.Customer?.Reference ?? string.Empty;

        return new Subscription(
            subscription.Id ?? 0,
            buyerId,
            buyerId,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            subscription.Product?.PriceInCents ?? 0,
            MapStatus(subscription.State),
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt,
            subscription.CancelAtEndOfPeriod ?? false,
            subscription.DelayedCancelAt,
            subscription.BalanceInCents);
    }

    private static SubscriptionStatus MapStatus(SubscriptionState? state)
    {
        if (state == SubscriptionState.Pending) return SubscriptionStatus.Pending;
        if (state == SubscriptionState.AwaitingSignup) return SubscriptionStatus.AwaitingSignup;
        if (state == SubscriptionState.Trialing) return SubscriptionStatus.Trialing;
        if (state == SubscriptionState.TrialEnded) return SubscriptionStatus.TrialEnded;
        if (state == SubscriptionState.Assessing) return SubscriptionStatus.Assessing;
        if (state == SubscriptionState.Active) return SubscriptionStatus.Active;
        if (state == SubscriptionState.SoftFailure) return SubscriptionStatus.SoftFailure;
        if (state == SubscriptionState.PastDue) return SubscriptionStatus.PastDue;
        if (state == SubscriptionState.Unpaid) return SubscriptionStatus.Unpaid;
        if (state == SubscriptionState.Suspended) return SubscriptionStatus.Suspended;
        if (state == SubscriptionState.OnHold) return SubscriptionStatus.OnHold;
        if (state == SubscriptionState.Paused) return SubscriptionStatus.Paused;
        if (state == SubscriptionState.Canceled) return SubscriptionStatus.Canceled;
        if (state == SubscriptionState.Expired) return SubscriptionStatus.Expired;
        if (state == SubscriptionState.FailedToCreate) return SubscriptionStatus.FailedToCreate;
        return SubscriptionStatus.Unknown;
    }
}
