using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using MaxioCustomer = MaxioAdvancedBilling.Models.Customer;
using MaxioProduct = MaxioAdvancedBilling.Models.Product;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;
using MaxioSubscriptionState = MaxioAdvancedBilling.Models.Enums.SubscriptionState;
using MaxioUsage = MaxioAdvancedBilling.Models.Usage;
using MeteredComponent = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.MeteredComponent;
using Subscription = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription;
using SubscriptionState = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.SubscriptionState;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single place eShopOnWeb talks to Maxio Advanced Billing (plan.md §2.2). Everything above
/// <see cref="IBillingClient"/> sees only eShopOnWeb's own types, dollars, and
/// <see cref="BillingProviderException"/>; no Maxio type, unit, or exception escapes this class.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private const decimal CentsPerDollar = 100m;

    private readonly MaxioSettings _settings;
    private readonly MaxioCatalogCache _cache;
    private readonly IAppLogger<MaxioBillingClient> _logger;
    private readonly MaxioAdvancedBillingClient _maxio;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> settings,
        MaxioCatalogCache cache, IAppLogger<MaxioBillingClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxio = new MaxioAdvancedBillingClient(httpClient, BuildOptions(_settings));
    }

    /// <summary>
    /// Builds the SDK options. The outbound target is resolved here and only here: an explicit
    /// <c>Maxio:BaseUrl</c> is applied verbatim and wins, otherwise the SDK derives the host from the
    /// configured site subdomain and region (plan.md §2.3).
    /// </summary>
    public static MaxioAdvancedBillingClientOptions BuildOptions(MaxioSettings settings)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
            Environment = settings.IsEuRegion ? ServerEnvironment.Eu : ServerEnvironment.Us
        };

        var site = settings.Subdomain?.Trim() ?? string.Empty;

        if (settings.IsEuRegion)
        {
            options.Server.Production.Eu.Site = site;
            if (settings.HasExplicitBaseUrl)
            {
                options.Server.Production.Eu.BaseUrl = settings.ResolveBaseUrl();
            }
        }
        else
        {
            options.Server.Production.Us.Site = site;
            if (settings.HasExplicitBaseUrl)
            {
                options.Server.Production.Us.BaseUrl = settings.ResolveBaseUrl();
            }
        }

        return options;
    }

    // ---------------------------------------------------------------------------------------------
    // Catalog
    // ---------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ProductResponse> responses;
        try
        {
            responses = await _maxio.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 200,
                ct: cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw FromTyped("list plans", ex, ex.Error.TryGetRawError(out var raw) ? raw : null, null);
        }
        catch (Exception ex) when (ShouldTranslate(ex))
        {
            throw Translate("list plans", ex);
        }

        return responses
            .Select(response => MapPlan(response.Product))
            .Where(plan => !plan.Archived)
            .OrderBy(plan => plan.Price)
            .ToList();
    }

    public async Task<BillingPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        var plans = await ListPlansAsync(cancellationToken).ConfigureAwait(false);
        return plans.FirstOrDefault(plan =>
            string.Equals(plan.Handle, planHandle.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<MeteredComponent?> FindComponentByHandleAsync(string componentHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(componentHandle))
        {
            return null;
        }

        var components = await ListComponentsAsync(cancellationToken).ConfigureAwait(false);
        return components.FirstOrDefault(component =>
            string.Equals(component.Handle, componentHandle.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------------------------------------
    // Customers
    // ---------------------------------------------------------------------------------------------

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string customerReference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return null;
        }

        try
        {
            var response = await _maxio.Customers
                .ReadCustomerByReference(customerReference.Trim(), cancellationToken)
                .ConfigureAwait(false);

            return MapCustomer(response.Customer);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // An unknown reference is the normal "this user has never subscribed" case, not a failure.
            return null;
        }
        catch (Exception ex) when (ShouldTranslate(ex))
        {
            throw Translate("look up customer by reference", ex);
        }
    }

    public async Task<BillingCustomer> CreateCustomerAsync(BillingCustomerRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);

        try
        {
            var response = await _maxio.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = registration.FirstName,
                        LastName = registration.LastName,
                        Email = registration.Email,
                        Reference = registration.Reference
                    }
                },
                cancellationToken).ConfigureAwait(false);

            return MapCustomer(response.Customer);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            throw FromTyped("create customer", ex, ex.Error.TryGetRawError(out var raw) ? raw : null, null);
        }
        catch (Exception ex) when (ShouldTranslate(ex))
        {
            throw Translate("create customer", ex);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Subscriptions
    // ---------------------------------------------------------------------------------------------

    public async Task<Subscription> CreateSubscriptionAsync(BillingCustomer customer, string planHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentException.ThrowIfNullOrWhiteSpace(planHandle);

        try
        {
            var response = await _maxio.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = planHandle.Trim(),
                        CustomerId = customer.Id,
                        // Without this, a site whose default is automatic collection refuses an enrollment
                        // that has no payment method on file. Configuration decides (plan.md UC1).
                        PaymentCollectionMethod = ResolveCollectionMethod(_settings.PaymentCollectionMethod)
                    }
                },
                cancellationToken).ConfigureAwait(false);

            var subscription = response.Subscription
                ?? throw new BillingProviderException("Maxio accepted the enrollment but returned no subscription.");

            return MapSubscription(subscription, customer.Reference);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? Describe(errors) : null;
            throw FromTyped("create subscription", ex, ex.Error.TryGetRawError(out var raw) ? raw : null, detail);
        }
        catch (Exception ex) when (ShouldTranslate(ex))
        {
            throw Translate("create subscription", ex);
        }
    }

    public async Task<IReadOnlyList<Subscription>> ListSubscriptionsForCustomerAsync(int customerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var responses = await _maxio.Customers
                .ListCustomerSubscriptions(customerId, cancellationToken)
                .ConfigureAwait(false);

            return responses
                .Select(response => response.Subscription)
                .Where(subscription => subscription is not null)
                .Select(subscription => MapSubscription(subscription!, null))
                .ToList();
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<Subscription>();
        }
        catch (Exception ex) when (ShouldTranslate(ex))
        {
            throw Translate("list customer subscriptions", ex);
        }
    }

    public async Task<Subscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        if (subscriptionId <= 0)
        {
            return null;
        }

        try
        {
            var response = await _maxio.Subscriptions
                .ReadSubscription(subscriptionId, include: null, ct: cancellationToken)
                .ConfigureAwait(false);

            return response.Subscription is null ? null : MapSubscription(response.Subscription, null);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ShouldTranslate(ex))
        {
            throw Translate("read subscription", ex);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Usage
    // ---------------------------------------------------------------------------------------------

    public async Task<UsageRecord> RecordUsageAsync(int subscriptionId, string componentHandle, int quantity,
        string? memo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentHandle);

        var componentId = await ResolveComponentIdAsync(componentHandle, cancellationToken).ConfigureAwait(false);

        try
        {
            var response = await _maxio.SubscriptionComponents.CreateUsage(
                subscriptionIdOrReference: SubscriptionIdOrReference.Int(subscriptionId),
                componentId: ComponentIdModel.Int(componentId),
                body: new CreateUsageRequest
                {
                    Usage = new CreateUsage
                    {
                        Quantity = quantity,
                        Memo = memo
                    }
                },
                ct: cancellationToken).ConfigureAwait(false);

            return MapUsage(response.Usage, quantity);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? Describe(errors) : null;
            throw FromTyped("record usage", ex, ex.Error.TryGetRawError(out var raw) ? raw : null, detail);
        }
        catch (Exception ex) when (ShouldTranslate(ex))
        {
            throw Translate("record usage", ex);
        }
    }

    public async Task<int?> GetPeriodToDateUsageAsync(int subscriptionId, string componentHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentHandle);

        var componentId = await ResolveComponentIdAsync(componentHandle, cancellationToken).ConfigureAwait(false);

        try
        {
            var response = await _maxio.SubscriptionComponents
                .ReadSubscriptionComponent(subscriptionId, componentId, cancellationToken)
                .ConfigureAwait(false);

            // UnitBalance is the metered accumulation for the subscription's current billing period.
            return response.Component?.UnitBalance;
        }
        catch (SdkException<ReadSubscriptionComponentError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                // The component has never been used on this subscription — nothing has accrued yet.
                return null;
            }

            throw FromTyped("read period-to-date usage", ex, ex.Error.TryGetRawError(out var raw) ? raw : null, null);
        }
        catch (Exception ex) when (ShouldTranslate(ex))
        {
            throw Translate("read period-to-date usage", ex);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Plan change
    // ---------------------------------------------------------------------------------------------

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPlanHandle);

        var handle = targetPlanHandle.Trim();
        var subscription = await GetSubscriptionAsync(subscriptionId, cancellationToken).ConfigureAwait(false)
            ?? throw new BillingProviderException($"Maxio has no subscription with id {subscriptionId}.");

        var targetPlan = await FindPlanByHandleAsync(handle, cancellationToken).ConfigureAwait(false)
            ?? throw new BillingConfigurationException(
                $"Plan handle '{handle}' does not resolve in product family '{_settings.ProductFamilyHandle}'. " +
                "Re-seed the sandbox or correct the configuration (plan.md UC0).");

        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            // A deferred change prorates nothing: the customer keeps the current plan until the period ends
            // and pays the new plan's full price from the next period onwards.
            return new PlanChangePreview(
                subscriptionId: subscriptionId,
                currentPlanHandle: subscription.PlanHandle,
                targetPlanHandle: targetPlan.Handle,
                timing: timing,
                prorationCharge: 0m,
                prorationCredit: 0m,
                newPlanPrice: targetPlan.Price,
                effectiveAt: subscription.CurrentPeriodEnd ?? subscription.NextAssessmentAt);
        }

        try
        {
            var response = await _maxio.SubscriptionProducts.PreviewSubscriptionProductMigration(
                subscriptionId,
                new SubscriptionMigrationPreviewRequest
                {
                    Migration = new SubscriptionMigrationPreviewOptions
                    {
                        ProductHandle = handle,
                        IncludeTrial = false,
                        IncludeInitialCharge = false,
                        IncludeCoupons = true,
                        PreservePeriod = true
                    }
                },
                cancellationToken).ConfigureAwait(false);

            var migration = response.Migration;

            return new PlanChangePreview(
                subscriptionId: subscriptionId,
                currentPlanHandle: subscription.PlanHandle,
                targetPlanHandle: targetPlan.Handle,
                timing: timing,
                prorationCharge: FromCents(migration.ChargeInCents),
                prorationCredit: FromCents(migration.CreditAppliedInCents),
                newPlanPrice: targetPlan.Price,
                effectiveAt: DateTimeOffset.UtcNow);
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? Describe(errors) : null;
            throw FromTyped("preview plan change", ex, ex.Error.TryGetRawError(out var raw) ? raw : null, detail);
        }
        catch (Exception ex) when (ShouldTranslate(ex))
        {
            throw Translate("preview plan change", ex);
        }
    }

    public async Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPlanHandle);

        var handle = targetPlanHandle.Trim();

        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            try
            {
                var deferred = await _maxio.Subscriptions.UpdateSubscription(
                    subscriptionId,
                    new UpdateSubscriptionRequest
                    {
                        Subscription = new UpdateSubscription
                        {
                            ProductHandle = handle,
                            ProductChangeDelayed = true
                        }
                    },
                    cancellationToken).ConfigureAwait(false);

                return deferred.Subscription is null
                    ? await RequireSubscriptionAsync(subscriptionId, cancellationToken).ConfigureAwait(false)
                    : MapSubscription(deferred.Subscription, null);
            }
            catch (SdkException<UpdateSubscriptionError> ex)
            {
                var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? Describe(errors) : null;
                throw FromTyped("schedule plan change", ex, ex.Error.TryGetRawError(out var raw) ? raw : null, detail);
            }
            catch (Exception ex) when (ShouldTranslate(ex))
            {
                throw Translate("schedule plan change", ex);
            }
        }

        try
        {
            var migrated = await _maxio.SubscriptionProducts.MigrateSubscriptionProduct(
                subscriptionId,
                new SubscriptionProductMigrationRequest
                {
                    Migration = new SubscriptionProductMigration
                    {
                        ProductHandle = handle,
                        IncludeTrial = false,
                        IncludeInitialCharge = false,
                        IncludeCoupons = true,
                        PreservePeriod = true
                    }
                },
                cancellationToken).ConfigureAwait(false);

            return migrated.Subscription is null
                ? await RequireSubscriptionAsync(subscriptionId, cancellationToken).ConfigureAwait(false)
                : MapSubscription(migrated.Subscription, null);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? Describe(errors) : null;
            throw FromTyped("apply plan change", ex, ex.Error.TryGetRawError(out var raw) ? raw : null, detail);
        }
        catch (Exception ex) when (ShouldTranslate(ex))
        {
            throw Translate("apply plan change", ex);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------------------

    public Task<Subscription> ApplyLifecycleActionAsync(int subscriptionId, SubscriptionLifecycleAction action,
        string? reason, CancellationToken cancellationToken = default) => action switch
        {
            SubscriptionLifecycleAction.Pause => PauseAsync(subscriptionId, cancellationToken),
            SubscriptionLifecycleAction.Resume => ResumeAsync(subscriptionId, cancellationToken),
            SubscriptionLifecycleAction.CancelImmediately => CancelNowAsync(subscriptionId, reason, cancellationToken),
            SubscriptionLifecycleAction.CancelAtEndOfPeriod => CancelAtPeriodEndAsync(subscriptionId, reason, cancellationToken),
            SubscriptionLifecycleAction.Reactivate => ReactivateAsync(subscriptionId, cancellationToken),
            _ => throw new BillingProviderException($"Lifecycle action '{action}' is not supported.")
        };

    private async Task<Subscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        try
        {
            // A null body holds the subscription open-endedly; it stays held until it is resumed.
            var response = await _maxio.SubscriptionStatus
                .PauseSubscription(subscriptionId, body: null, ct: cancellationToken)
                .ConfigureAwait(false);

            return response.Subscription is null
                ? await RequireSubscriptionAsync(subscriptionId, cancellationToken).ConfigureAwait(false)
                : MapSubscription(response.Subscription, null);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? Describe(errors) : null;
            throw FromTyped("pause subscription", ex, ex.Error.TryGetRawError(out var raw) ? raw : null, detail);
        }
        catch (Exception ex) when (ShouldTranslate(ex))
        {
            throw Translate("pause subscription", ex);
        }
    }

    private async Task<Subscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _maxio.SubscriptionStatus
                .ResumeSubscription(subscriptionId, calendarBillingResumptionCharge: null, ct: cancellationToken)
                .ConfigureAwait(false);

            return response.Subscription is null
                ? await RequireSubscriptionAsync(subscriptionId, cancellationToken).ConfigureAwait(false)
                : MapSubscription(response.Subscription, null);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? Describe(errors) : null;
            throw FromTyped("resume subscription", ex, ex.Error.TryGetRawError(out var raw) ? raw : null, detail);
        }
        catch (Exception ex) when (ShouldTranslate(ex))
        {
            throw Translate("resume subscription", ex);
        }
    }

    private async Task<Subscription> CancelNowAsync(int subscriptionId, string? reason, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _maxio.SubscriptionStatus
                .CancelSubscription(subscriptionId, BuildCancellationRequest(reason), cancellationToken)
                .ConfigureAwait(false);

            return response.Subscription is null
                ? await RequireSubscriptionAsync(subscriptionId, cancellationToken).ConfigureAwait(false)
                : MapSubscription(response.Subscription, null);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            throw FromTyped("cancel subscription", ex, ex.Error.TryGetRawError(out var raw) ? raw : null,
                DescribeCancellationError(ex.Error));
        }
        catch (Exception ex) when (ShouldTranslate(ex))
        {
            throw Translate("cancel subscription", ex);
        }
    }

    private async Task<Subscription> CancelAtPeriodEndAsync(int subscriptionId, string? reason,
        CancellationToken cancellationToken)
    {
        try
        {
            // The delayed-cancellation endpoint returns only a confirmation message, so the caller's view of
            // the subscription is refreshed from the provider afterwards.
            await _maxio.SubscriptionStatus
                .InitiateDelayedCancellation(subscriptionId, BuildCancellationRequest(reason), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SdkException<InitiateDelayedCancellationError> ex)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? Describe(errors) : null;
            throw FromTyped("schedule cancellation", ex, ex.Error.TryGetRawError(out var raw) ? raw : null, detail);
        }
        catch (Exception ex) when (ShouldTranslate(ex))
        {
            throw Translate("schedule cancellation", ex);
        }

        return await RequireSubscriptionAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Subscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _maxio.SubscriptionStatus
                .ReactivateSubscription(subscriptionId, body: null, ct: cancellationToken)
                .ConfigureAwait(false);

            return response.Subscription is null
                ? await RequireSubscriptionAsync(subscriptionId, cancellationToken).ConfigureAwait(false)
                : MapSubscription(response.Subscription, null);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? Describe(errors) : null;
            throw FromTyped("reactivate subscription", ex, ex.Error.TryGetRawError(out var raw) ? raw : null, detail);
        }
        catch (Exception ex) when (ShouldTranslate(ex))
        {
            throw Translate("reactivate subscription", ex);
        }
    }

    /// <summary>
    /// Maps the configured collection method onto Maxio's vocabulary. An unrecognised value falls back to
    /// remittance — the card-free option — rather than silently demanding a payment method nobody captured.
    /// </summary>
    internal static CollectionMethod ResolveCollectionMethod(string? configured)
    {
        var value = configured?.Trim();

        if (string.Equals(value, CollectionMethod.Automatic.Value, StringComparison.OrdinalIgnoreCase))
        {
            return CollectionMethod.Automatic;
        }

        if (string.Equals(value, CollectionMethod.Prepaid.Value, StringComparison.OrdinalIgnoreCase))
        {
            return CollectionMethod.Prepaid;
        }

        if (string.Equals(value, CollectionMethod.Invoice.Value, StringComparison.OrdinalIgnoreCase))
        {
            return CollectionMethod.Invoice;
        }

        return CollectionMethod.Remittance;
    }

    private static CancellationRequest? BuildCancellationRequest(string? reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? null
            : new CancellationRequest
            {
                Subscription = new CancellationOptions { CancellationMessage = reason.Trim() }
            };

    // ---------------------------------------------------------------------------------------------
    // Catalog resolution
    // ---------------------------------------------------------------------------------------------

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_settings.ProductFamilyId is > 0)
        {
            return _settings.ProductFamilyId.Value;
        }

        var cacheKey = $"family:{_settings.ProductFamilyHandle}";
        if (_cache.TryGet<int>(cacheKey, out var cachedId))
        {
            return cachedId;
        }

        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new BillingConfigurationException(
                $"'{MaxioSettings.SectionName}:ProductFamilyHandle' is not configured.");
        }

        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _maxio.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldTranslate(ex))
        {
            throw Translate("list product families", ex);
        }

        var match = families
            .Select(response => response.ProductFamily)
            .FirstOrDefault(family => family is not null && string.Equals(
                family.Handle, _settings.ProductFamilyHandle.Trim(), StringComparison.OrdinalIgnoreCase));

        if (match?.Id is not > 0)
        {
            throw new BillingConfigurationException(
                $"Product family handle '{_settings.ProductFamilyHandle}' does not exist on Maxio site " +
                $"'{_settings.Subdomain}'. Seed the sandbox before using the subscription features (plan.md UC0).");
        }

        _cache.Set(cacheKey, match.Id.Value, _settings.CatalogCacheDuration);
        return match.Id.Value;
    }

    private async Task<IReadOnlyList<MeteredComponent>> ListComponentsAsync(CancellationToken cancellationToken)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ComponentResponse> responses;
        try
        {
            responses = await _maxio.Components.ListComponentsForProductFamily(
                productFamilyId: familyId,
                includeArchived: false,
                filter: null,
                dateField: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                page: 1,
                perPage: 200,
                ct: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldTranslate(ex))
        {
            throw Translate("list components", ex);
        }

        return responses.Select(response => MapComponent(response.Component)).ToList();
    }

    /// <summary>
    /// Resolves the numeric component id for a handle. The numeric id is used for usage calls because the
    /// handle form is not documented for the create-usage operation.
    /// </summary>
    private async Task<int> ResolveComponentIdAsync(string componentHandle, CancellationToken cancellationToken)
    {
        var handle = componentHandle.Trim();
        var cacheKey = $"component:{_settings.ProductFamilyHandle}:{handle}";

        if (_cache.TryGet<int>(cacheKey, out var cachedId))
        {
            return cachedId;
        }

        var component = await FindComponentByHandleAsync(handle, cancellationToken).ConfigureAwait(false)
            ?? throw new BillingConfigurationException(
                $"Component handle '{handle}' does not exist on product family '{_settings.ProductFamilyHandle}'. " +
                "Seed the sandbox before recording usage (plan.md UC0).");

        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"Component '{handle}' is of kind '{component.Kind}', not metered. A component cannot be " +
                "converted in place — archive it and recreate it as metered (plan.md UC0).");
        }

        _cache.Set(cacheKey, component.Id, _settings.CatalogCacheDuration);
        return component.Id;
    }

    private async Task<Subscription> RequireSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        return await GetSubscriptionAsync(subscriptionId, cancellationToken).ConfigureAwait(false)
            ?? throw new BillingProviderException(
                $"Maxio accepted the operation but subscription {subscriptionId} could no longer be read back.");
    }

    // ---------------------------------------------------------------------------------------------
    // Mapping — every money value crosses from Maxio's cents to eShopOnWeb's dollars here
    // ---------------------------------------------------------------------------------------------

    /// <summary>Maxio reports plan, product and proration money in cents; eShopOnWeb works in dollars.</summary>
    private static decimal FromCents(long? cents) => cents.GetValueOrDefault() / CentsPerDollar;

    private static BillingPlan MapPlan(MaxioProduct product) => new(
        id: product.Id.GetValueOrDefault(),
        handle: product.Handle ?? string.Empty,
        name: product.Name ?? string.Empty,
        description: product.Description,
        price: FromCents(product.PriceInCents),
        interval: product.Interval.GetValueOrDefault(1),
        intervalUnit: product.IntervalUnit?.Value ?? IntervalUnit.Month.Value,
        requiresPaymentMethod: product.RequireCreditCard.GetValueOrDefault(),
        archived: product.ArchivedAt.HasValue);

    private static MeteredComponent MapComponent(MaxioAdvancedBilling.Models.Component component) => new(
        id: component.Id.GetValueOrDefault(),
        handle: component.Handle ?? string.Empty,
        name: component.Name ?? string.Empty,
        kind: component.Kind?.Value ?? "unknown",
        isMetered: component.Kind == ComponentKind.MeteredComponent,
        unitPrice: ResolveUnitPrice(component),
        pricingScheme: component.PricingScheme?.Value);

    /// <summary>
    /// Maxio publishes a component's unit price as a decimal-dollars string and, separately, in cents.
    /// The string is authoritative; the cents field is the fallback.
    /// </summary>
    private static decimal? ResolveUnitPrice(MaxioAdvancedBilling.Models.Component component)
    {
        if (!string.IsNullOrWhiteSpace(component.UnitPrice) &&
            decimal.TryParse(component.UnitPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var dollars))
        {
            return dollars;
        }

        return component.PricePerUnitInCents.HasValue ? FromCents(component.PricePerUnitInCents) : null;
    }

    private static BillingCustomer MapCustomer(MaxioCustomer customer) => new(
        id: customer.Id.GetValueOrDefault(),
        reference: customer.Reference ?? string.Empty,
        email: customer.Email ?? string.Empty,
        firstName: customer.FirstName ?? string.Empty,
        lastName: customer.LastName ?? string.Empty);

    private static Subscription MapSubscription(MaxioSubscription subscription, string? referenceFallback)
    {
        var product = subscription.Product;

        return new Subscription(
            id: subscription.Id.GetValueOrDefault(),
            customerReference: subscription.Customer?.Reference ?? referenceFallback ?? string.Empty,
            customerId: subscription.Customer?.Id.GetValueOrDefault() ?? 0,
            planHandle: product?.Handle ?? string.Empty,
            planName: product?.Name ?? string.Empty,
            planPrice: FromCents(product?.PriceInCents ?? subscription.ProductPriceInCents),
            state: MapState(subscription.State),
            providerState: subscription.State?.Value ?? "unknown",
            currentPeriodStart: subscription.CurrentPeriodStartedAt,
            currentPeriodEnd: subscription.CurrentPeriodEndsAt,
            nextAssessmentAt: subscription.NextAssessmentAt,
            cancellationScheduledAt: subscription.DelayedCancelAt ?? subscription.ScheduledCancellationAt);
    }

    private static UsageRecord MapUsage(MaxioUsage usage, int requestedQuantity) => new(
        id: usage.Id.GetValueOrDefault(),
        quantity: ReadQuantity(usage) ?? requestedQuantity,
        memo: usage.Memo,
        recordedAt: usage.CreatedAt);

    /// <summary>Usage quantity comes back as an int-or-string union; both forms are read.</summary>
    private static int? ReadQuantity(MaxioUsage usage)
    {
        if (usage.Quantity is null)
        {
            return null;
        }

        if (usage.Quantity.TryGetInt(out var quantity))
        {
            return quantity;
        }

        if (usage.Quantity.TryGetString(out var text) &&
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return (int)parsed;
        }

        return null;
    }

    private static readonly IReadOnlyDictionary<string, SubscriptionState> StateMap =
        new Dictionary<string, SubscriptionState>(StringComparer.OrdinalIgnoreCase)
        {
            [MaxioSubscriptionState.Pending.Value] = SubscriptionState.Pending,
            [MaxioSubscriptionState.AwaitingSignup.Value] = SubscriptionState.Pending,
            [MaxioSubscriptionState.FailedToCreate.Value] = SubscriptionState.Failed,
            [MaxioSubscriptionState.Trialing.Value] = SubscriptionState.Trialing,
            [MaxioSubscriptionState.Assessing.Value] = SubscriptionState.Active,
            [MaxioSubscriptionState.Active.Value] = SubscriptionState.Active,
            [MaxioSubscriptionState.SoftFailure.Value] = SubscriptionState.PastDue,
            [MaxioSubscriptionState.PastDue.Value] = SubscriptionState.PastDue,
            [MaxioSubscriptionState.TrialEnded.Value] = SubscriptionState.PastDue,
            [MaxioSubscriptionState.Unpaid.Value] = SubscriptionState.Suspended,
            [MaxioSubscriptionState.Suspended.Value] = SubscriptionState.Suspended,
            [MaxioSubscriptionState.OnHold.Value] = SubscriptionState.Paused,
            [MaxioSubscriptionState.Paused.Value] = SubscriptionState.Paused,
            [MaxioSubscriptionState.Canceled.Value] = SubscriptionState.Cancelled,
            [MaxioSubscriptionState.Expired.Value] = SubscriptionState.Expired
        };

    /// <summary>
    /// Maps Maxio's state vocabulary onto eShopOnWeb's. An unrecognised value becomes
    /// <see cref="SubscriptionState.Unknown"/>, which permits no lifecycle action — an unmapped provider
    /// state can never be mistaken for an actionable one.
    /// </summary>
    private static SubscriptionState MapState(MaxioSubscriptionState? state)
    {
        var value = state?.Value;

        return value is not null && StateMap.TryGetValue(value, out var mapped)
            ? mapped
            : SubscriptionState.Unknown;
    }

    // ---------------------------------------------------------------------------------------------
    // Error translation — no provider or transport exception type escapes this class
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// True for the exceptions this class converts. Cancellation and the exceptions it has already
    /// translated pass straight through.
    /// </summary>
    private static bool ShouldTranslate(Exception exception) =>
        exception is not OperationCanceledException &&
        exception is not BillingProviderException &&
        exception is not BillingConfigurationException;

    private BillingProviderException Translate(string operation, Exception exception)
    {
        if (exception is SdkException<RawError> sdkException)
        {
            return FromRaw(operation, sdkException.Error, sdkException);
        }

        // Provider text is passed as an argument, never inlined into the template, so braces in a provider
        // message can never be mistaken for log-template placeholders.
        _logger.LogWarning("Maxio {0} failed: {1}: {2}", operation, exception.GetType().Name, exception.Message);
        return new BillingProviderException($"Maxio could not {operation}: {exception.Message}", null, null, exception);
    }

    private BillingProviderException FromTyped(string operation, Exception exception, RawError? raw, string? detail)
    {
        if (raw is not null && detail is null)
        {
            return FromRaw(operation, raw, exception);
        }

        var status = raw is null ? (int?)null : (int)raw.StatusCode;
        _logger.LogWarning("Maxio {0} was rejected (status {1}): {2}",
            operation, status?.ToString(CultureInfo.InvariantCulture) ?? "unknown", detail ?? "no detail");

        return new BillingProviderException(
            detail is null ? $"Maxio rejected the request to {operation}." : $"Maxio could not {operation}: {detail}",
            status,
            detail,
            exception);
    }

    private BillingProviderException FromRaw(string operation, RawError error, Exception exception)
    {
        var status = (int)error.StatusCode;
        var body = ReadBody(error);

        _logger.LogWarning("Maxio {0} failed with HTTP {1}.", operation, status.ToString(CultureInfo.InvariantCulture));

        return new BillingProviderException(
            $"Maxio could not {operation} (HTTP {status.ToString(CultureInfo.InvariantCulture)}).",
            status,
            body,
            exception);
    }

    private static string? ReadBody(RawError error)
    {
        try
        {
            var body = error.ReadAsString();
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            return body.Length <= 512 ? body : body.Substring(0, 512);
        }
        catch (Exception)
        {
            // A body that cannot be read must never mask the status code we already have.
            return null;
        }
    }

    private static string? Describe(ErrorListResponse1? errors)
    {
        if (errors?.Errors is null || errors.Errors.Count == 0)
        {
            return null;
        }

        return string.Join("; ", errors.Errors);
    }

    private static string? DescribeCancellationError(CancelSubscriptionApiError error)
    {
        if (error.TryGetCancelSubscriptionErrorResponse(out var response))
        {
            if (response.TryGetErrorListResponse1(out var list))
            {
                return Describe(list);
            }

            if (response.TryGetSingleErrorResponse1(out var single))
            {
                return single.Error;
            }
        }

        return null;
    }
}
