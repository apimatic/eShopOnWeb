using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core;
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
/// The single integration point with Maxio Advanced Billing (§2.2/§4.2 of the integration plan).
/// Nothing else in eShopOnWeb references the Maxio SDK directly.
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

    public async Task EnsureConfigurationValidAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.ProductFamilies.ReadProductFamily(_settings.ProductFamilyId, ct: cancellationToken);

            var componentResponse = await _client.Components.ReadComponent(
                _settings.ProductFamilyId, $"handle:{_settings.MeteredComponentHandle}", ct: cancellationToken);

            var kind = componentResponse.Component?.Kind;
            if (kind != ComponentKind.MeteredComponent)
            {
                throw new BillingProviderException(
                    $"Configured metered component '{_settings.MeteredComponentHandle}' is not of Metered kind (actual: {kind}). Re-check UC0's seed (see plan §UC0).");
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw Wrap(ex, "validate the configured product family/component");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _settings.ProductFamilyId.ToString(),
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
                .Where(p => p is not null)
                .Select(p => MapPlan(p!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw Wrap(ex, "list plans");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(string customerReference, string email, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var (firstName, lastName) = SplitDisplayName(customerReference, email);
            var response = await _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = customerReference
                }
            }, ct: cancellationToken);

            return MapCustomer(response.Customer!, customerReference, email);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A duplicate reference is a 422, not an upsert (plan.md trap notes, Step 4). The typed
            // 422 body for this operation doesn't model customer-domain errors at all, so treat this
            // as a race with a concurrent create: re-check by reference rather than parsing the body.
            var retried = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (retried is not null)
            {
                return retried;
            }

            throw WrapError("create customer", ex,
                () => ex.Error.TryGetRawError(out var raw) ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}" : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(customerReference, ct: cancellationToken);
            return response.Customer is null ? null : MapCustomer(response.Customer, customerReference, response.Customer.Email ?? string.Empty);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Wrap(ex, $"look up the Maxio customer for reference '{customerReference}'");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(int billingCustomerId, string planHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = billingCustomerId,
                    ProductHandle = planHandle,
                    // These demo products have RequireCreditCard = false, but that field is documented
                    // as legacy/deprecated (plan.md §5.2) and the site still rejects subscription
                    // creation with no payment profile on file. CreateSubscription.NextBillingAt's own
                    // doc comment states that a future timestamp means "no payment will be captured at
                    // all" at signup — confirmed empirically against this sandbox to also satisfy the
                    // payment-method-on-file check. A short, practically-immediate deferral keeps the
                    // no-card-capture demo experience (plan.md §1.3) working without inventing any
                    // unsupported request shape.
                    NextBillingAt = DateTimeOffset.UtcNow.AddMinutes(5)
                }
            }, ct: cancellationToken);

            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw WrapError("create subscription", ex,
                () => ex.Error.TryGetErrorListResponse1(out var errs) ? string.Join("; ", errs.Errors) : null,
                () => ex.Error.TryGetRawError(out var raw) ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}" : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int billingCustomerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(billingCustomerId, ct: cancellationToken);
            return responses
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw Wrap(ex, $"list subscriptions for customer {billingCustomerId}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: cancellationToken);
            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<RawError> ex)
        {
            throw Wrap(ex, $"read subscription {subscriptionId}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.SubscriptionComponents.CreateUsage(
                SubscriptionIdOrReference.Int(subscriptionId),
                ComponentIdModel.Int(_settings.MeteredComponentId),
                new CreateUsageRequest
                {
                    Usage = new CreateUsage
                    {
                        Quantity = quantity,
                        Memo = memo
                    }
                },
                ct: cancellationToken);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            throw WrapError("record usage", ex,
                () => ex.Error.TryGetErrorListResponse1(out var errs) ? string.Join("; ", errs.Errors) : null,
                () => ex.Error.TryGetRawError(out var raw) ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}" : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<int> GetMeteredUsageBalanceAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionComponents.ReadSubscriptionComponent(subscriptionId, _settings.MeteredComponentId, ct: cancellationToken);
            return response.Component?.UnitBalance ?? 0;
        }
        catch (SdkException<ReadSubscriptionComponentError> ex)
        {
            throw WrapError("read the metered usage balance", ex,
                () => ex.Error.TryGetNoContent(out var notFound) ? $"HTTP {(int)notFound.StatusCode}: component not found on subscription {subscriptionId}" : null,
                () => ex.Error.TryGetRawError(out var raw) ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}" : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<BillingPlanChangePreview> PreviewPlanChangeNowAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(subscriptionId, new SubscriptionMigrationPreviewRequest
            {
                Migration = new SubscriptionMigrationPreviewOptions
                {
                    ProductHandle = targetPlanHandle,
                    PreservePeriod = true // keep the current period, prorated charge now (plan.md trap notes, Step 8)
                }
            }, ct: cancellationToken);

            var preview = response.Migration!;
            return new BillingPlanChangePreview
            {
                TargetPlanHandle = targetPlanHandle,
                Prorated = true,
                EffectiveDate = DateTimeOffset.UtcNow,
                ProratedAdjustmentInCents = ToNullableInt(preview.ProratedAdjustmentInCents)
            };
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            throw WrapError("preview plan change", ex,
                () => ex.Error.TryGetErrorListResponse1(out var errs) ? string.Join("; ", errs.Errors) : null,
                () => ex.Error.TryGetRawError(out var raw) ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}" : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<BillingSubscription> CommitPlanChangeNowAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(subscriptionId, new SubscriptionProductMigrationRequest
            {
                Migration = new SubscriptionProductMigration
                {
                    ProductHandle = targetPlanHandle,
                    PreservePeriod = true
                }
            }, ct: cancellationToken);

            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            throw WrapError("commit plan change", ex,
                () => ex.Error.TryGetErrorListResponse1(out var errs) ? string.Join("; ", errs.Errors) : null,
                () => ex.Error.TryGetRawError(out var raw) ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}" : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<BillingSubscription> SchedulePlanChangeAtRenewalAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.UpdateSubscription(subscriptionId, new UpdateSubscriptionRequest
            {
                Subscription = new UpdateSubscription
                {
                    ProductHandle = targetPlanHandle,
                    ProductChangeDelayed = true
                }
            }, ct: cancellationToken);

            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<UpdateSubscriptionError> ex)
        {
            throw WrapError("schedule plan change at renewal", ex,
                () => ex.Error.TryGetErrorListResponse1(out var errs) ? string.Join("; ", errs.Errors) : null,
                () => ex.Error.TryGetRawError(out var raw) ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}" : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.PauseSubscription(subscriptionId, null, ct: cancellationToken);
            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            throw WrapError("pause subscription", ex,
                () => ex.Error.TryGetErrorListResponse1(out var errs) ? string.Join("; ", errs.Errors) : null,
                () => ex.Error.TryGetRawError(out var raw) ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}" : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ResumeSubscription(subscriptionId, null, ct: cancellationToken);
            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            throw WrapError("resume subscription", ex,
                () => ex.Error.TryGetErrorListResponse1(out var errs) ? string.Join("; ", errs.Errors) : null,
                () => ex.Error.TryGetRawError(out var raw) ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}" : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, CancellationToken cancellationToken = default)
    {
        if (endOfPeriod)
        {
            try
            {
                await _client.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId, null, ct: cancellationToken);
            }
            catch (SdkException<InitiateDelayedCancellationError> ex)
            {
                throw WrapError("schedule end-of-period cancellation", ex,
                    () => ex.Error.TryGetNoContent(out var notFound) ? $"HTTP {(int)notFound.StatusCode}: subscription {subscriptionId} not found" : null,
                    () => ex.Error.TryGetErrorListResponse1(out var errs) ? string.Join("; ", errs.Errors) : null,
                    () => ex.Error.TryGetRawError(out var raw) ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}" : null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                throw new BillingProviderException("Maxio is unreachable.", ex);
            }

            // The delayed-cancellation response carries no subscription snapshot; re-read for the resulting state.
            return await GetSubscriptionAsync(subscriptionId, cancellationToken);
        }

        try
        {
            var response = await _client.SubscriptionStatus.CancelSubscription(subscriptionId, null, ct: cancellationToken);
            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            throw WrapError("cancel subscription", ex,
                () => ex.Error.TryGetNoContent(out var notFound) ? $"HTTP {(int)notFound.StatusCode}: subscription {subscriptionId} not found" : null,
                () => ex.Error.TryGetCancelSubscriptionErrorResponse(out var body) ? DescribeCancelError(body) : null,
                () => ex.Error.TryGetRawError(out var raw) ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}" : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    public async Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, null, ct: cancellationToken);
            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            throw WrapError("reactivate subscription", ex,
                () => ex.Error.TryGetErrorListResponse1(out var errs) ? string.Join("; ", errs.Errors) : null,
                () => ex.Error.TryGetRawError(out var raw) ? $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}" : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new BillingProviderException("Maxio is unreachable.", ex);
        }
    }

    private static BillingPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        PriceInCents = ToNullableInt(product.PriceInCents) ?? 0,
        Interval = product.Interval ?? 1,
        IntervalUnit = product.IntervalUnit?.Value ?? string.Empty,
        RequiresPaymentMethod = product.RequireCreditCard ?? false
    };

    private static BillingCustomer MapCustomer(Customer customer, string fallbackReference, string fallbackEmail) => new()
    {
        Id = customer.Id ?? 0,
        Reference = customer.Reference ?? fallbackReference,
        Email = customer.Email ?? fallbackEmail
    };

    private static BillingSubscription MapSubscription(Subscription subscription)
    {
        var product = subscription.Product;
        return new BillingSubscription
        {
            Id = subscription.Id ?? 0,
            // Fail closed: a missing customer link must never satisfy an ownership check (-1 never
            // matches a real, positive Maxio customer id).
            BillingCustomerId = subscription.Customer?.Id ?? -1,
            State = MapState(subscription.State),
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? string.Empty,
            PriceInCents = ToNullableInt(subscription.ProductPriceInCents) ?? ToNullableInt(product?.PriceInCents) ?? 0,
            NextBillingDate = subscription.CurrentPeriodEndsAt,
            CancelAtPeriodEnd = subscription.CancelAtEndOfPeriod ?? false,
            PendingPlanHandle = subscription.NextProductHandle
        };
    }

    private static BillingSubscriptionState MapState(SubscriptionState? state)
    {
        if (state == SubscriptionState.Active) return BillingSubscriptionState.Active;
        if (state == SubscriptionState.Trialing) return BillingSubscriptionState.Trialing;
        // Both Paused and OnHold map to the single provider-agnostic Paused state: which one
        // PauseSubscription actually produces is not documented by the SDK (plan.md, Blockers), so
        // both are honored rather than branching on an assumed value.
        if (state == SubscriptionState.Paused || state == SubscriptionState.OnHold) return BillingSubscriptionState.Paused;
        if (state == SubscriptionState.PastDue || state == SubscriptionState.SoftFailure ||
            state == SubscriptionState.Suspended || state == SubscriptionState.Unpaid) return BillingSubscriptionState.PastDue;
        if (state == SubscriptionState.Canceled) return BillingSubscriptionState.Cancelled;
        if (state == SubscriptionState.Expired) return BillingSubscriptionState.Expired;
        return BillingSubscriptionState.Unknown;
    }

    private static (string FirstName, string LastName) SplitDisplayName(string customerReference, string email)
    {
        var atIndex = email.IndexOf('@');
        var localPart = atIndex > 0 ? email[..atIndex] : email;
        var firstName = string.IsNullOrWhiteSpace(localPart) ? customerReference : localPart;
        return (firstName, "eShopOnWeb Customer");
    }

    private static string? DescribeCancelError(CancelSubscriptionErrorResponse body)
    {
        if (body.TryGetErrorListResponse1(out var list))
        {
            return string.Join("; ", list.Errors);
        }

        if (body.TryGetSingleErrorResponse1(out var single))
        {
            return single.Error;
        }

        return null;
    }

    private static int? ToNullableInt(long? value) => value.HasValue ? (int)value.Value : null;

    private static BillingProviderException Wrap(SdkException<RawError> ex, string action) =>
        new($"Maxio operation to {action} failed: HTTP {(int)ex.Error.StatusCode}: {ex.Error.ReadAsString()}", ex);

    private static BillingProviderException WrapError(string action, Exception source, params Func<string?>[] tryAccessors)
    {
        foreach (var tryAccessor in tryAccessors)
        {
            var message = tryAccessor();
            if (message is not null)
            {
                return new BillingProviderException($"Maxio operation to {action} failed: {message}", source);
            }
        }

        return new BillingProviderException($"Maxio operation to {action} failed with an unrecognized error shape.", source);
    }
}
