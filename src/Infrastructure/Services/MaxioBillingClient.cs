using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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
using DomainStatus = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.SubscriptionStatus;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The one and only place eShopOnWeb talks to Maxio Advanced Billing. Everything the provider returns is
/// normalized into ApplicationCore types, and every provider failure is translated into a typed
/// <see cref="BillingProviderException"/> so no SDK type escapes this class.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private const int MAX_PAGE_SIZE = 200;
    private const int MAX_USAGE_PAGES = 50;
    private const string BASIC_AUTH_PASSWORD = "x";

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    private int? _productFamilyId;

    public MaxioBillingClient(HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingClient> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _client = new MaxioAdvancedBillingClient(httpClient, CreateClientOptions(_settings));
    }

    /// <summary>
    /// Builds the SDK options. The target server is resolved from configuration: an explicit
    /// <c>Maxio:BaseUrl</c> is used verbatim, otherwise the host is derived from the site subdomain. Only
    /// the options of the selected region are read, so the override is applied to that region.
    /// </summary>
    /// <remarks>
    /// Public so the one-off provisioning tool constructs its client exactly the way the application does,
    /// rather than duplicating the target-server rules.
    /// </remarks>
    public static MaxioAdvancedBillingClientOptions CreateClientOptions(MaxioSettings settings)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = settings.IsEuropeanRegion ? ServerEnvironment.Eu : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = BASIC_AUTH_PASSWORD
            }
        };

        var target = settings.ResolveBaseUrl();

        if (settings.IsEuropeanRegion)
        {
            if (settings.HasExplicitBaseUrl)
            {
                options.Server.Production.Eu.BaseUrl = target;
            }
            else
            {
                options.Server.Production.Eu.Site = settings.Subdomain.Trim();
            }
        }
        else
        {
            if (settings.HasExplicitBaseUrl)
            {
                options.Server.Production.Us.BaseUrl = target;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain.Trim();
            }
        }

        return options;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        try
        {
            var responses = await _client.ProductFamilies.ListProductsForProductFamily(
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
                perPage: MAX_PAGE_SIZE,
                ct: cancellationToken);

            return responses.Select(response => MapPlan(response.Product))
                .Where(plan => plan is not null)
                .Select(plan => plan!)
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw Translate("ListProductsForProductFamily", ex,
                error => error.TryGetString(out var message) ? message : null);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("ListProductsForProductFamily", ex);
        }
    }

    public async Task<SubscriptionPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        try
        {
            var response = await _client.Products.ReadProductByHandle(planHandle, ct: cancellationToken);

            return MapPlan(response.Product);
        }
        catch (SdkException<RawError> ex) when (StatusOf(ex) == 404)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw("ReadProductByHandle", ex);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("ReadProductByHandle", ex);
        }
    }

    public async Task<MeteredComponentDefinition?> GetMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        var handle = _settings.MeteredComponentHandle;

        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingConfigurationException(
                $"'{MaxioSettings.CONFIG_SECTION}:MeteredComponentHandle' is not configured, so usage cannot be reported.");
        }

        Component component;

        try
        {
            var response = await _client.Components.FindComponent(handle, ct: cancellationToken);
            component = response.Component;
        }
        catch (SdkException<RawError> ex) when (StatusOf(ex) == 404)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw("FindComponent", ex);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("FindComponent", ex);
        }

        // A component of the right handle on the wrong family is not available to our subscriptions, and
        // silently using it would bill against the wrong catalog.
        if (!string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle) &&
            !string.IsNullOrWhiteSpace(component.ProductFamilyHandle) &&
            !string.Equals(component.ProductFamilyHandle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingConfigurationException(
                $"Metered component '{handle}' belongs to product family '{component.ProductFamilyHandle}', not the configured '{_settings.ProductFamilyHandle}'. Recreate it on the correct family.");
        }

        return MapComponent(component);
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: cancellationToken);

            return MapCustomer(response.Customer);
        }
        catch (SdkException<RawError> ex) when (StatusOf(ex) == 404)
        {
            return null;
        }
        catch (SdkException<RawError> ex) when (StatusOf(ex) is 400 or 422)
        {
            // The lookup endpoint reports an unmatched reference inconsistently; log the body once and
            // treat it as "no such customer" rather than failing an otherwise valid subscribe.
            _logger.LogWarning("Customer lookup by reference returned {Status}; treating as not found. Provider said: {Body}",
                StatusOf(ex), SafeRawMessage(ex.Error));
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw("ReadCustomerByReference", ex);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("ReadCustomerByReference", ex);
        }
    }

    public async Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = customer.FirstName,
                        LastName = customer.LastName,
                        Email = customer.Email,
                        Reference = customer.Reference
                    }
                },
                ct: cancellationToken);

            return MapCustomer(response.Customer);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            throw Translate("CreateCustomer", ex,
                error => error.TryGetCustomerErrorResponse1(out var validation) ? DescribeCustomerErrors(validation) : null);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("CreateCustomer", ex);
        }
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        CustomerId = customerId,
                        ProductHandle = planHandle,

                        // eShopOnWeb never captures card details, so automatic collection would fail the
                        // first invoice for want of a payment method. Enrolling on remittance terms bills
                        // the customer by invoice instead, which is what this integration supports.
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                },
                ct: cancellationToken);

            return RequireSubscription("CreateSubscription", response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw Translate("CreateSubscription", ex,
                error => error.TryGetErrorListResponse1(out var validation) ? DescribeErrorList(validation) : null);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("CreateSubscription", ex);
        }
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(customerId, ct: cancellationToken);

            return responses.Select(response => MapSubscription(response.Subscription))
                .Where(subscription => subscription is not null)
                .Select(subscription => subscription!)
                .ToList();
        }
        catch (SdkException<RawError> ex) when (StatusOf(ex) == 404)
        {
            return Array.Empty<CustomerSubscription>();
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw("ListCustomerSubscriptions", ex);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("ListCustomerSubscriptions", ex);
        }
    }

    public async Task<CustomerSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: cancellationToken);

            return MapSubscription(response.Subscription);
        }
        catch (SdkException<RawError> ex) when (StatusOf(ex) == 404)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw("ReadSubscription", ex);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("ReadSubscription", ex);
        }
    }

    public async Task<UsageRecord> RecordUsageAsync(int subscriptionId,
        int componentId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionComponents.CreateUsage(
                SubscriptionIdOrReference.Int(subscriptionId),
                ComponentIdModel.Int(componentId),
                body: new CreateUsageRequest
                {
                    Usage = new CreateUsage
                    {
                        Quantity = (double)quantity,
                        Memo = memo
                    }
                },
                ct: cancellationToken);

            var usage = response.Usage;

            return new UsageRecord(usage.Id ?? 0,
                usage.SubscriptionId ?? subscriptionId,
                usage.ComponentId ?? componentId,
                ReadQuantity(usage.Quantity) ?? quantity,
                usage.Memo,
                usage.CreatedAt);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            throw Translate("CreateUsage", ex,
                error => error.TryGetErrorListResponse1(out var validation) ? DescribeErrorList(validation) : null);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("CreateUsage", ex);
        }
    }

    public async Task<int?> GetComponentUnitBalanceAsync(int subscriptionId, int componentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionComponents.ReadSubscriptionComponent(subscriptionId, componentId, ct: cancellationToken);

            return response.Component?.UnitBalance;
        }
        catch (SdkException<ReadSubscriptionComponentError> ex)
        {
            throw Translate("ReadSubscriptionComponent", ex, _ => null);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("ReadSubscriptionComponent", ex);
        }
    }

    public async Task<decimal> SumUsageSinceAsync(int subscriptionId,
        int componentId,
        DateTimeOffset? since,
        CancellationToken cancellationToken = default)
    {
        var total = 0m;

        try
        {
            for (var page = 1; page <= MAX_USAGE_PAGES; page++)
            {
                var responses = await _client.SubscriptionComponents.ListUsages(
                    SubscriptionIdOrReference.Int(subscriptionId),
                    ComponentIdModel.Int(componentId),
                    sinceId: null,
                    maxId: null,
                    sinceDate: since,
                    untilDate: null,
                    page: page,
                    perPage: MAX_PAGE_SIZE,
                    ct: cancellationToken);

                foreach (var response in responses)
                {
                    total += ReadQuantity(response.Usage.Quantity) ?? 0m;
                }

                if (responses.Count < MAX_PAGE_SIZE)
                {
                    return total;
                }
            }

            _logger.LogWarning("Usage history for subscription {SubscriptionId} exceeded {MaxPages} pages; the period-to-date total is a partial sum.",
                subscriptionId, MAX_USAGE_PAGES);

            return total;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw("ListUsages", ex);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("ListUsages", ex);
        }
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default)
    {
        var current = await GetSubscriptionAsync(subscriptionId, cancellationToken);
        var targetPlan = await FindPlanByHandleAsync(targetPlanHandle, cancellationToken);

        if (targetPlan is null)
        {
            throw new BillingConfigurationException(
                $"Target plan '{targetPlanHandle}' does not resolve against the billing provider.");
        }

        SubscriptionMigrationPreview preview;

        try
        {
            var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(
                subscriptionId,
                body: new SubscriptionMigrationPreviewRequest
                {
                    Migration = new SubscriptionMigrationPreviewOptions
                    {
                        ProductHandle = targetPlanHandle
                    }
                },
                ct: cancellationToken);

            preview = response.Migration;
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            throw Translate("PreviewSubscriptionProductMigration", ex,
                error => error.TryGetErrorListResponse1(out var validation) ? DescribeErrorList(validation) : null);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("PreviewSubscriptionProductMigration", ex);
        }

        return new PlanChangePreview(subscriptionId,
            current?.PlanHandle,
            current?.PlanName,
            current?.PlanPrice ?? 0m,
            targetPlan.Handle,
            targetPlan.Name,
            targetPlan.Price,
            PlanChangeTiming.Immediately,
            FromCents(preview.ProratedAdjustmentInCents),
            FromCents(preview.ChargeInCents),
            FromCents(preview.CreditAppliedInCents),
            FromCents(preview.PaymentDueInCents),
            effectiveAt: DateTimeOffset.UtcNow);
    }

    public async Task<CustomerSubscription> ChangePlanImmediatelyAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(
                subscriptionId,
                body: new SubscriptionProductMigrationRequest
                {
                    Migration = new SubscriptionProductMigration
                    {
                        ProductHandle = targetPlanHandle
                    }
                },
                ct: cancellationToken);

            return RequireSubscription("MigrateSubscriptionProduct", response.Subscription);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            throw Translate("MigrateSubscriptionProduct", ex,
                error => error.TryGetErrorListResponse1(out var validation) ? DescribeErrorList(validation) : null);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("MigrateSubscriptionProduct", ex);
        }
    }

    public async Task<CustomerSubscription> ChangePlanAtRenewalAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.UpdateSubscription(
                subscriptionId,
                body: new UpdateSubscriptionRequest
                {
                    Subscription = new UpdateSubscription
                    {
                        ProductHandle = targetPlanHandle,
                        ProductChangeDelayed = true
                    }
                },
                ct: cancellationToken);

            return RequireSubscription("UpdateSubscription", response.Subscription);
        }
        catch (SdkException<UpdateSubscriptionError> ex)
        {
            throw Translate("UpdateSubscription", ex,
                error => error.TryGetErrorListResponse1(out var validation) ? DescribeErrorList(validation) : null);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("UpdateSubscription", ex);
        }
    }

    public async Task<CustomerSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.PauseSubscription(subscriptionId, body: null, ct: cancellationToken);

            return RequireSubscription("PauseSubscription", response.Subscription);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            throw Translate("PauseSubscription", ex,
                error => error.TryGetErrorListResponse1(out var validation) ? DescribeErrorList(validation) : null);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("PauseSubscription", ex);
        }
    }

    public async Task<CustomerSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ResumeSubscription(
                subscriptionId,
                calendarBillingResumptionCharge: null,
                ct: cancellationToken);

            return RequireSubscription("ResumeSubscription", response.Subscription);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            throw Translate("ResumeSubscription", ex,
                error => error.TryGetErrorListResponse1(out var validation) ? DescribeErrorList(validation) : null);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("ResumeSubscription", ex);
        }
    }

    public async Task<CustomerSubscription> CancelSubscriptionImmediatelyAsync(int subscriptionId, string? reason, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.CancelSubscription(
                subscriptionId,
                body: BuildCancellationRequest(reason),
                ct: cancellationToken);

            return RequireSubscription("CancelSubscription", response.Subscription);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            throw Translate("CancelSubscription", ex, DescribeCancellationError);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("CancelSubscription", ex);
        }
    }

    public async Task<CustomerSubscription> CancelSubscriptionAtPeriodEndAsync(int subscriptionId, string? reason, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.SubscriptionStatus.InitiateDelayedCancellation(
                subscriptionId,
                body: BuildCancellationRequest(reason),
                ct: cancellationToken);
        }
        catch (SdkException<InitiateDelayedCancellationError> ex)
        {
            throw Translate("InitiateDelayedCancellation", ex,
                error => error.TryGetErrorListResponse1(out var validation) ? DescribeErrorList(validation) : null);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("InitiateDelayedCancellation", ex);
        }

        // A delayed cancellation returns only an acknowledgement message, so the caller's view of the
        // subscription has to be refreshed from the provider.
        var refreshed = await GetSubscriptionAsync(subscriptionId, cancellationToken);

        return refreshed ?? throw new BillingProviderException("InitiateDelayedCancellation",
            $"The cancellation was scheduled but subscription {subscriptionId} could no longer be read back.");
    }

    public async Task<CustomerSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, body: null, ct: cancellationToken);

            return RequireSubscription("ReactivateSubscription", response.Subscription);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            throw Translate("ReactivateSubscription", ex,
                error => error.TryGetErrorListResponse1(out var validation) ? DescribeErrorList(validation) : null);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("ReactivateSubscription", ex);
        }
    }

    /// <summary>
    /// Resolves the configured product family handle to the provider's numeric id. Handles are the durable
    /// identifier; ids are reassigned whenever the catalog is re-created, so they are never hard-coded.
    /// </summary>
    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_productFamilyId.HasValue)
        {
            return _productFamilyId.Value;
        }

        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new BillingConfigurationException(
                $"'{MaxioSettings.CONFIG_SECTION}:ProductFamilyHandle' is not configured, so no plans can be listed.");
        }

        IReadOnlyList<ProductFamilyResponse> families;

        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw("ListProductFamilies", ex);
        }
        catch (Exception ex) when (IsCommunicationFailure(ex))
        {
            throw CommunicationFailure("ListProductFamilies", ex);
        }

        var match = families
            .Select(response => response.ProductFamily)
            .FirstOrDefault(family => family is not null
                && string.Equals(family.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is null)
        {
            throw new BillingConfigurationException(
                $"Product family '{_settings.ProductFamilyHandle}' does not exist on the configured billing site. Provision it before using the subscription features.");
        }

        _productFamilyId = match.Id.Value;

        return _productFamilyId.Value;
    }

    private static CancellationRequest? BuildCancellationRequest(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        return new CancellationRequest
        {
            Subscription = new CancellationOptions
            {
                CancellationMessage = reason
            }
        };
    }

    private static CustomerSubscription RequireSubscription(string operation, Subscription? subscription)
    {
        var mapped = MapSubscription(subscription);

        return mapped ?? throw new BillingProviderException(operation,
            "The billing provider accepted the call but returned no subscription.");
    }

    private static SubscriptionPlan? MapPlan(Product? product)
    {
        if (product?.Id is null || string.IsNullOrWhiteSpace(product.Handle))
        {
            return null;
        }

        return new SubscriptionPlan(product.Id.Value,
            product.Handle,
            product.Name ?? product.Handle,
            product.Description,
            FromCents(product.PriceInCents),
            product.Interval ?? 0,
            MapIntervalUnit(product.IntervalUnit),
            product.RequireCreditCard == true,
            product.ArchivedAt.HasValue);
    }

    private static MeteredComponentDefinition MapComponent(Component component)
    {
        return new MeteredComponentDefinition(component.Id ?? 0,
            component.Handle ?? string.Empty,
            component.Name ?? component.Handle ?? string.Empty,
            component.UnitName,
            ResolveUnitPrice(component),
            component.Kind == ComponentKind.MeteredComponent);
    }

    private static BillingCustomer MapCustomer(Customer customer)
    {
        return new BillingCustomer(customer.Id ?? 0,
            customer.Reference,
            customer.Email,
            customer.FirstName,
            customer.LastName);
    }

    private static CustomerSubscription? MapSubscription(Subscription? subscription)
    {
        if (subscription?.Id is null)
        {
            return null;
        }

        var product = subscription.Product;

        return new CustomerSubscription(subscription.Id.Value,
            MapState(subscription.State),
            subscription.Customer?.Reference,
            subscription.Customer?.Id,
            product?.Handle,
            product?.Name,
            FromCents(subscription.ProductPriceInCents ?? product?.PriceInCents),
            subscription.CurrentPeriodStartedAt,
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt,
            subscription.CancelAtEndOfPeriod == true,
            subscription.DelayedCancelAt ?? subscription.ScheduledCancellationAt,
            subscription.NextProductHandle);
    }

    /// <summary>
    /// Maps the provider's subscription state onto the domain's. An unrecognised state maps to
    /// <see cref="DomainStatus.Unknown"/> rather than throwing, so a state Maxio adds later cannot break
    /// the storefront.
    /// </summary>
    private static DomainStatus MapState(SubscriptionState? state)
    {
        if (state is null)
        {
            return DomainStatus.Unknown;
        }

        if (state == SubscriptionState.Active) return DomainStatus.Active;
        if (state == SubscriptionState.Trialing) return DomainStatus.Trialing;
        if (state == SubscriptionState.TrialEnded) return DomainStatus.TrialEnded;
        if (state == SubscriptionState.Assessing) return DomainStatus.Assessing;
        if (state == SubscriptionState.Pending) return DomainStatus.Pending;
        if (state == SubscriptionState.AwaitingSignup) return DomainStatus.AwaitingSignup;
        if (state == SubscriptionState.SoftFailure) return DomainStatus.SoftFailure;
        if (state == SubscriptionState.PastDue) return DomainStatus.PastDue;
        if (state == SubscriptionState.Suspended) return DomainStatus.Suspended;
        if (state == SubscriptionState.Unpaid) return DomainStatus.Unpaid;
        if (state == SubscriptionState.Canceled) return DomainStatus.Canceled;
        if (state == SubscriptionState.Expired) return DomainStatus.Expired;
        if (state == SubscriptionState.FailedToCreate) return DomainStatus.FailedToCreate;

        // The provider's hold endpoint reports either of these for a paused subscription.
        if (state == SubscriptionState.Paused || state == SubscriptionState.OnHold) return DomainStatus.Paused;

        return DomainStatus.Unknown;
    }

    private static BillingIntervalUnit MapIntervalUnit(IntervalUnit? unit)
    {
        if (unit is null)
        {
            return BillingIntervalUnit.Unknown;
        }

        if (unit == IntervalUnit.Month) return BillingIntervalUnit.Month;
        if (unit == IntervalUnit.Day) return BillingIntervalUnit.Day;

        return BillingIntervalUnit.Unknown;
    }

    /// <summary>
    /// Component prices arrive either as a decimal string in major units or as an integer number of cents.
    /// Prefer the string, fall back to the cents field, so $0.01 never becomes $1.00.
    /// </summary>
    private static decimal ResolveUnitPrice(Component component)
    {
        if (!string.IsNullOrWhiteSpace(component.UnitPrice) &&
            decimal.TryParse(component.UnitPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return FromCents(component.PricePerUnitInCents);
    }

    /// <summary>Converts the provider's minor units to whole currency units.</summary>
    private static decimal FromCents(long? cents) => cents.HasValue ? cents.Value / 100m : 0m;

    /// <summary>Reads the int-or-string usage quantity union the provider returns.</summary>
    private static decimal? ReadQuantity(Quantity1? quantity)
    {
        if (quantity is null)
        {
            return null;
        }

        if (quantity.TryGetInt(out var whole))
        {
            return whole;
        }

        if (quantity.TryGetString(out var text) &&
            decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? DescribeErrorList(ErrorListResponse1 errors)
    {
        return errors.Errors.Count == 0 ? null : string.Join("; ", errors.Errors);
    }

    private static string? DescribeCancellationError(CancelSubscriptionApiError error)
    {
        if (!error.TryGetCancelSubscriptionErrorResponse(out var payload))
        {
            return null;
        }

        if (payload.TryGetErrorListResponse1(out var list))
        {
            return DescribeErrorList(list);
        }

        if (payload.TryGetSingleErrorResponse1(out var single))
        {
            return single.Error;
        }

        return null;
    }

    /// <summary>
    /// The provider's customer validation payload reuses a shared shape whose members do not map to
    /// customer fields, so extract whatever it does carry and let the caller fall back to the status text.
    /// </summary>
    private static string? DescribeCustomerErrors(CustomerErrorResponse1 response)
    {
        var messages = new List<string>();

        if (response.Errors?.PerPage is { Count: > 0 } perPage)
        {
            messages.AddRange(perPage);
        }

        if (response.Errors?.PricePoint is { Count: > 0 } pricePoint)
        {
            messages.AddRange(pricePoint);
        }

        return messages.Count == 0 ? null : string.Join("; ", messages);
    }

    private static BillingProviderException Translate<TError>(string operation,
        SdkException<TError> exception,
        Func<TError, string?> describe)
        where TError : ApiError
    {
        var message = describe(exception.Error);
        int? status = null;

        // TryGetRawError is the last resort, not a catch-all: it stays false for any status that has a
        // typed accessor of its own.
        if (message is null && exception.Error.TryGetRawError(out var raw))
        {
            message = SafeRawMessage(raw);
            status = (int)raw.StatusCode;
        }

        return new BillingProviderException(operation, message ?? exception.Message, status, exception);
    }

    private static BillingProviderException TranslateRaw(string operation, SdkException<RawError> exception)
    {
        return new BillingProviderException(operation, SafeRawMessage(exception.Error), StatusOf(exception), exception);
    }

    private static BillingProviderException CommunicationFailure(string operation, Exception exception)
    {
        // A malformed payload is reported separately from an unreachable host: the provider's generated
        // error models do not always match what it actually sends, and a raw JsonException escaping this
        // boundary would leak an SDK concern into the storefront.
        var message = exception is JsonException
            ? "The billing provider returned a response that could not be interpreted."
            : "The billing provider could not be reached.";

        return new BillingProviderException(operation, message, statusCode: null, innerException: exception);
    }

    private static bool IsCommunicationFailure(Exception exception)
    {
        return exception is HttpRequestException or TaskCanceledException or JsonException;
    }

    private static int StatusOf(SdkException<RawError> exception) => (int)exception.Error.StatusCode;

    private static string SafeRawMessage(RawError raw)
    {
        var status = (int)raw.StatusCode;

        try
        {
            var body = raw.ReadAsString();

            return string.IsNullOrWhiteSpace(body) ? $"HTTP {status}" : body;
        }
        catch (Exception)
        {
            // The body may be absent or non-textual; the status is still worth surfacing.
            return $"HTTP {status}";
        }
    }
}
