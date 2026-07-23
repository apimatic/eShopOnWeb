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
using DomainSubscription = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription;
using DomainSubscriptionState = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.SubscriptionState;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;
using MaxioSubscriptionState = MaxioAdvancedBilling.Models.Enums.SubscriptionState;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single integration point with Maxio Advanced Billing. Every Maxio call in eShopOnWeb goes
/// through this class, which normalizes results into the ApplicationCore model and surfaces every
/// provider failure as a <see cref="BillingProviderException"/>.
/// </summary>
/// <remarks>
/// The outbound target server is resolved here from <see cref="MaxioSettings.ResolveBaseUrl"/>, so
/// pointing the same build at production, a dev/sandbox tenant, or a local mock is a configuration
/// change and never a code change (§2.3).
/// </remarks>
public class MaxioBillingClient : IBillingClient
{
    /// <summary>Maxio authenticates the API key as the Basic username with a fixed placeholder password.</summary>
    private const string ApiKeyPasswordPlaceholder = "x";

    private const int PageSize = 200;
    private const int MaxPages = 25;
    private const int MaxProviderMessageLength = 512;

    private readonly Lazy<MaxioAdvancedBillingClient> _maxioClient;
    private readonly MaxioSettings _settings;
    private readonly MaxioCatalogCache _catalogCache;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        MaxioCatalogCache catalogCache,
        IAppLogger<MaxioBillingClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings.Value;
        _catalogCache = catalogCache;
        _logger = logger;

