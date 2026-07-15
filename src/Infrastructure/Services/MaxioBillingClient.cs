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
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single, concrete integration point with Maxio Advanced Billing (plan.md §2.2). Implements the
/// provider-agnostic <see cref="IBillingClient"/> seam; nothing outside this class ever touches the Maxio
/// SDK. Every Maxio call and model shape here is taken from the maxio-sdk-exp1-agents plugin's SDK map —
/// no Maxio-side detail is invented.
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

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _settings.ProductFamilyId.ToString(CultureInfo.InvariantCulture),
                dateField: null, filter: null, startDate: null, endDate: null,
                startDatetime: null, endDatetime: null, includeArchived: null, include: null,
                page: 1, perPage: 50, ct: cancellationToken);

            return products
                .Select(pr => pr.Product)
                .Where(p => p is not null && p.ArchivedAt is null)
                .Select(p => ToBillingPlan(p!))
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
            {
                throw new BillingConfigurationException(
                    $"Product family '{_settings.ProductFamilyId}' was not found on the billing provider: {notFound}. Re-run UC0 and update configuration.");
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Could not list plans from the billing provider: {raw.ReadAsString()}", ex);
            }

            throw new BillingProviderException("Could not list plans from the billing provider.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("The billing provider is unreachable. Please try again shortly.", ex);
        }
    }

    public async Task<BillingPlan?> FindPlanAsync(string productHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Products.ReadProductByHandle(apiHandle: productHandle, ct: cancellationToken);
            return response.Product is null ? null : ToBillingPlan(response.Product);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Could not resolve plan '{productHandle}': {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("The billing provider is unreachable. Please try again shortly.", ex);
        }
    }

    public async Task EnsureMeteredComponentConfiguredAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Components.FindComponent(handle: _settings.MeteredComponentHandle, ct: cancellationToken);
            if (response.Component is null || response.Component.Kind != ComponentKind.MeteredComponent)
            {
                throw new BillingConfigurationException(
                    $"Component '{_settings.MeteredComponentHandle}' is not configured as a metered component on the billing provider. Re-run UC0 and update configuration.");
            }
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingConfigurationException(
                $"Component '{_settings.MeteredComponentHandle}' was not found on the billing provider. Re-run UC0 and update configuration.");
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Could not validate the metered component: {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("The billing provider is unreachable. Please try again shortly.", ex);
        }
    }

    public async Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: reference, ct: cancellationToken);
            return response.Customer?.Id is null ? null : new BillingCustomer(response.Customer.Id.Value, reference);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Could not look up the billing customer: {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("The billing provider is unreachable. Please try again shortly.", ex);
        }
    }

    public async Task<BillingCustomer> FindOrCreateCustomerAsync(string reference, string email, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var atIndex = email.IndexOf('@');
            var localPart = atIndex > 0 ? email[..atIndex] : email;

            var response = await _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = localPart,
                        LastName = "Customer",
                        Email = email,
                        Reference = reference
                    }
                }, ct: cancellationToken);

            if (response.Customer?.Id is null)
            {
                throw new BillingProviderException("The billing provider did not return a customer id.");
            }

            return new BillingCustomer(response.Customer.Id.Value, reference);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // The billing provider only allows one customer per reference — a retried create for a
            // reference that now exists is rejected. Re-run the lookup rather than treating that as fatal.
            var afterConflict = await FindCustomerAsync(reference, cancellationToken);
            if (afterConflict is not null)
            {
                return afterConflict;
            }

            throw DescribeCreateCustomerError(ex.Error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("The billing provider is unreachable. Please try again shortly.", ex);
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: cancellationToken);
            return subscriptions
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => ToBillingSubscription(s!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Could not list subscriptions for customer {customerId}: {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("The billing provider is unreachable. Please try again shortly.", ex);
        }
    }

    public async Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId: subscriptionId, include: null, ct: cancellationToken);
            if (response.Subscription is null)
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }

            return ToBillingSubscription(response.Subscription);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new SubscriptionNotFoundException(subscriptionId);
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Could not read subscription {subscriptionId}: {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("The billing provider is unreachable. Please try again shortly.", ex);
        }
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            // The site's own default payment collection method (Site.DefaultPaymentCollectionMethod,
            // read via Sites.ReadSite) applies whenever CreateSubscription.PaymentCollectionMethod is left
            // unset — independently of the product's RequireCreditCard flag. For this demo's products
            // (requires payment method off, no card on file), that default resolves to automatic card
            // collection and the create fails with "No payment method was on file". Request non-automatic
            // collection explicitly so a balance can be created without a stored payment method.
            var collectionMethod = await ResolveNonAutomaticCollectionMethodAsync(cancellationToken);

            var response = await _client.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customerId,
                        PaymentCollectionMethod = collectionMethod
                    }
                }, ct: cancellationToken);

            return RequireSubscription(response, customerId);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw DescribeErrorListError(
                ex.Error.TryGetErrorListResponse1(out var typed) ? typed.Errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Could not resolve the site's payment collection settings: {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("The billing provider is unreachable. Please try again shortly.", ex);
        }
    }

    private CollectionMethod? _cachedNonAutomaticCollectionMethod;

    /// <summary>
    /// Resolves the non-automatic <see cref="CollectionMethod"/> valid for this site's billing architecture
    /// (map: <c>CollectionMethod</c> — legacy Statements Architecture accepts <c>invoice</c>/<c>automatic</c>;
    /// current Relationship Invoicing Architecture accepts <c>remittance</c>/<c>automatic</c>/<c>prepaid</c>),
    /// so subscriptions can be created without an automatic card-charge attempt. Cached after first read
    /// since a site's architecture does not change at runtime.
    /// </summary>
    private async Task<CollectionMethod> ResolveNonAutomaticCollectionMethodAsync(CancellationToken cancellationToken)
    {
        if (_cachedNonAutomaticCollectionMethod is { } cached)
        {
            return cached;
        }

        var response = await _client.Sites.ReadSite(ct: cancellationToken);
        var resolved = response.Site?.RelationshipInvoicingEnabled == true
            ? CollectionMethod.Remittance
            : CollectionMethod.Invoice;

        _cachedNonAutomaticCollectionMethod = resolved;
        return resolved;
    }

    public async Task<BillingUsageReading> RecordUsageAsync(int subscriptionId, double quantity, string? memo, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.SubscriptionComponents.CreateUsage(
                subscriptionIdOrReference: SubscriptionIdOrReference.Int(subscriptionId),
                componentId: ComponentIdModel.Int(_settings.MeteredComponentId),
                body: new CreateUsageRequest { Usage = new CreateUsage { Quantity = quantity, Memo = memo } },
                ct: cancellationToken);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            throw DescribeErrorListError(
                ex.Error.TryGetErrorListResponse1(out var typed) ? typed.Errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("The billing provider is unreachable. Please try again shortly.", ex);
        }

        var periodToDate = await TryGetComponentUnitBalanceAsync(subscriptionId, cancellationToken);
        return new BillingUsageReading(Recorded: true, PeriodToDateUnits: periodToDate, PeriodToDateAvailable: periodToDate is not null);
    }

    public async Task<BillingPlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken cancellationToken = default)
    {
        var subscription = await GetSubscriptionAsync(subscriptionId, cancellationToken);
        var stalenessToken = BillingStalenessToken.From(subscription);

        if (!applyImmediately)
        {
            // The billing provider exposes no distinct preview for a delayed (next-renewal, unprorated)
            // product change — UpdateSubscription performs it directly, with no proration. The preview
            // shown to the customer is computed here: nothing is charged until the next renewal, when the
            // full new-plan price takes effect.
            var targetPlan = await FindPlanAsync(targetProductHandle, cancellationToken)
                ?? throw new BillingConfigurationException($"Plan '{targetProductHandle}' is not configured on the billing provider.");

            return new BillingPlanChangePreview(
                CurrentProductHandle: subscription.ProductHandle,
                TargetProductHandle: targetProductHandle,
                ApplyImmediately: false,
                ProratedAdjustmentInCents: 0,
                ChargeInCents: targetPlan.PriceInCents,
                PaymentDueInCents: 0,
                CreditAppliedInCents: 0,
                StalenessToken: stalenessToken);
        }

        try
        {
            var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(
                subscriptionId,
                body: new SubscriptionMigrationPreviewRequest
                {
                    Migration = new SubscriptionMigrationPreviewOptions { ProductHandle = targetProductHandle }
                }, ct: cancellationToken);

            var migration = response.Migration;
            return new BillingPlanChangePreview(
                CurrentProductHandle: subscription.ProductHandle,
                TargetProductHandle: targetProductHandle,
                ApplyImmediately: true,
                ProratedAdjustmentInCents: migration?.ProratedAdjustmentInCents,
                ChargeInCents: migration?.ChargeInCents,
                PaymentDueInCents: migration?.PaymentDueInCents,
                CreditAppliedInCents: migration?.CreditAppliedInCents,
                StalenessToken: stalenessToken);
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            throw DescribeErrorListError(
                ex.Error.TryGetErrorListResponse1(out var typed) ? typed.Errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("The billing provider is unreachable. Please try again shortly.", ex);
        }
    }

    public async Task<BillingSubscription> CommitPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken cancellationToken = default)
    {
        if (!applyImmediately)
        {
            try
            {
                var response = await _client.Subscriptions.UpdateSubscription(
                    subscriptionId,
                    body: new UpdateSubscriptionRequest
                    {
                        Subscription = new UpdateSubscription
                        {
                            ProductHandle = targetProductHandle,
                            ProductChangeDelayed = true
                        }
                    }, ct: cancellationToken);

                return RequireSubscription(response, subscriptionId);
            }
            catch (SdkException<UpdateSubscriptionError> ex)
            {
                throw DescribeErrorListError(
                    ex.Error.TryGetErrorListResponse1(out var typed) ? typed.Errors : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new BillingProviderException("The billing provider is unreachable. Please try again shortly.", ex);
            }
        }

        try
        {
            var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(
                subscriptionId,
                body: new SubscriptionProductMigrationRequest
                {
                    Migration = new SubscriptionProductMigration { ProductHandle = targetProductHandle }
                }, ct: cancellationToken);

            return RequireSubscription(response, subscriptionId);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            throw DescribeErrorListError(
                ex.Error.TryGetErrorListResponse1(out var typed) ? typed.Errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("The billing provider is unreachable. Please try again shortly.", ex);
        }
    }

    public async Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.PauseSubscription(subscriptionId, body: null, ct: cancellationToken);
            return RequireSubscription(response, subscriptionId);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            throw DescribeErrorListError(
                ex.Error.TryGetErrorListResponse1(out var typed) ? typed.Errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("The billing provider is unreachable. Please try again shortly.", ex);
        }
    }

    public async Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ResumeSubscription(subscriptionId, calendarBillingResumptionCharge: null, ct: cancellationToken);
            return RequireSubscription(response, subscriptionId);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            throw DescribeErrorListError(
                ex.Error.TryGetErrorListResponse1(out var typed) ? typed.Errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("The billing provider is unreachable. Please try again shortly.", ex);
        }
    }

    public async Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default)
    {
        try
        {
            if (endOfPeriod)
            {
                await _client.SubscriptionStatus.InitiateDelayedCancellation(
                    subscriptionId,
                    body: new CancellationRequest
                    {
                        Subscription = new CancellationOptions { CancellationMessage = reason, CancelAtEndOfPeriod = true }
                    }, ct: cancellationToken);

                return await GetSubscriptionAsync(subscriptionId, cancellationToken);
            }

            var response = await _client.SubscriptionStatus.CancelSubscription(
                subscriptionId,
                body: new CancellationRequest
                {
                    Subscription = new CancellationOptions { CancellationMessage = reason }
                }, ct: cancellationToken);

            return RequireSubscription(response, subscriptionId);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            throw DescribeCancelError(ex.Error);
        }
        catch (SdkException<InitiateDelayedCancellationError> ex)
        {
            throw DescribeErrorListError(
                ex.Error.TryGetErrorListResponse1(out var typed) ? typed.Errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("The billing provider is unreachable. Please try again shortly.", ex);
        }
    }

    public async Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, body: null, ct: cancellationToken);
            return RequireSubscription(response, subscriptionId);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            throw DescribeErrorListError(
                ex.Error.TryGetErrorListResponse1(out var typed) ? typed.Errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("The billing provider is unreachable. Please try again shortly.", ex);
        }
    }

    private async Task<int?> TryGetComponentUnitBalanceAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.SubscriptionComponents.ReadSubscriptionComponent(
                subscriptionId: subscriptionId, componentId: _settings.MeteredComponentId, ct: cancellationToken);
            return response.Component?.UnitBalance;
        }
        catch (Exception ex) when (ex is SdkException<ReadSubscriptionComponentError> or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Could not read back the period-to-date usage total for subscription {0}: {1}", subscriptionId, ex.Message);
            return null;
        }
    }

    private static BillingSubscription RequireSubscription(SubscriptionResponse response, int contextId)
    {
        if (response.Subscription is null)
        {
            throw new BillingProviderException($"The billing provider did not return the expected subscription (context id {contextId}).");
        }

        return ToBillingSubscription(response.Subscription);
    }

    private static BillingProviderException DescribeCreateCustomerError(CreateCustomerError error)
    {
        if (error.TryGetCustomerErrorResponse1(out var typed))
        {
            var messages = new List<string>();
            if (typed.Errors?.PerPage is { } perPage) messages.AddRange(perPage);
            if (typed.Errors?.PricePoint is { } pricePoint) messages.AddRange(pricePoint);
            if (messages.Count > 0)
            {
                return new BillingProviderException(string.Join(" ", messages));
            }
        }

        if (error.TryGetRawError(out var raw))
        {
            return new BillingProviderException($"The billing provider rejected the customer: {raw.ReadAsString()}");
        }

        return new BillingProviderException("The billing provider rejected the customer.");
    }

    private static BillingProviderException DescribeCancelError(CancelSubscriptionApiError error)
    {
        if (error.TryGetNoContent(out var noContent))
        {
            return new BillingProviderException($"Subscription was not found: {noContent.ReadAsString()}");
        }

        if (error.TryGetCancelSubscriptionErrorResponse(out var typed))
        {
            if (typed.TryGetErrorListResponse1(out var list) && list.Errors.Count > 0)
            {
                return new BillingProviderException(string.Join(" ", list.Errors));
            }

            return new BillingProviderException("The billing provider rejected the cancellation.");
        }

        if (error.TryGetRawError(out var raw))
        {
            return new BillingProviderException($"The billing provider rejected the cancellation: {raw.ReadAsString()}");
        }

        return new BillingProviderException("The billing provider rejected the cancellation.");
    }

    private static BillingProviderException DescribeErrorListError(IReadOnlyList<string>? errors, RawError? rawError)
    {
        if (errors is { Count: > 0 })
        {
            return new BillingProviderException(string.Join(" ", errors));
        }

        if (rawError is not null)
        {
            return new BillingProviderException($"The billing provider rejected the request: {rawError.ReadAsString()}");
        }

        return new BillingProviderException("The billing provider rejected the request.");
    }

    private static BillingPlan ToBillingPlan(Product product) => new(
        Handle: product.Handle ?? string.Empty,
        ProductId: product.Id ?? throw new BillingProviderException("The billing provider returned a product with no id."),
        Name: product.Name ?? string.Empty,
        PriceInCents: product.PriceInCents,
        IntervalCount: product.Interval,
        IntervalUnit: product.IntervalUnit?.Value,
        RequiresPaymentMethod: product.RequireCreditCard ?? false);

    // Maxio ids are always positive — a missing Id/Customer/Product on a subscription payload is a
    // provider-contract violation, not a legitimate "0". Failing loudly here (rather than defaulting to 0)
    // keeps a malformed response from silently defeating the ownership check (GetOwnedSubscriptionAsync)
    // or the plan-change staleness token (BillingStalenessToken), both of which compare these ids.
    private static BillingSubscription ToBillingSubscription(MaxioAdvancedBilling.Models.Subscription subscription) => new(
        Id: subscription.Id ?? throw new BillingProviderException("The billing provider returned a subscription with no id."),
        CustomerId: subscription.Customer?.Id ?? throw new BillingProviderException("The billing provider returned a subscription with no customer."),
        ProductHandle: subscription.Product?.Handle ?? throw new BillingProviderException("The billing provider returned a subscription with no product."),
        ProductId: subscription.Product?.Id ?? throw new BillingProviderException("The billing provider returned a subscription with no product."),
        State: subscription.State?.Value ?? "unknown",
        CancelAtEndOfPeriod: subscription.CancelAtEndOfPeriod ?? false,
        CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
        ProductVersionNumber: subscription.ProductVersionNumber);
}
