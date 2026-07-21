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
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Subscription = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single Infrastructure class that talks to Maxio Advanced Billing. Implements
/// <see cref="IBillingClient"/> against the generated <see cref="MaxioAdvancedBillingClient"/>,
/// translating every provider failure into a <see cref="BillingProviderException"/> so nothing
/// above this class ever sees a Maxio-specific exception or model type.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings, IAppLogger<MaxioBillingClient> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct = default)
    {
        var handles = new[] { _settings.DefaultProductHandle, _settings.AlternateProductHandle }
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct();

        var plans = new List<BillingPlan>();
        foreach (var handle in handles)
        {
            var product = await ReadProductByHandleAsync(handle, ct);
            plans.Add(MapPlan(product));
        }

        return plans;
    }

    public async Task<BillingComponent> ValidateMeteredComponentAsync(CancellationToken ct = default)
    {
        MaxioAdvancedBilling.Models.Component component;
        try
        {
            var response = await _client.Components.FindComponent(handle: _settings.MeteredComponentHandle, ct: ct);
            component = response.Component;
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapRawError(ex.Error, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw WrapConnectionFailure(ex);
        }

        var mapped = MapComponent(component);
        if (!mapped.IsMetered)
        {
            throw new BillingProviderException(
                $"Configured component '{mapped.Handle}' is of kind {mapped.Kind}, not Metered. Fix the sandbox seed (UC0) before recording usage.",
                BillingErrorKind.Validation);
        }

        return mapped;
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(string customerReference, string email, string firstName, string lastName, CancellationToken ct = default)
    {
        var existing = await FindCustomerAsync(customerReference, ct);
        if (existing != null)
        {
            return existing;
        }

        try
        {
            var response = await _client.Customers.CreateCustomer(body: new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = customerReference
                }
            }, ct: ct);

            return MapCustomer(response.Customer);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A concurrent request may have created the same reference between our lookup and this create.
            var raceWinner = await FindCustomerAsync(customerReference, ct);
            if (raceWinner != null)
            {
                return raceWinner;
            }

            var hasRaw = ex.Error.TryGetRawError(out var raw);
            var message = hasRaw ? SafeReadRaw(raw!) : ex.Message;
            var kind = hasRaw ? ClassifyByStatus(raw!.StatusCode) : BillingErrorKind.Validation;
            var statusCode = hasRaw ? (int)raw!.StatusCode : (int?)null;
            throw new BillingProviderException(message, kind, statusCode, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw WrapConnectionFailure(ex);
        }
    }

    public async Task<BillingCustomer?> FindCustomerAsync(string customerReference, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: customerReference, ct: ct);
            return MapCustomer(response.Customer);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapRawError(ex.Error, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw WrapConnectionFailure(ex);
        }
    }

    public async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct);
            return response.Select(r => MapSubscription(r.Subscription)).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapRawError(ex.Error, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw WrapConnectionFailure(ex);
        }
    }

    public async Task<Subscription> CreateSubscriptionAsync(int customerId, string customerReference, string productHandle, CancellationToken ct = default)
    {
        var product = await ReadProductByHandleAsync(productHandle, ct);
        if (product.RequireCreditCard == true)
        {
            throw new BillingProviderException(
                $"Product '{productHandle}' requires a payment method; this demo only supports no-card-capture plans. Fix the sandbox seed (UC0).",
                BillingErrorKind.Validation);
        }

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body: new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    // No-card-capture demo plans (RequireCreditCard=false) still need an explicit
                    // non-automatic collection method, or the site 422s asking for a card on file
                    // regardless of the product's own require-card flag.
                    PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice
                }
            }, ct: ct);

            return MapSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw BuildValidationException(ex.Error.TryGetErrorListResponse1(out var errList) ? errList : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw WrapConnectionFailure(ex);
        }
    }

    public async Task<Subscription> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId: subscriptionId, include: null, ct: ct);
            return MapSubscription(response.Subscription);
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapRawError(ex.Error, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw WrapConnectionFailure(ex);
        }
    }

    public async Task<UsageRecordResult> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken ct = default)
    {
        var component = await ValidateMeteredComponentAsync(ct);

        long usageId;
        try
        {
            var response = await _client.SubscriptionComponents.CreateUsage(
                subscriptionIdOrReference: SubscriptionIdOrReference.Int(subscriptionId),
                componentId: ComponentIdModel.String($"handle:{component.Handle}"),
                body: new CreateUsageRequest
                {
                    Usage = new CreateUsage
                    {
                        Quantity = quantity,
                        Memo = memo
                    }
                },
                ct: ct);

            usageId = response.Usage.Id ?? 0;
        }
        catch (SdkException<CreateUsageError> ex)
        {
            throw BuildValidationException(ex.Error.TryGetErrorListResponse1(out var errList) ? errList : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw WrapConnectionFailure(ex);
        }

        int? periodToDateUnits = null;
        try
        {
            var readBack = await _client.SubscriptionComponents.ReadSubscriptionComponent(subscriptionId, component.Id, ct);
            periodToDateUnits = readBack.Component?.UnitBalance;
        }
        catch (Exception ex)
        {
            // Best-effort only: the usage report above already succeeded (UC2 failure scenarios).
            _logger.LogWarning("Usage {Quantity} recorded for subscription {SubscriptionId}, but reading back the period-to-date total failed: {Message}", quantity, subscriptionId, ex.Message);
        }

        return new UsageRecordResult(usageId, quantity, memo, periodToDateUnits);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(subscriptionId, new SubscriptionMigrationPreviewRequest
            {
                Migration = new SubscriptionMigrationPreviewOptions
                {
                    ProductHandle = targetProductHandle
                }
            }, ct);

            var migration = response.Migration;
            return new PlanChangePreview(
                targetProductHandle,
                migration.ProratedAdjustmentInCents ?? 0,
                migration.ChargeInCents ?? 0,
                migration.PaymentDueInCents ?? 0,
                migration.CreditAppliedInCents ?? 0);
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            throw BuildValidationException(ex.Error.TryGetErrorListResponse1(out var errList) ? errList : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw WrapConnectionFailure(ex);
        }
    }

    public async Task<Subscription> CommitPlanChangeNowAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(subscriptionId, new SubscriptionProductMigrationRequest
            {
                Migration = new SubscriptionProductMigration
                {
                    ProductHandle = targetProductHandle
                }
            }, ct);

            return MapSubscription(response.Subscription);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            throw BuildValidationException(ex.Error.TryGetErrorListResponse1(out var errList) ? errList : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw WrapConnectionFailure(ex);
        }
    }

    public async Task<Subscription> SchedulePlanChangeAtRenewalAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Subscriptions.UpdateSubscription(subscriptionId, new UpdateSubscriptionRequest
            {
                Subscription = new UpdateSubscription
                {
                    ProductHandle = targetProductHandle,
                    ProductChangeDelayed = true
                }
            }, ct);

            return MapSubscription(response.Subscription);
        }
        catch (SdkException<UpdateSubscriptionError> ex)
        {
            throw BuildValidationException(ex.Error.TryGetErrorListResponse1(out var errList) ? errList : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw WrapConnectionFailure(ex);
        }
    }

    public async Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            await _client.SubscriptionStatus.PauseSubscription(subscriptionId, body: null, ct: ct);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            throw BuildValidationException(ex.Error.TryGetErrorListResponse1(out var errList) ? errList : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw WrapConnectionFailure(ex);
        }

        return await GetSubscriptionAsync(subscriptionId, ct);
    }

    public async Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            await _client.SubscriptionStatus.ResumeSubscription(subscriptionId, calendarBillingResumptionCharge: null, ct: ct);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            throw BuildValidationException(ex.Error.TryGetErrorListResponse1(out var errList) ? errList : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw WrapConnectionFailure(ex);
        }

        return await GetSubscriptionAsync(subscriptionId, ct);
    }

    public async Task<Subscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, CancellationToken ct = default)
    {
        try
        {
            if (endOfPeriod)
            {
                await _client.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId, new CancellationRequest
                {
                    Subscription = new CancellationOptions
                    {
                        CancelAtEndOfPeriod = true
                    }
                }, ct);
            }
            else
            {
                await _client.SubscriptionStatus.CancelSubscription(subscriptionId, body: null, ct: ct);
            }
        }
        catch (SdkException<InitiateDelayedCancellationError> ex)
        {
            throw BuildValidationException(ex.Error.TryGetErrorListResponse1(out var errList) ? errList : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            var hasRaw = ex.Error.TryGetRawError(out var raw);
            var message = hasRaw ? SafeReadRaw(raw!) : ex.Message;
            var kind = hasRaw ? ClassifyByStatus(raw!.StatusCode) : BillingErrorKind.ProviderRejected;
            var statusCode = hasRaw ? (int)raw!.StatusCode : (int?)null;
            throw new BillingProviderException(message, kind, statusCode, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw WrapConnectionFailure(ex);
        }

        return await GetSubscriptionAsync(subscriptionId, ct);
    }

    public async Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            await _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, body: null, ct: ct);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            throw BuildValidationException(ex.Error.TryGetErrorListResponse1(out var errList) ? errList : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw WrapConnectionFailure(ex);
        }

        return await GetSubscriptionAsync(subscriptionId, ct);
    }

    private async Task<Product> ReadProductByHandleAsync(string handle, CancellationToken ct)
    {
        try
        {
            var response = await _client.Products.ReadProductByHandle(apiHandle: handle, ct: ct);
            return response.Product;
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapRawError(ex.Error, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw WrapConnectionFailure(ex);
        }
    }

    private static BillingProviderException BuildValidationException(ErrorListResponse1? errorList, RawError? raw, Exception inner)
    {
        if (errorList?.Errors is { Count: > 0 })
        {
            return new BillingProviderException(string.Join("; ", errorList.Errors), BillingErrorKind.Validation, raw != null ? (int)raw.StatusCode : null, inner);
        }

        if (raw != null)
        {
            return new BillingProviderException(SafeReadRaw(raw), ClassifyByStatus(raw.StatusCode), (int)raw.StatusCode, inner);
        }

        return new BillingProviderException(inner.Message, BillingErrorKind.Validation, null, inner);
    }

    private static BillingProviderException WrapRawError(RawError raw, Exception inner) =>
        new(SafeReadRaw(raw), ClassifyByStatus(raw.StatusCode), (int)raw.StatusCode, inner);

    private static BillingProviderException WrapConnectionFailure(Exception inner) =>
        new("Maxio Advanced Billing could not be reached.", BillingErrorKind.ConnectionFailure, null, inner);

    private static bool IsConnectionFailure(Exception ex) => ex is HttpRequestException or TaskCanceledException;

    private static BillingErrorKind ClassifyByStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.NotFound => BillingErrorKind.NotFound,
        (HttpStatusCode)422 => BillingErrorKind.Validation,
        _ => BillingErrorKind.ProviderRejected
    };

    private static string SafeReadRaw(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch
        {
            return $"Maxio request failed with status {(int)raw.StatusCode}.";
        }
    }

    private static BillingPlan MapPlan(Product product) => new(
        product.Id ?? 0,
        product.Handle ?? string.Empty,
        product.Name ?? product.Handle ?? string.Empty,
        product.PriceInCents ?? 0,
        product.Interval ?? 1,
        product.IntervalUnit?.Value ?? "month",
        product.RequireCreditCard ?? false);

    private static Subscription MapSubscription(MaxioAdvancedBilling.Models.Subscription? subscription)
    {
        if (subscription == null)
        {
            throw new BillingProviderException("Maxio returned an empty subscription payload.", BillingErrorKind.ProviderRejected);
        }

        var product = subscription.Product;
        return new Subscription(
            subscription.Id ?? 0,
            product?.Handle ?? string.Empty,
            product?.Name ?? product?.Handle ?? string.Empty,
            product?.PriceInCents ?? 0,
            MapStatus(subscription.State),
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt,
            subscription.CancelAtEndOfPeriod ?? false,
            subscription.DelayedCancelAt);
    }

    private static BillingComponent MapComponent(MaxioAdvancedBilling.Models.Component component) => new(
        component.Id ?? 0,
        component.Handle ?? string.Empty,
        MapKind(component.Kind),
        component.UnitName);

    private static BillingCustomer MapCustomer(Customer customer) => new(
        customer.Id ?? 0,
        customer.Reference ?? string.Empty,
        customer.Email ?? string.Empty);

    private static ComponentKind MapKind(MaxioAdvancedBilling.Models.Enums.ComponentKind? kind)
    {
        if (kind == MaxioAdvancedBilling.Models.Enums.ComponentKind.MeteredComponent) return ComponentKind.Metered;
        if (kind == MaxioAdvancedBilling.Models.Enums.ComponentKind.QuantityBasedComponent) return ComponentKind.QuantityBased;
        if (kind == MaxioAdvancedBilling.Models.Enums.ComponentKind.OnOffComponent) return ComponentKind.OnOff;
        if (kind == MaxioAdvancedBilling.Models.Enums.ComponentKind.PrepaidUsageComponent) return ComponentKind.PrepaidUsage;
        if (kind == MaxioAdvancedBilling.Models.Enums.ComponentKind.EventBasedComponent) return ComponentKind.EventBased;
        return ComponentKind.Unknown;
    }

    private static SubscriptionStatus MapStatus(MaxioAdvancedBilling.Models.Enums.SubscriptionState? state)
    {
        if (state == MaxioAdvancedBilling.Models.Enums.SubscriptionState.Pending) return SubscriptionStatus.Pending;
        if (state == MaxioAdvancedBilling.Models.Enums.SubscriptionState.FailedToCreate) return SubscriptionStatus.FailedToCreate;
        if (state == MaxioAdvancedBilling.Models.Enums.SubscriptionState.Trialing) return SubscriptionStatus.Trialing;
        if (state == MaxioAdvancedBilling.Models.Enums.SubscriptionState.Assessing) return SubscriptionStatus.Assessing;
        if (state == MaxioAdvancedBilling.Models.Enums.SubscriptionState.Active) return SubscriptionStatus.Active;
        if (state == MaxioAdvancedBilling.Models.Enums.SubscriptionState.SoftFailure) return SubscriptionStatus.SoftFailure;
        if (state == MaxioAdvancedBilling.Models.Enums.SubscriptionState.PastDue) return SubscriptionStatus.PastDue;
        if (state == MaxioAdvancedBilling.Models.Enums.SubscriptionState.Suspended) return SubscriptionStatus.Suspended;
        if (state == MaxioAdvancedBilling.Models.Enums.SubscriptionState.Canceled) return SubscriptionStatus.Canceled;
        if (state == MaxioAdvancedBilling.Models.Enums.SubscriptionState.Expired) return SubscriptionStatus.Expired;
        if (state == MaxioAdvancedBilling.Models.Enums.SubscriptionState.Paused) return SubscriptionStatus.Paused;
        if (state == MaxioAdvancedBilling.Models.Enums.SubscriptionState.Unpaid) return SubscriptionStatus.Unpaid;
        if (state == MaxioAdvancedBilling.Models.Enums.SubscriptionState.TrialEnded) return SubscriptionStatus.TrialEnded;
        if (state == MaxioAdvancedBilling.Models.Enums.SubscriptionState.OnHold) return SubscriptionStatus.OnHold;
        if (state == MaxioAdvancedBilling.Models.Enums.SubscriptionState.AwaitingSignup) return SubscriptionStatus.AwaitingSignup;
        return SubscriptionStatus.Unknown;
    }
}