        // Built on first use, not at construction: a host whose Maxio section is missing still starts
        // and still serves every non-subscription page, and the misconfiguration surfaces on the
        // subscription pages as the same typed error any other provider failure does.
        _maxioClient = new Lazy<MaxioAdvancedBillingClient>(
            () => new MaxioAdvancedBillingClient(httpClient, BuildOptions(_settings)));
    }

    private MaxioAdvancedBillingClient Maxio
    {
        get
        {
            try
            {
                return _maxioClient.Value;
            }
            catch (InvalidOperationException ex)
            {
                throw new BillingProviderException($"The Maxio integration is not configured. {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Builds the SDK options: Basic auth from the configured API key, the data-centre region, and
    /// the outbound base URL. The URL is assigned to the region branch the environment selects,
    /// because the SDK only consults the branch matching <c>Environment</c>.
    /// </summary>
    private static MaxioAdvancedBillingClientOptions BuildOptions(MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException(
                $"'{MaxioSettings.ConfigurationSectionName}:{nameof(MaxioSettings.ApiKey)}' is not configured. Set it in .NET user-secrets; it must never be committed.");
        }

        var baseUrl = settings.ResolveBaseUrl();

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = ApiKeyPasswordPlaceholder
            },
            Environment = settings.IsEuRegion ? ServerEnvironment.Eu : ServerEnvironment.Us
        };

        // The value is a URL template; a literal absolute URL carries no {site} placeholder and is
        // therefore used verbatim — which is exactly what an explicit Maxio:BaseUrl must do.
        if (settings.IsEuRegion)
        {
            options.Server.Production.Eu.BaseUrl = baseUrl;
        }
        else
        {
            options.Server.Production.Us.BaseUrl = baseUrl;
        }

        return options;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(cancellationToken);

        return catalog.Plans;
    }

    public async Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        // Resolution is served from the time-bounded catalog cache, so an unknown handle costs
        // nothing: a caller cannot use one to force repeated provider round-trips.
        var catalog = await GetCatalogAsync(cancellationToken);

        return catalog.FindPlan(planHandle);
    }

    public async Task<MeteredComponentDefinition> GetMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(cancellationToken);

        return catalog.MeteredComponent ?? throw new BillingProviderException(
            $"Component '{_settings.MeteredComponentHandle}' was not found on product family '{catalog.ProductFamilyHandle}'. Seed it first (UC0).");
    }

    public async Task<BillingCustomer?> FindCustomerAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return null;
        }

        try
        {
            var response = await Maxio.Customers.ReadCustomerByReference(customerReference, ct: cancellationToken);

            return MapCustomer(response.Customer);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("Looking up the billing customer", ex.Error);
        }
        catch (HttpRequestException ex)
        {
            throw Transport("looking up the billing customer", ex);
        }
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(CustomerRegistration registration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var existing = await FindCustomerAsync(registration.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = registration.FirstName,
                LastName = registration.LastName,
                Email = registration.Email,
                Reference = registration.Reference
            }
        };

        try
        {
            var response = await Maxio.Customers.CreateCustomer(body, ct: cancellationToken);

            return MapCustomer(response.Customer);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // The reference is unique provider-side, so a concurrent subscribe can lose this race.
            // Re-reading by reference makes creation idempotent rather than an error.
            var raced = await FindCustomerAsync(registration.Reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw Failure("Creating the billing customer", ex.Error, null);
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("Creating the billing customer", ex.Error);
        }
        catch (HttpRequestException ex)
        {
            throw Transport("creating the billing customer", ex);
        }
    }

    public async Task<IReadOnlyCollection<DomainSubscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<DomainSubscription>();
        }

        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await Maxio.Customers.ListCustomerSubscriptions(customer.Id, ct: cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("Listing the customer's subscriptions", ex.Error);
        }
        catch (HttpRequestException ex)
        {
            throw Transport("listing the customer's subscriptions", ex);
        }

        return responses
            .Select(r => r.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!, customer.Reference))
            .ToList();
    }

    public async Task<DomainSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Maxio.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: cancellationToken);

            return response.Subscription is null ? null : MapSubscription(response.Subscription, null);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("Reading the subscription", ex.Error);
        }
        catch (HttpRequestException ex)
        {
            throw Transport("reading the subscription", ex);
        }
    }

    public async Task<DomainSubscription> CreateSubscriptionAsync(string customerReference, string planHandle, CancellationToken cancellationToken = default)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerReference = customerReference,

                // The demo plans do not require a payment method; remittance keeps the flow free of
                // card capture and 3-DS while still producing a real invoice on renewal.
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        SubscriptionResponse response;
        try
        {
            response = await Maxio.Subscriptions.CreateSubscription(body, ct: cancellationToken);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw Failure("Creating the subscription", ex.Error, TryReadErrors(ex.Error.TryGetErrorListResponse1));
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("Creating the subscription", ex.Error);
        }
        catch (HttpRequestException ex)
        {
            throw Transport("creating the subscription", ex);
        }

        var subscription = response.Subscription
            ?? throw new BillingProviderException("Maxio accepted the enrollment but returned no subscription.");

        return MapSubscription(subscription, customerReference);
    }

    public async Task<UsageRecord> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        var component = await GetMeteredComponentAsync(cancellationToken);

        var body = new CreateUsageRequest
        {
            Usage = new CreateUsage
            {
                Quantity = (double)quantity,
                Memo = memo
            }
        };

        UsageResponse response;
        try
        {
            response = await Maxio.SubscriptionComponents.CreateUsage(
                subscriptionIdOrReference: SubscriptionIdOrReference.Int(subscriptionId),
                componentId: ComponentIdModel.Int(component.Id),
                body: body,
                ct: cancellationToken);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            throw Failure("Recording usage", ex.Error, TryReadErrors(ex.Error.TryGetErrorListResponse1));
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("Recording usage", ex.Error);
        }
        catch (HttpRequestException ex)
        {
            throw Transport("recording usage", ex);
        }

        var usage = response.Usage;

        return new UsageRecord(
            usage.Id ?? 0,
            usage.SubscriptionId ?? subscriptionId,
            usage.ComponentId ?? component.Id,
            usage.ComponentHandle ?? component.Handle,
            ReadQuantity(usage.Quantity) ?? quantity,
            usage.Memo ?? memo,
            usage.CreatedAt);
    }

    public async Task<int?> GetPeriodToDateUsageAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var component = await GetMeteredComponentAsync(cancellationToken);

        try
        {
            var response = await Maxio.SubscriptionComponents.ReadSubscriptionComponent(subscriptionId, component.Id, ct: cancellationToken);

            return response.Component?.UnitBalance;
        }
        catch (SdkException<ReadSubscriptionComponentError> ex)
        {
            // The component only materializes on a subscription once it has been touched; treat
            // "not found" as "nothing accrued yet" rather than an error.
            if (ex.Error.TryGetNoContent(out _))
            {
                return 0;
            }

            throw Failure("Reading the period-to-date usage", ex.Error, null);
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("Reading the period-to-date usage", ex.Error);
        }
        catch (HttpRequestException ex)
        {
            throw Transport("reading the period-to-date usage", ex);
        }
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new BillingProviderException($"Subscription {subscriptionId} was not found on the billing provider.");

        var targetPlan = await FindPlanAsync(targetPlanHandle, cancellationToken)
            ?? throw new BillingProviderException($"Plan '{targetPlanHandle}' does not resolve on the billing provider. Check the configured product handles (UC0).");

        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            // A deferred change is not prorated: nothing is owed now and the new price simply takes
            // effect from the next period.
            return new PlanChangePreview(
                subscriptionId,
                subscription.PlanHandle,
                targetPlan.Handle,
                timing,
                proratedAdjustmentInCents: 0,
                chargeInCents: 0,
                creditAppliedInCents: 0,
                paymentDueInCents: 0,
                newPlanPriceInCents: targetPlan.PriceInCents,
                effectiveAt: subscription.CurrentPeriodEndsAt);
        }

        var body = new SubscriptionMigrationPreviewRequest
        {
            Migration = new SubscriptionMigrationPreviewOptions
            {
                ProductHandle = targetPlan.Handle,
                IncludeTrial = false,
                IncludeInitialCharge = false,
                IncludeCoupons = true,
                PreservePeriod = true
            }
        };

        SubscriptionMigrationPreviewResponse response;
        try
        {
            response = await Maxio.SubscriptionProducts.PreviewSubscriptionProductMigration(subscriptionId, body, ct: cancellationToken);
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            throw Failure("Previewing the plan change", ex.Error, TryReadErrors(ex.Error.TryGetErrorListResponse1));
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("Previewing the plan change", ex.Error);
        }
        catch (HttpRequestException ex)
        {
            throw Transport("previewing the plan change", ex);
        }

        var migration = response.Migration;

        return new PlanChangePreview(
            subscriptionId,
            subscription.PlanHandle,
            targetPlan.Handle,
            timing,
            migration.ProratedAdjustmentInCents ?? 0,
            migration.ChargeInCents ?? 0,
            migration.CreditAppliedInCents ?? 0,
            migration.PaymentDueInCents ?? 0,
            targetPlan.PriceInCents,
            effectiveAt: null);
    }

    public async Task<DomainSubscription> ChangePlanAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            return await ChangePlanAtNextRenewalAsync(subscriptionId, targetPlanHandle, cancellationToken);
        }

        var body = new SubscriptionProductMigrationRequest
        {
            Migration = new SubscriptionProductMigration
            {
                ProductHandle = targetPlanHandle,
                IncludeTrial = false,
                IncludeInitialCharge = false,
                IncludeCoupons = true,
                PreservePeriod = true
            }
        };

        SubscriptionResponse response;
        try
        {
            response = await Maxio.SubscriptionProducts.MigrateSubscriptionProduct(subscriptionId, body, ct: cancellationToken);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            throw Failure("Changing the plan", ex.Error, TryReadErrors(ex.Error.TryGetErrorListResponse1));
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("Changing the plan", ex.Error);
        }
        catch (HttpRequestException ex)
        {
            throw Transport("changing the plan", ex);
        }

        return RequireSubscription(response, "Maxio accepted the plan change but returned no subscription.");
    }

    private async Task<DomainSubscription> ChangePlanAtNextRenewalAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken)
    {
        var body = new UpdateSubscriptionRequest
        {
            Subscription = new UpdateSubscription
            {
                ProductHandle = targetPlanHandle,
                ProductChangeDelayed = true
            }
        };

        SubscriptionResponse response;
        try
        {
            response = await Maxio.Subscriptions.UpdateSubscription(subscriptionId, body, ct: cancellationToken);
        }
        catch (SdkException<UpdateSubscriptionError> ex)
        {
            throw Failure("Scheduling the plan change", ex.Error, TryReadErrors(ex.Error.TryGetErrorListResponse1));
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("Scheduling the plan change", ex.Error);
        }
        catch (HttpRequestException ex)
        {
            throw Transport("scheduling the plan change", ex);
        }

        return RequireSubscription(response, "Maxio accepted the scheduled plan change but returned no subscription.");
    }

    public async Task<DomainSubscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        SubscriptionResponse response;
        try
        {
            response = await Maxio.SubscriptionStatus.PauseSubscription(subscriptionId, body: null, ct: cancellationToken);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            throw Failure("Pausing the subscription", ex.Error, TryReadErrors(ex.Error.TryGetErrorListResponse1));
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("Pausing the subscription", ex.Error);
        }
        catch (HttpRequestException ex)
        {
            throw Transport("pausing the subscription", ex);
        }

        return RequireSubscription(response, "Maxio accepted the pause but returned no subscription.");
    }

    public async Task<DomainSubscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        SubscriptionResponse response;
        try
        {
            response = await Maxio.SubscriptionStatus.ResumeSubscription(subscriptionId, calendarBillingResumptionCharge: null, ct: cancellationToken);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            throw Failure("Resuming the subscription", ex.Error, TryReadErrors(ex.Error.TryGetErrorListResponse1));
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("Resuming the subscription", ex.Error);
        }
        catch (HttpRequestException ex)
        {
            throw Transport("resuming the subscription", ex);
        }

        return RequireSubscription(response, "Maxio accepted the resume but returned no subscription.");
    }

    public async Task<DomainSubscription> CancelAsync(int subscriptionId, CancellationTiming timing, string? reason, CancellationToken cancellationToken = default)
    {
        var body = string.IsNullOrWhiteSpace(reason)
            ? null
            : new CancellationRequest
            {
                Subscription = new CancellationOptions { CancellationMessage = reason }
            };

        if (timing == CancellationTiming.EndOfPeriod)
        {
            return await CancelAtEndOfPeriodAsync(subscriptionId, body, cancellationToken);
        }

        SubscriptionResponse response;
        try
        {
            response = await Maxio.SubscriptionStatus.CancelSubscription(subscriptionId, body, ct: cancellationToken);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            throw Failure("Cancelling the subscription", ex.Error, ReadCancellationErrors(ex.Error));
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("Cancelling the subscription", ex.Error);
        }
        catch (HttpRequestException ex)
        {
            throw Transport("cancelling the subscription", ex);
        }

        return RequireSubscription(response, "Maxio accepted the cancellation but returned no subscription.");
    }

    private async Task<DomainSubscription> CancelAtEndOfPeriodAsync(int subscriptionId, CancellationRequest? body, CancellationToken cancellationToken)
    {
        try
        {
            await Maxio.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId, body, ct: cancellationToken);
        }
        catch (SdkException<InitiateDelayedCancellationError> ex)
        {
            throw Failure("Scheduling the end-of-period cancellation", ex.Error, TryReadErrors(ex.Error.TryGetErrorListResponse1));
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("Scheduling the end-of-period cancellation", ex.Error);
        }
        catch (HttpRequestException ex)
        {
            throw Transport("scheduling the end-of-period cancellation", ex);
        }

        // The delayed-cancel endpoint returns only a message, so the provider's own view of the
        // subscription is read back rather than assumed.
        return await GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new BillingProviderException(
                $"Maxio scheduled the end-of-period cancellation but subscription {subscriptionId} could not be read back.");
    }

    public async Task<DomainSubscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        SubscriptionResponse response;
        try
        {
            response = await Maxio.SubscriptionStatus.ReactivateSubscription(subscriptionId, body: null, ct: cancellationToken);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            throw Failure("Reactivating the subscription", ex.Error, TryReadErrors(ex.Error.TryGetErrorListResponse1));
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("Reactivating the subscription", ex.Error);
        }
        catch (HttpRequestException ex)
        {
            throw Transport("reactivating the subscription", ex);
        }

        return RequireSubscription(response, "Maxio accepted the reactivation but returned no subscription.");
    }

    private Task<MaxioCatalog> GetCatalogAsync(CancellationToken cancellationToken) =>
        _catalogCache.GetAsync(LoadCatalogAsync, cancellationToken);

    /// <summary>
    /// Resolves the configured handles to their live Maxio ids. Handles are the durable identifiers;
    /// any configured id is treated as a hint and only used to warn about configuration drift.
    /// </summary>
    private async Task<MaxioCatalog> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        // Credentials and the target server are validated first, so a host with no Maxio section at
        // all reports the missing key rather than a downstream symptom of it.
        _ = Maxio;

        var familyHandle = RequireConfigured(_settings.ProductFamilyHandle, nameof(MaxioSettings.ProductFamilyHandle));

        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await Maxio.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Failure("Listing product families", ex.Error);
        }
        catch (HttpRequestException ex)
        {
            throw Transport("listing product families", ex);
        }

        var family = families
            .Select(r => r.ProductFamily)
            .FirstOrDefault(f => f is not null && string.Equals(f.Handle, familyHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new BillingProviderException(
                $"Product family '{familyHandle}' was not found on the configured Maxio site. Seed it first (UC0).");

        var familyId = family.Id
            ?? throw new BillingProviderException($"Maxio returned product family '{familyHandle}' without an id.");

        WarnOnIdDrift("product family", familyHandle, _settings.ProductFamilyId, familyId);

        var plans = await LoadPlansAsync(familyId, cancellationToken);
        var component = await LoadMeteredComponentAsync(familyId, cancellationToken);

        return new MaxioCatalog(familyId, familyHandle, plans, component);
    }

    private async Task<IReadOnlyList<SubscriptionPlan>> LoadPlansAsync(int familyId, CancellationToken cancellationToken)
    {
        var familyIdText = familyId.ToString(CultureInfo.InvariantCulture);
        var plans = new List<SubscriptionPlan>();

        for (var page = 1; page <= MaxPages; page++)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await Maxio.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyIdText,
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
                throw Failure("Listing plans", ex.Error, null);
            }
            catch (SdkException<RawError> ex)
            {
                throw Failure("Listing plans", ex.Error);
            }
            catch (HttpRequestException ex)
            {
                throw Transport("listing plans", ex);
            }

            foreach (var product in batch.Select(r => r.Product))
            {
                // A product without a handle cannot be referenced durably, and an archived one is no
                // longer offerable — neither belongs in the customer-facing plan list.
                if (string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt.HasValue)
                {
                    continue;
                }

                plans.Add(MapPlan(product));
            }

            if (batch.Count < PageSize)
            {
                break;
            }
        }

        WarnOnIdDrift("product", _settings.DefaultProductHandle, _settings.DefaultProductId,
            plans.FirstOrDefault(p => string.Equals(p.Handle, _settings.DefaultProductHandle, StringComparison.OrdinalIgnoreCase))?.Id);
        WarnOnIdDrift("product", _settings.AlternateProductHandle, _settings.AlternateProductId,
            plans.FirstOrDefault(p => string.Equals(p.Handle, _settings.AlternateProductHandle, StringComparison.OrdinalIgnoreCase))?.Id);

        return plans;
    }

    private async Task<MeteredComponentDefinition?> LoadMeteredComponentAsync(int familyId, CancellationToken cancellationToken)
    {
        var componentHandle = _settings.MeteredComponentHandle;
        if (string.IsNullOrWhiteSpace(componentHandle))
        {
            return null;
        }

        for (var page = 1; page <= MaxPages; page++)
        {
            IReadOnlyList<ComponentResponse> batch;
            try
            {
                batch = await Maxio.Components.ListComponentsForProductFamily(
                    productFamilyId: familyId,
                    includeArchived: false,
                    filter: null,
                    dateField: null,
                    endDate: null,
                    endDatetime: null,
                    startDate: null,
                    startDatetime: null,
                    page: page,
                    perPage: PageSize,
                    ct: cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                throw Failure("Listing product-family components", ex.Error);
            }
            catch (HttpRequestException ex)
            {
                throw Transport("listing product-family components", ex);
            }

            foreach (var component in batch.Select(r => r.Component))
            {
                if (string.Equals(component.Handle, componentHandle, StringComparison.OrdinalIgnoreCase))
                {
                    var mapped = MapComponent(component);
                    WarnOnIdDrift("component", componentHandle, _settings.MeteredComponentId, mapped.Id);

                    return mapped;
                }
            }

            if (batch.Count < PageSize)
            {
                break;
            }
        }

        return null;
    }

    private static SubscriptionPlan MapPlan(Product product) => new(
        product.Id ?? 0,
        product.Handle!,
        product.Name ?? product.Handle!,
        product.Description,
        product.PriceInCents ?? 0,
        product.Interval ?? 0,
        product.IntervalUnit?.Value ?? "unknown",
        product.RequireCreditCard ?? false,
        product.ProductFamily?.Handle);

    private static MeteredComponentDefinition MapComponent(Component component) => new(
        component.Id ?? 0,
        component.Handle ?? string.Empty,
        component.Name ?? component.Handle ?? string.Empty,
        component.Kind?.Value ?? "unknown",
        component.Kind == ComponentKind.MeteredComponent,
        component.UnitName,
        ReadUnitPrice(component),
        component.PricingScheme?.Value,
        component.ProductFamilyId,
        component.ProductFamilyHandle);

    private static BillingCustomer MapCustomer(Customer customer) => new(
        customer.Id ?? 0,
        customer.Reference ?? customer.Email ?? string.Empty,
        customer.Email ?? string.Empty,
        customer.FirstName ?? string.Empty,
        customer.LastName ?? string.Empty);

    private static DomainSubscription MapSubscription(MaxioSubscription subscription, string? fallbackCustomerReference)
    {
        var id = subscription.Id
            ?? throw new BillingProviderException("Maxio returned a subscription without an id.");

        var customerId = subscription.Customer?.Id ?? 0;

        // Ownership decisions depend on this value, so it never falls back to something another user
        // could match: an unresolvable reference becomes a synthetic, per-customer value.
        var customerReference = FirstNonEmpty(
            subscription.Customer?.Reference,
            fallbackCustomerReference,
            subscription.Customer?.Email)
            ?? string.Create(CultureInfo.InvariantCulture, $"maxio-customer-{customerId}");

        var planHandle = FirstNonEmpty(subscription.Product?.Handle) ?? "unknown";
        var planName = FirstNonEmpty(subscription.Product?.Name) ?? planHandle;
        var planPriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0;

        return new DomainSubscription(
            id,
            customerId,
            customerReference,
            planHandle,
            planName,
            planPriceInCents,
            MapState(subscription.State),
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt,
            subscription.CancelAtEndOfPeriod ?? false,
            subscription.DelayedCancelAt);
    }

    /// <summary>
    /// Projects Maxio's subscription state onto the domain enum. An unrecognized value maps to
    /// <see cref="DomainSubscriptionState.Unknown"/>, which the service treats as "no transition is
    /// safe" rather than guessing.
    /// </summary>
    internal static DomainSubscriptionState MapState(MaxioSubscriptionState? state)
    {
        if (state is null)
        {
            return DomainSubscriptionState.Unknown;
        }

        if (state == MaxioSubscriptionState.Active) return DomainSubscriptionState.Active;
        if (state == MaxioSubscriptionState.Trialing) return DomainSubscriptionState.Trialing;
        if (state == MaxioSubscriptionState.Assessing) return DomainSubscriptionState.Assessing;
        if (state == MaxioSubscriptionState.Pending) return DomainSubscriptionState.Pending;
        if (state == MaxioSubscriptionState.AwaitingSignup) return DomainSubscriptionState.AwaitingSignup;
        if (state == MaxioSubscriptionState.SoftFailure) return DomainSubscriptionState.SoftFailure;
        if (state == MaxioSubscriptionState.PastDue) return DomainSubscriptionState.PastDue;
        if (state == MaxioSubscriptionState.Suspended) return DomainSubscriptionState.Suspended;
        if (state == MaxioSubscriptionState.Canceled) return DomainSubscriptionState.Canceled;
        if (state == MaxioSubscriptionState.Expired) return DomainSubscriptionState.Expired;
        if (state == MaxioSubscriptionState.Paused) return DomainSubscriptionState.Paused;
        if (state == MaxioSubscriptionState.OnHold) return DomainSubscriptionState.OnHold;
        if (state == MaxioSubscriptionState.Unpaid) return DomainSubscriptionState.Unpaid;
        if (state == MaxioSubscriptionState.TrialEnded) return DomainSubscriptionState.TrialEnded;
        if (state == MaxioSubscriptionState.FailedToCreate) return DomainSubscriptionState.FailedToCreate;

        return DomainSubscriptionState.Unknown;
    }

    private static decimal? ReadUnitPrice(Component component)
    {
        if (!string.IsNullOrWhiteSpace(component.UnitPrice) &&
            decimal.TryParse(component.UnitPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return component.PricePerUnitInCents.HasValue ? component.PricePerUnitInCents.Value / 100m : null;
    }

    /// <summary>Maxio echoes a recorded quantity back as either a number or a decimal string.</summary>
    private static decimal? ReadQuantity(Quantity1? quantity)
    {
        if (quantity is null)
        {
            return null;
        }

        if (quantity.TryGetInt(out var asInt))
        {
            return asInt;
        }

        if (quantity.TryGetString(out var asString) &&
            decimal.TryParse(asString, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DomainSubscription RequireSubscription(SubscriptionResponse response, string messageWhenMissing)
    {
        var subscription = response.Subscription ?? throw new BillingProviderException(messageWhenMissing);

        return MapSubscription(subscription, null);
    }

    private delegate bool TryGetErrorList(out ErrorListResponse1 errors);

    private static IReadOnlyList<string>? TryReadErrors(TryGetErrorList tryGet) =>
        tryGet(out var errors) ? errors.Errors : null;

    private static IReadOnlyList<string>? ReadCancellationErrors(CancelSubscriptionApiError error)
    {
        if (!error.TryGetCancelSubscriptionErrorResponse(out var response))
        {
            return null;
        }

        if (response.TryGetErrorListResponse1(out var list))
        {
            return list.Errors;
        }

        return response.TryGetSingleErrorResponse1(out var single) ? new[] { single.Error } : null;
    }

    /// <summary>
    /// Turns an untyped provider failure into a domain exception. The provider's raw body goes to the
    /// log, never into the message: that message reaches storefront pages and API clients, and an
    /// upstream error body is a diagnostic, not something to show an unprivileged user.
    /// </summary>
    private BillingProviderException Failure(string operation, RawError raw)
    {
        var status = (int)raw.StatusCode;

        _logger.LogWarning("{0} failed with HTTP {1}. Maxio responded: {2}", operation, status, Summarize(raw));

        return new BillingProviderException($"{operation} failed with HTTP {status}.", status);
    }

    /// <summary>
    /// Turns a typed provider failure into a domain exception. A validation payload is the provider's
    /// own description of what the caller got wrong, so it is safe — and useful — to surface verbatim.
    /// </summary>
    private BillingProviderException Failure(string operation, ApiError error, IReadOnlyList<string>? messages)
    {
        if (messages is { Count: > 0 })
        {
            return new BillingProviderException($"{operation} was rejected by Maxio: {string.Join("; ", messages)}", 422);
        }

        if (error.TryGetRawError(out var raw))
        {
            return Failure(operation, raw);
        }

        _logger.LogWarning("{0} failed and Maxio returned no diagnosable error body.", operation);

        return new BillingProviderException($"{operation} failed and Maxio returned no diagnosable error body.");
    }

    private static BillingProviderException Transport(string operation, HttpRequestException exception) =>
        new($"Could not reach Maxio while {operation}: {exception.Message}", exception);

    /// <summary>
    /// Condenses a provider error body into a short, single-line message so that an HTML error page
    /// or a large payload never ends up in a log line or a user-facing message.
    /// </summary>
    private static string Summarize(RawError raw)
    {
        string body;
        try
        {
            body = raw.ReadAsString();
        }
        catch (Exception)
        {
            return "<unreadable response body>";
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return "<empty response body>";
        }

        body = body.Replace('\r', ' ').Replace('\n', ' ').Trim();

        return body.Length <= MaxProviderMessageLength ? body : body[..MaxProviderMessageLength] + "…";
    }

    private static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    private static string RequireConfigured(string value, string settingName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new BillingProviderException(
                $"The Maxio integration is not configured: '{MaxioSettings.ConfigurationSectionName}:{settingName}' is not set.")
            : value;

    private void WarnOnIdDrift(string entity, string handle, int? configuredId, int? liveId)
    {
        if (configuredId is null || liveId is null || configuredId == liveId || string.IsNullOrWhiteSpace(handle))
        {
            return;
        }

        _logger.LogWarning(
            "Configured {0} id {1} for handle '{2}' no longer matches the live id {3}; the live id resolved from the handle is used.",
            entity,
            configuredId.Value,
            handle,
            liveId.Value);
    }
}
