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
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using CollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod;
using MaxioComponentKind = MaxioAdvancedBilling.Models.Enums.ComponentKind;
using MaxioIntervalUnit = MaxioAdvancedBilling.Models.Enums.IntervalUnit;
using MaxioSubscriptionState = MaxioAdvancedBilling.Models.Enums.SubscriptionState;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The one and only place eShopOnWeb talks to Maxio Advanced Billing (plan.md §2.2). Everything
/// outside this class works against <see cref="IBillingClient"/> and the provider-agnostic domain
/// types, so no Maxio SDK type — request, response, enum or exception — ever escapes Infrastructure.
/// <para>
/// Two conversions happen here and nowhere else: Maxio's integer-cent amounts become decimal
/// currency units, and every Maxio failure becomes a <see cref="BillingProviderException"/> (the
/// provider refused or was unreachable) or a <see cref="BillingConfigurationException"/> (the
/// catalog does not match configuration).
/// </para>
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    /// <summary>Maxio's HTTP Basic scheme uses the API key as the username and the literal "x" as the password.</summary>
    private const string BasicAuthPassword = "x";

    /// <summary>Maxio addresses an entity by handle inside a URL path with this prefix.</summary>
    private const string HandlePrefix = "handle:";

    /// <summary>Page size used when following pagination. Chosen to keep usage totals to few round trips.</summary>
    private const int PageSize = 200;

    private readonly MaxioAdvancedBillingClient _maxio;
    private readonly MaxioSettings _settings;
    private readonly SemaphoreSlim _catalogLock = new(1, 1);

    private int? _resolvedProductFamilyId;
    private MeteredComponent? _resolvedMeteredComponent;

    /// <summary>
    /// Builds the provider client over an <see cref="HttpClient"/> supplied by
    /// <c>IHttpClientFactory</c>. The injected client is also the seam tests substitute, so the
    /// whole integration is exercisable without touching the network.
    /// </summary>
    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _settings = options.Value ?? throw new ArgumentException("Maxio settings are not configured.", nameof(options));

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new BillingConfigurationException(
                $"'{MaxioSettings.SectionName}:ApiKey' is not configured. Supply it through user-secrets or the environment.");
        }

        var sdkOptions = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = _settings.ApiKey!,
                Password = BasicAuthPassword
            },
            Environment = _settings.IsEuRegion ? ServerEnvironment.Eu : ServerEnvironment.Us
        };

        ConfigureTargetServer(sdkOptions);

        _maxio = new MaxioAdvancedBillingClient(httpClient, sdkOptions);
    }

    /// <summary>
    /// Points the SDK at the configured target server. An explicit <c>Maxio:BaseUrl</c> always wins
    /// and is applied verbatim to both regional servers, so the override cannot be defeated by the
    /// region setting; otherwise the site subdomain is substituted into the provider's own default
    /// host template. This is what makes the identical build retargetable between production, a
    /// sandbox tenant and a local mock through configuration alone (plan.md §2.3).
    /// </summary>
    private void ConfigureTargetServer(MaxioAdvancedBillingClientOptions sdkOptions)
    {
        if (_settings.HasExplicitBaseUrl)
        {
            var baseUrl = _settings.ResolveBaseUrl();
            sdkOptions.Server.Production.Us.BaseUrl = baseUrl;
            sdkOptions.Server.Production.Eu.BaseUrl = baseUrl;
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.Subdomain))
        {
            throw new BillingConfigurationException(
                $"Neither '{MaxioSettings.SectionName}:BaseUrl' nor '{MaxioSettings.SectionName}:Subdomain' is configured, " +
                "so there is no Maxio server to target.");
        }

        var subdomain = _settings.Subdomain!.Trim();
        sdkOptions.Server.Production.Us.Site = subdomain;
        sdkOptions.Server.Production.Eu.Site = subdomain;
    }

    /// <summary>The outbound base URL this client is targeting. Exposed for startup diagnostics.</summary>
    public string TargetBaseUrl => _settings.ResolveBaseUrl();

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProductResponse> products;

        try
        {
            products = await _maxio.ProductFamilies.ListProductsForProductFamily(ProductFamilySelector,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: PageSize,
                ct: cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            // A 404 here means the configured product family handle does not resolve.
            if (ex.Error.TryGetString(out var message))
            {
                throw new BillingProviderException(nameof(ListPlansAsync), 404, message);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, nameof(ListPlansAsync));
            }

            throw Unrecognised(nameof(ListPlansAsync));
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, nameof(ListPlansAsync));
        }
        catch (Exception ex) when (IsUnreadableProviderFailure(ex, cancellationToken))
        {
            throw FromUnreadable(nameof(ListPlansAsync), ex);
        }

        return products
            .Select(response => response.Product)
            .Where(product => product is not null && product.ArchivedAt is null)
            .Select(product => MapPlan(product!))
            .Where(plan => plan is not null)
            .Select(plan => plan!)
            .OrderBy(plan => plan.Price)
            .ToList();
    }

    public async Task<SubscriptionPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        try
        {
            var response = await InvokeAsync(nameof(FindPlanByHandleAsync),
                ct => _maxio.Products.ReadProductByHandle(planHandle.Trim(), ct: ct),
                cancellationToken).ConfigureAwait(false);

            var product = response.Product;
            return product is null || product.ArchivedAt is not null ? null : MapPlan(product);
        }
        catch (BillingProviderException ex) when (ex.IsNotFound)
        {
            return null;
        }
    }

    public async Task<BillingCatalogValidation> ValidateCatalogAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var familyHandle = _settings.ProductFamilyHandle ?? string.Empty;
        int? familyId = null;
        IReadOnlyList<SubscriptionPlan> plans = Array.Empty<SubscriptionPlan>();
        var meteredValid = false;
        int? meteredId = null;
        string? meteredKind = null;

        try
        {
            familyId = await ResolveProductFamilyIdAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            errors.Add(ex.Message);
        }

        if (familyId.HasValue)
        {
            try
            {
                plans = await ListPlansAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (BillingProviderException ex)
            {
                errors.Add($"Plans could not be listed: {ex.ProviderMessage}");
            }

            foreach (var handle in ConfiguredPlanHandles())
            {
                if (!plans.Any(plan => string.Equals(plan.Handle, handle, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"Configured plan handle '{handle}' does not resolve to a plan in product family '{familyHandle}'.");
                }
            }

            try
            {
                var component = await ResolveMeteredComponentAsync(cancellationToken).ConfigureAwait(false);
                meteredValid = true;
                meteredId = component.Id;
                meteredKind = component.Kind;
            }
            catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
            {
                errors.Add(ex.Message);
                meteredKind = (ex as BillingConfigurationException) is null ? null : meteredKind;
            }
        }

        return new BillingCatalogValidation(familyHandle, familyId, plans, errors, meteredValid, meteredId, meteredKind);
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string userReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userReference))
        {
            return null;
        }

        try
        {
            var response = await InvokeAsync(nameof(FindCustomerByReferenceAsync),
                ct => _maxio.Customers.ReadCustomerByReference(userReference, ct: ct),
                cancellationToken).ConfigureAwait(false);

            return MapCustomer(response.Customer, userReference);
        }
        catch (BillingProviderException ex) when (ex.IsNotFound)
        {
            return null;
        }
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(string userReference,
        string email,
        CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerByReferenceAsync(userReference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await CreateCustomerAsync(userReference, email, cancellationToken).ConfigureAwait(false);
        }
        catch (BillingProviderException)
        {
            // The create failed. The overwhelmingly common cause is that a concurrent request
            // created this customer between our lookup and our create, so the reference is now
            // taken. Re-read by reference and use the winner: idempotency is anchored on the
            // reference itself, never on our ability to parse the provider's rejection body.
            var raced = await TryFindQuietlyAsync(userReference, cancellationToken).ConfigureAwait(false);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    /// <summary>Reads a customer by reference, treating any failure as "not found".</summary>
    private async Task<BillingCustomer?> TryFindQuietlyAsync(string userReference, CancellationToken cancellationToken)
    {
        try
        {
            return await FindCustomerByReferenceAsync(userReference, cancellationToken).ConfigureAwait(false);
        }
        catch (BillingProviderException)
        {
            return null;
        }
    }

    private async Task<BillingCustomer> CreateCustomerAsync(string userReference,
        string email,
        CancellationToken cancellationToken)
    {
        var (firstName, lastName) = DeriveName(email, userReference);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = userReference
            }
        };

        try
        {
            var response = await _maxio.Customers.CreateCustomer(body, ct: cancellationToken).ConfigureAwait(false);
            return MapCustomer(response.Customer, userReference)
                ?? throw new BillingProviderException(nameof(EnsureCustomerAsync), 0, "The provider returned no customer.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var conflict))
            {
                throw new BillingProviderException(nameof(EnsureCustomerAsync), 422, DescribeCustomerError(conflict));
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, nameof(EnsureCustomerAsync));
            }

            throw Unrecognised(nameof(EnsureCustomerAsync));
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, nameof(EnsureCustomerAsync));
        }
        catch (Exception ex) when (IsUnreadableProviderFailure(ex, cancellationToken))
        {
            throw FromUnreadable(nameof(EnsureCustomerAsync), ex);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForCustomerAsync(int customerId,
        CancellationToken cancellationToken = default)
    {
        var responses = await InvokeAsync(nameof(ListSubscriptionsForCustomerAsync),
            ct => _maxio.Customers.ListCustomerSubscriptions(customerId, ct: ct),
            cancellationToken).ConfigureAwait(false);

        return responses
            .Select(response => MapSubscription(response.Subscription))
            .Where(subscription => subscription is not null)
            .Select(subscription => subscription!)
            .ToList();
    }

    public async Task<CustomerSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await InvokeAsync(nameof(GetSubscriptionAsync),
                ct => _maxio.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: ct),
                cancellationToken).ConfigureAwait(false);

            return MapSubscription(response.Subscription);
        }
        catch (BillingProviderException ex) when (ex.IsNotFound)
        {
            return null;
        }
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(int customerId,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                CustomerId = customerId,
                ProductHandle = planHandle,
                PaymentCollectionMethod = ResolveCollectionMethod()
            }
        };

        try
        {
            var response = await _maxio.Subscriptions.CreateSubscription(body, ct: cancellationToken).ConfigureAwait(false);
            return MapSubscription(response.Subscription)
                ?? throw new BillingProviderException(nameof(CreateSubscriptionAsync), 0, "The provider returned no subscription.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw From422(errors, nameof(CreateSubscriptionAsync));
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, nameof(CreateSubscriptionAsync));
            }

            throw Unrecognised(nameof(CreateSubscriptionAsync));
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, nameof(CreateSubscriptionAsync));
        }
        catch (Exception ex) when (IsUnreadableProviderFailure(ex, cancellationToken))
        {
            throw FromUnreadable(nameof(CreateSubscriptionAsync), ex);
        }
    }

    public async Task<UsageRecord> RecordUsageAsync(int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        // Refuse to bill anything until the configured component has been proven to be metered.
        var component = await ResolveMeteredComponentAsync(cancellationToken).ConfigureAwait(false);

        var body = new CreateUsageRequest
        {
            Usage = new CreateUsage
            {
                Quantity = (double)quantity,
                Memo = memo
            }
        };

        try
        {
            var response = await _maxio.SubscriptionComponents
                .CreateUsage(subscriptionId, component.Id, body, ct: cancellationToken)
                .ConfigureAwait(false);

            return MapUsage(response.Usage, subscriptionId);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw From422(errors, nameof(RecordUsageAsync));
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, nameof(RecordUsageAsync));
            }

            throw Unrecognised(nameof(RecordUsageAsync));
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, nameof(RecordUsageAsync));
        }
        catch (Exception ex) when (IsUnreadableProviderFailure(ex, cancellationToken))
        {
            throw FromUnreadable(nameof(RecordUsageAsync), ex);
        }
    }

    public async Task<decimal> GetUsageTotalAsync(int subscriptionId,
        DateTimeOffset? since,
        CancellationToken cancellationToken = default)
    {
        var component = await ResolveMeteredComponentAsync(cancellationToken).ConfigureAwait(false);

        var total = decimal.Zero;
        var page = 1;

        // The provider exposes no period-to-date aggregate, so the total is summed client-side and
        // pagination is followed to completion — stopping at the first page would silently
        // under-report the customer's bill.
        while (true)
        {
            var currentPage = page;
            var usages = await InvokeAsync(nameof(GetUsageTotalAsync),
                ct => _maxio.SubscriptionComponents.ListUsages(subscriptionId,
                    component.Id,
                    sinceId: null,
                    maxId: null,
                    sinceDate: since,
                    untilDate: null,
                    page: currentPage,
                    perPage: PageSize,
                    ct: ct),
                cancellationToken).ConfigureAwait(false);

            if (usages.Count == 0)
            {
                break;
            }

            total = usages.Aggregate(total, (running, response) => running + ReadQuantity(response.Usage));

            if (usages.Count < PageSize)
            {
                break;
            }

            page++;
        }

        return total;
    }

    public async Task<decimal?> GetUsageUnitPriceAsync(CancellationToken cancellationToken = default)
    {
        var component = await ResolveMeteredComponentAsync(cancellationToken).ConfigureAwait(false);
        return component.UnitPrice;
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetSubscriptionAsync(subscriptionId, cancellationToken).ConfigureAwait(false)
            ?? throw new BillingProviderException(nameof(PreviewPlanChangeAsync), 404, $"Subscription {subscriptionId} was not found.");

        var targetPlan = await FindPlanByHandleAsync(targetPlanHandle, cancellationToken).ConfigureAwait(false)
            ?? throw new BillingConfigurationException($"Plan handle '{targetPlanHandle}' does not resolve to a plan.");

        // A change deferred to the next renewal is not prorated: the customer simply starts paying
        // the new plan's price from the next period, so there is nothing for the provider to price.
        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            return new PlanChangePreview(subscriptionId,
                subscription.PlanHandle ?? string.Empty,
                targetPlan.Handle,
                timing,
                proratedAdjustment: decimal.Zero,
                charge: decimal.Zero,
                paymentDue: decimal.Zero,
                creditApplied: decimal.Zero,
                targetPlanPrice: targetPlan.Price)
            {
                CurrentPlanName = subscription.PlanName,
                TargetPlanName = targetPlan.Name,
                EffectiveAt = subscription.CurrentPeriodEndsAt
            };
        }

        var body = new SubscriptionMigrationPreviewRequest
        {
            Migration = new SubscriptionMigrationPreviewOptions
            {
                ProductHandle = targetPlan.Handle
            }
        };

        try
        {
            var response = await _maxio.SubscriptionProducts
                .PreviewSubscriptionProductMigration(subscriptionId, body, ct: cancellationToken)
                .ConfigureAwait(false);

            var migration = response.Migration;

            return new PlanChangePreview(subscriptionId,
                subscription.PlanHandle ?? string.Empty,
                targetPlan.Handle,
                timing,
                proratedAdjustment: FromCents(migration.ProratedAdjustmentInCents),
                charge: FromCents(migration.ChargeInCents),
                paymentDue: FromCents(migration.PaymentDueInCents),
                creditApplied: FromCents(migration.CreditAppliedInCents),
                targetPlanPrice: targetPlan.Price)
            {
                CurrentPlanName = subscription.PlanName,
                TargetPlanName = targetPlan.Name
            };
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw From422(errors, nameof(PreviewPlanChangeAsync));
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, nameof(PreviewPlanChangeAsync));
            }

            throw Unrecognised(nameof(PreviewPlanChangeAsync));
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, nameof(PreviewPlanChangeAsync));
        }
        catch (Exception ex) when (IsUnreadableProviderFailure(ex, cancellationToken))
        {
            throw FromUnreadable(nameof(PreviewPlanChangeAsync), ex);
        }
    }

    public async Task<CustomerSubscription> ChangePlanAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        return timing == PlanChangeTiming.AtNextRenewal
            ? await SchedulePlanChangeAsync(subscriptionId, targetPlanHandle, cancellationToken).ConfigureAwait(false)
            : await MigratePlanNowAsync(subscriptionId, targetPlanHandle, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CustomerSubscription> MigratePlanNowAsync(int subscriptionId,
        string targetPlanHandle,
        CancellationToken cancellationToken)
    {
        var body = new SubscriptionProductMigrationRequest
        {
            Migration = new SubscriptionProductMigration
            {
                ProductHandle = targetPlanHandle,
                IncludeInitialCharge = false,
                PreservePeriod = false
            }
        };

        try
        {
            var response = await _maxio.SubscriptionProducts
                .MigrateSubscriptionProduct(subscriptionId, body, ct: cancellationToken)
                .ConfigureAwait(false);

            return MapSubscription(response.Subscription)
                ?? throw new BillingProviderException(nameof(ChangePlanAsync), 0, "The provider returned no subscription.");
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw From422(errors, nameof(ChangePlanAsync));
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, nameof(ChangePlanAsync));
            }

            throw Unrecognised(nameof(ChangePlanAsync));
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, nameof(ChangePlanAsync));
        }
        catch (Exception ex) when (IsUnreadableProviderFailure(ex, cancellationToken))
        {
            throw FromUnreadable(nameof(ChangePlanAsync), ex);
        }
    }

    private async Task<CustomerSubscription> SchedulePlanChangeAsync(int subscriptionId,
        string targetPlanHandle,
        CancellationToken cancellationToken)
    {
        var body = new UpdateSubscriptionRequest
        {
            Subscription = new UpdateSubscription
            {
                ProductHandle = targetPlanHandle,
                ProductChangeDelayed = true
            }
        };

        try
        {
            var response = await _maxio.Subscriptions
                .UpdateSubscription(subscriptionId, body, ct: cancellationToken)
                .ConfigureAwait(false);

            return MapSubscription(response.Subscription)
                ?? throw new BillingProviderException(nameof(ChangePlanAsync), 0, "The provider returned no subscription.");
        }
        catch (SdkException<UpdateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw From422(errors, nameof(ChangePlanAsync));
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, nameof(ChangePlanAsync));
            }

            throw Unrecognised(nameof(ChangePlanAsync));
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, nameof(ChangePlanAsync));
        }
        catch (Exception ex) when (IsUnreadableProviderFailure(ex, cancellationToken))
        {
            throw FromUnreadable(nameof(ChangePlanAsync), ex);
        }
    }

    public async Task<CustomerSubscription> PauseSubscriptionAsync(int subscriptionId,
        DateTimeOffset? automaticallyResumeAt,
        CancellationToken cancellationToken = default)
    {
        var body = automaticallyResumeAt.HasValue
            ? new PauseRequest { Hold = new AutoResume { AutomaticallyResumeAt = automaticallyResumeAt } }
            : null;

        try
        {
            var response = await _maxio.SubscriptionStatus
                .PauseSubscription(subscriptionId, body, ct: cancellationToken)
                .ConfigureAwait(false);

            return MapSubscription(response.Subscription)
                ?? throw new BillingProviderException(nameof(PauseSubscriptionAsync), 0, "The provider returned no subscription.");
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw From422(errors, nameof(PauseSubscriptionAsync));
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, nameof(PauseSubscriptionAsync));
            }

            throw Unrecognised(nameof(PauseSubscriptionAsync));
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, nameof(PauseSubscriptionAsync));
        }
        catch (Exception ex) when (IsUnreadableProviderFailure(ex, cancellationToken))
        {
            throw FromUnreadable(nameof(PauseSubscriptionAsync), ex);
        }
    }

    public async Task<CustomerSubscription> ResumeSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _maxio.SubscriptionStatus
                .ResumeSubscription(subscriptionId, calendarBillingResumptionCharge: null, ct: cancellationToken)
                .ConfigureAwait(false);

            return MapSubscription(response.Subscription)
                ?? throw new BillingProviderException(nameof(ResumeSubscriptionAsync), 0, "The provider returned no subscription.");
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw From422(errors, nameof(ResumeSubscriptionAsync));
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, nameof(ResumeSubscriptionAsync));
            }

            throw Unrecognised(nameof(ResumeSubscriptionAsync));
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, nameof(ResumeSubscriptionAsync));
        }
        catch (Exception ex) when (IsUnreadableProviderFailure(ex, cancellationToken))
        {
            throw FromUnreadable(nameof(ResumeSubscriptionAsync), ex);
        }
    }

    public async Task<CustomerSubscription> CancelSubscriptionAsync(int subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        return timing == CancellationTiming.EndOfPeriod
            ? await CancelAtEndOfPeriodAsync(subscriptionId, reason, cancellationToken).ConfigureAwait(false)
            : await CancelNowAsync(subscriptionId, reason, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CustomerSubscription> CancelNowAsync(int subscriptionId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var body = string.IsNullOrWhiteSpace(reason)
            ? null
            : new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = reason } };

        try
        {
            var response = await _maxio.SubscriptionStatus
                .CancelSubscription(subscriptionId, body, ct: cancellationToken)
                .ConfigureAwait(false);

            return MapSubscription(response.Subscription)
                ?? throw new BillingProviderException(nameof(CancelSubscriptionAsync), 0, "The provider returned no subscription.");
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            if (ex.Error.TryGetNoContent(out var missing))
            {
                throw FromRaw(missing, nameof(CancelSubscriptionAsync));
            }

            if (ex.Error.TryGetCancelSubscriptionErrorResponse(out var refused))
            {
                throw new BillingProviderException(nameof(CancelSubscriptionAsync), 422, DescribeCancellationError(refused));
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, nameof(CancelSubscriptionAsync));
            }

            throw Unrecognised(nameof(CancelSubscriptionAsync));
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, nameof(CancelSubscriptionAsync));
        }
        catch (Exception ex) when (IsUnreadableProviderFailure(ex, cancellationToken))
        {
            throw FromUnreadable(nameof(CancelSubscriptionAsync), ex);
        }
    }

    private async Task<CustomerSubscription> CancelAtEndOfPeriodAsync(int subscriptionId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var body = string.IsNullOrWhiteSpace(reason)
            ? null
            : new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = reason } };

        try
        {
            await _maxio.SubscriptionStatus
                .InitiateDelayedCancellation(subscriptionId, body, ct: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SdkException<InitiateDelayedCancellationError> ex)
        {
            if (ex.Error.TryGetNoContent(out var missing))
            {
                throw FromRaw(missing, nameof(CancelSubscriptionAsync));
            }

            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw From422(errors, nameof(CancelSubscriptionAsync));
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, nameof(CancelSubscriptionAsync));
            }

            throw Unrecognised(nameof(CancelSubscriptionAsync));
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, nameof(CancelSubscriptionAsync));
        }
        catch (Exception ex) when (IsUnreadableProviderFailure(ex, cancellationToken))
        {
            throw FromUnreadable(nameof(CancelSubscriptionAsync), ex);
        }

        // A delayed cancellation returns only a confirmation message, so the subscription is read
        // back to report the pending-cancellation state the caller needs to display.
        return await GetSubscriptionAsync(subscriptionId, cancellationToken).ConfigureAwait(false)
            ?? throw new BillingProviderException(nameof(CancelSubscriptionAsync), 404,
                $"Subscription {subscriptionId} could not be read back after scheduling its cancellation.");
    }

    public async Task<CustomerSubscription> ReactivateSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var body = new ReactivateSubscriptionRequest { Resume = true };

        try
        {
            var response = await _maxio.SubscriptionStatus
                .ReactivateSubscription(subscriptionId, body, ct: cancellationToken)
                .ConfigureAwait(false);

            return MapSubscription(response.Subscription)
                ?? throw new BillingProviderException(nameof(ReactivateSubscriptionAsync), 0, "The provider returned no subscription.");
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw From422(errors, nameof(ReactivateSubscriptionAsync));
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, nameof(ReactivateSubscriptionAsync));
            }

            throw Unrecognised(nameof(ReactivateSubscriptionAsync));
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, nameof(ReactivateSubscriptionAsync));
        }
        catch (Exception ex) when (IsUnreadableProviderFailure(ex, cancellationToken))
        {
            throw FromUnreadable(nameof(ReactivateSubscriptionAsync), ex);
        }
    }

    /// <summary>
    /// How the product family is addressed on operations that accept a string selector. The handle
    /// is preferred because the provider reassigns numeric ids whenever the catalog is re-seeded.
    /// </summary>
    private string ProductFamilySelector
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
            {
                return HandlePrefix + _settings.ProductFamilyHandle!.Trim();
            }

            if (_settings.ProductFamilyId.HasValue)
            {
                return _settings.ProductFamilyId.Value.ToString(CultureInfo.InvariantCulture);
            }

            throw new BillingConfigurationException(
                $"Neither '{MaxioSettings.SectionName}:ProductFamilyHandle' nor '{MaxioSettings.SectionName}:ProductFamilyId' is configured.");
        }
    }

    private IEnumerable<string> ConfiguredPlanHandles()
    {
        if (!string.IsNullOrWhiteSpace(_settings.DefaultProductHandle))
        {
            yield return _settings.DefaultProductHandle!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_settings.AlternateProductHandle))
        {
            yield return _settings.AlternateProductHandle!.Trim();
        }
    }

    /// <summary>
    /// Resolves the configured family handle to its live numeric id. The provider cannot look a
    /// family up by handle directly, so the list is matched client-side.
    /// </summary>
    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_resolvedProductFamilyId.HasValue)
        {
            return _resolvedProductFamilyId.Value;
        }

        await _catalogLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_resolvedProductFamilyId.HasValue)
            {
                return _resolvedProductFamilyId.Value;
            }

            var handle = _settings.ProductFamilyHandle?.Trim();
            if (string.IsNullOrWhiteSpace(handle))
            {
                _resolvedProductFamilyId = _settings.ProductFamilyId
                    ?? throw new BillingConfigurationException(
                        $"Neither '{MaxioSettings.SectionName}:ProductFamilyHandle' nor '{MaxioSettings.SectionName}:ProductFamilyId' is configured.");
                return _resolvedProductFamilyId.Value;
            }

            var families = await InvokeAsync(nameof(ResolveProductFamilyIdAsync),
                ct => _maxio.ProductFamilies.ListProductFamilies(dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct),
                cancellationToken).ConfigureAwait(false);

            var match = families
                .Select(response => response.ProductFamily)
                .FirstOrDefault(family => family is not null
                    && string.Equals(family.Handle, handle, StringComparison.OrdinalIgnoreCase));

            if (match?.Id is null)
            {
                throw new BillingConfigurationException(
                    $"Product family handle '{handle}' does not resolve to a product family on this Maxio site.");
            }

            _resolvedProductFamilyId = match.Id.Value;
            return _resolvedProductFamilyId.Value;
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    /// <summary>
    /// Resolves the configured usage component and proves it is metered before any usage is billed
    /// against it. A component of the wrong kind is a seeding mistake that cannot be corrected in
    /// place, so it is reported as a configuration error rather than a provider failure (UC2).
    /// </summary>
    private async Task<MeteredComponent> ResolveMeteredComponentAsync(CancellationToken cancellationToken)
    {
        if (_resolvedMeteredComponent is not null)
        {
            return _resolvedMeteredComponent;
        }

        var familyId = await ResolveProductFamilyIdAsync(cancellationToken).ConfigureAwait(false);

        await _catalogLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_resolvedMeteredComponent is not null)
            {
                return _resolvedMeteredComponent;
            }

            var handle = _settings.MeteredComponentHandle?.Trim();
            var selector = !string.IsNullOrWhiteSpace(handle)
                ? HandlePrefix + handle
                : _settings.MeteredComponentId?.ToString(CultureInfo.InvariantCulture)
                    ?? throw new BillingConfigurationException(
                        $"Neither '{MaxioSettings.SectionName}:MeteredComponentHandle' nor '{MaxioSettings.SectionName}:MeteredComponentId' is configured.");

            Component? component;
            try
            {
                var response = await InvokeAsync(nameof(ResolveMeteredComponentAsync),
                    ct => _maxio.Components.ReadComponent(familyId, selector, ct: ct),
                    cancellationToken).ConfigureAwait(false);
                component = response.Component;
            }
            catch (BillingProviderException ex) when (ex.IsNotFound)
            {
                throw new BillingConfigurationException(
                    $"Usage component '{selector}' does not exist on product family '{_settings.ProductFamilyHandle}'.");
            }

            if (component?.Id is null)
            {
                throw new BillingConfigurationException(
                    $"Usage component '{selector}' does not exist on product family '{_settings.ProductFamilyHandle}'.");
            }

            if (component.Kind != MaxioComponentKind.MeteredComponent)
            {
                throw new BillingConfigurationException(
                    $"Usage component '{selector}' is of kind '{DescribeComponentKind(component.Kind)}', not metered. " +
                    "A component's kind cannot be changed in place — archive it and recreate it as metered.");
            }

            _resolvedMeteredComponent = new MeteredComponent(component.Id.Value,
                component.Handle,
                DescribeComponentKind(component.Kind),
                ReadUnitPrice(component));

            return _resolvedMeteredComponent;
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    /// <summary>The configured usage component once it has been resolved and proven metered.</summary>
    private sealed record MeteredComponent(int Id, string? Handle, string? Kind, decimal? UnitPrice);

    /// <summary>
    /// Runs a provider call that reports failure as a bare error payload, translating both a refusal
    /// and an unreachable provider into <see cref="BillingProviderException"/>. A cancellation the
    /// caller asked for is deliberately allowed to propagate unchanged.
    /// </summary>
    private static async Task<T> InvokeAsync<T>(string operation,
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        try
        {
            return await call(cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, operation);
        }
        catch (Exception ex) when (IsUnreadableProviderFailure(ex, cancellationToken))
        {
            throw FromUnreadable(operation, ex);
        }
    }

    /// <summary>
    /// Failures that are not a structured provider refusal: the provider could not be reached, or it
    /// answered with something the SDK could not deserialise. A <see cref="JsonException"/> belongs
    /// here because an error body that does not match the shape the SDK expects surfaces as a
    /// deserialisation failure — and System.Text.Json types must not escape this seam any more than
    /// SDK types do. A cancellation the caller asked for is excluded, so it propagates unchanged.
    /// </summary>
    private static bool IsUnreadableProviderFailure(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && exception is HttpRequestException or OperationCanceledException or JsonException;

    private static BillingProviderException FromRaw(RawError error, string operation)
    {
        string body;
        try
        {
            body = error.ReadAsString();
        }
        catch (Exception)
        {
            body = string.Empty;
        }

        return new BillingProviderException(operation,
            (int)error.StatusCode,
            string.IsNullOrWhiteSpace(body) ? "The billing provider returned no details." : body);
    }

    private static BillingProviderException From422(ErrorListResponse1 errors, string operation) =>
        new(operation, 422,
            errors.Errors is { Count: > 0 }
                ? string.Join("; ", errors.Errors)
                : "The billing provider rejected the request.");

    private static BillingProviderException FromUnreadable(string operation, Exception inner) =>
        new(operation,
            BillingProviderException.NoStatusCode,
            inner is JsonException
                ? "The billing provider returned a response that could not be interpreted."
                : "The billing provider is unreachable.",
            inner);

    private static BillingProviderException Unrecognised(string operation) =>
        new(operation, BillingProviderException.NoStatusCode, "The billing provider returned an unrecognised error.");

    /// <summary>
    /// Best-effort description of a customer rejection. The generated 422 model carries only
    /// pagination-shaped fields, so anything usable is concatenated and an explanatory fallback is
    /// used otherwise — the message is never allowed to become an unhelpful framework default.
    /// </summary>
    private static string DescribeCustomerError(CustomerErrorResponse1 error)
    {
        var details = new List<string>();

        if (error.Errors?.PerPage is { Count: > 0 } perPage)
        {
            details.AddRange(perPage);
        }

        if (error.Errors?.PricePoint is { Count: > 0 } pricePoint)
        {
            details.AddRange(pricePoint);
        }

        return details.Count > 0
            ? string.Join("; ", details)
            : "The billing provider rejected the customer — a customer with this reference may already exist.";
    }

    private static string DescribeCancellationError(CancelSubscriptionErrorResponse error)
    {
        if (error.TryGetErrorListResponse1(out var list) && list.Errors is { Count: > 0 })
        {
            return string.Join("; ", list.Errors);
        }

        if (error.TryGetSingleErrorResponse1(out var single) && !string.IsNullOrWhiteSpace(single.Error))
        {
            return single.Error;
        }

        return "The billing provider refused to cancel the subscription.";
    }

    /// <summary>
    /// The collection method new subscriptions are created with. This integration captures no
    /// payment method, so it defaults to remittance: the first period becomes an open invoice
    /// instead of an immediate charge the provider would refuse for want of a payment profile.
    /// </summary>
    private CollectionMethod ResolveCollectionMethod()
    {
        var configured = _settings.PaymentCollectionMethod?.Trim();

        if (string.IsNullOrEmpty(configured))
        {
            return CollectionMethod.Remittance;
        }

        if (configured.Equals("automatic", StringComparison.OrdinalIgnoreCase)) return CollectionMethod.Automatic;
        if (configured.Equals("invoice", StringComparison.OrdinalIgnoreCase)) return CollectionMethod.Invoice;
        if (configured.Equals("prepaid", StringComparison.OrdinalIgnoreCase)) return CollectionMethod.Prepaid;
        if (configured.Equals("remittance", StringComparison.OrdinalIgnoreCase)) return CollectionMethod.Remittance;

        throw new BillingConfigurationException(
            $"'{MaxioSettings.SectionName}:PaymentCollectionMethod' is '{configured}', which is not one of " +
            "'remittance', 'automatic', 'invoice' or 'prepaid'.");
    }

    /// <summary>Converts an integer-cent amount to whole currency units. This is the only place cents exist.</summary>
    private static decimal FromCents(long? cents) => cents.HasValue ? cents.Value / 100m : decimal.Zero;

    private static decimal? ReadUnitPrice(Component component)
    {
        if (!string.IsNullOrWhiteSpace(component.UnitPrice)
            && decimal.TryParse(component.UnitPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return component.PricePerUnitInCents.HasValue ? FromCents(component.PricePerUnitInCents) : null;
    }

    /// <summary>Reads a usage quantity, which the provider may send as either a JSON number or a string.</summary>
    private static decimal ReadQuantity(Usage usage)
    {
        if (usage.Quantity is null)
        {
            return decimal.Zero;
        }

        if (usage.Quantity.TryGetInt(out var whole))
        {
            return whole;
        }

        if (usage.Quantity.TryGetString(out var text)
            && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return decimal.Zero;
    }

    private static SubscriptionPlan? MapPlan(Product product)
    {
        if (product.Id is null || string.IsNullOrEmpty(product.Handle) || string.IsNullOrEmpty(product.Name))
        {
            return null;
        }

        return new SubscriptionPlan(product.Id.Value,
            product.Handle!,
            product.Name!,
            FromCents(product.PriceInCents),
            product.Interval ?? 1,
            MapIntervalUnit(product.IntervalUnit),
            product.Description);
    }

    /// <summary>
    /// The provider's own wire name for a component kind. The SDK's enum types render as a record
    /// (<c>ComponentKind { Value = … }</c>), which is noise in an operator-facing diagnostic, so the
    /// wire value is reported directly.
    /// </summary>
    private static string DescribeComponentKind(MaxioComponentKind? kind)
    {
        if (kind is null) return "unknown";
        if (kind == MaxioComponentKind.MeteredComponent) return "metered_component";
        if (kind == MaxioComponentKind.QuantityBasedComponent) return "quantity_based_component";
        if (kind == MaxioComponentKind.OnOffComponent) return "on_off_component";
        if (kind == MaxioComponentKind.PrepaidUsageComponent) return "prepaid_usage_component";
        if (kind == MaxioComponentKind.EventBasedComponent) return "event_based_component";
        return "unrecognised";
    }

    private static BillingIntervalUnit MapIntervalUnit(MaxioIntervalUnit? unit) =>
        unit == MaxioIntervalUnit.Day ? BillingIntervalUnit.Day : BillingIntervalUnit.Month;

    private static BillingCustomer? MapCustomer(Customer? customer, string fallbackReference)
    {
        if (customer?.Id is null)
        {
            return null;
        }

        return new BillingCustomer(customer.Id.Value,
            string.IsNullOrEmpty(customer.Reference) ? fallbackReference : customer.Reference!,
            customer.Email ?? string.Empty,
            customer.FirstName,
            customer.LastName);
    }

    private static CustomerSubscription? MapSubscription(Subscription? subscription)
    {
        if (subscription?.Id is null)
        {
            return null;
        }

        var customer = subscription.Customer;
        var product = subscription.Product;

        return new CustomerSubscription(subscription.Id.Value,
            MapState(subscription.State),
            customer?.Reference ?? customer?.Email ?? string.Empty,
            customer?.Id ?? 0)
        {
            CustomerEmail = customer?.Email,
            PlanId = product?.Id,
            PlanHandle = product?.Handle,
            PlanName = product?.Name,
            PlanPrice = FromCents(subscription.ProductPriceInCents ?? product?.PriceInCents),
            Currency = subscription.Currency,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod ?? false,
            ScheduledCancellationAt = subscription.ScheduledCancellationAt ?? subscription.DelayedCancelAt,
            OnHoldAt = subscription.OnHoldAt,
            AutomaticallyResumeAt = subscription.AutomaticallyResumeAt,
            PendingPlanId = subscription.NextProductId,
            PendingPlanHandle = subscription.NextProductHandle
        };
    }

    private static UsageRecord MapUsage(Usage usage, int subscriptionId) =>
        new(usage.Id ?? 0, usage.SubscriptionId ?? subscriptionId, ReadQuantity(usage))
        {
            Memo = usage.Memo,
            RecordedAt = usage.CreatedAt,
            ComponentId = usage.ComponentId,
            ComponentHandle = usage.ComponentHandle
        };

    /// <summary>
    /// Maps the provider's subscription state onto the domain's. The provider's states are string
    /// values that round-trip unknown members rather than throwing, so an unrecognised state becomes
    /// <see cref="SubscriptionState.Unknown"/> — from which no transition is ever considered legal.
    /// </summary>
    private static SubscriptionState MapState(MaxioSubscriptionState? state)
    {
        if (state is null)
        {
            return SubscriptionState.Unknown;
        }

        if (state == MaxioSubscriptionState.Active) return SubscriptionState.Active;
        if (state == MaxioSubscriptionState.Trialing) return SubscriptionState.Trialing;
        if (state == MaxioSubscriptionState.Pending) return SubscriptionState.Pending;
        if (state == MaxioSubscriptionState.AwaitingSignup) return SubscriptionState.AwaitingSignup;
        if (state == MaxioSubscriptionState.Assessing) return SubscriptionState.Assessing;
        if (state == MaxioSubscriptionState.SoftFailure) return SubscriptionState.SoftFailure;
        if (state == MaxioSubscriptionState.PastDue) return SubscriptionState.PastDue;
        if (state == MaxioSubscriptionState.Suspended) return SubscriptionState.Suspended;
        if (state == MaxioSubscriptionState.Canceled) return SubscriptionState.Canceled;
        if (state == MaxioSubscriptionState.Expired) return SubscriptionState.Expired;
        if (state == MaxioSubscriptionState.Paused) return SubscriptionState.Paused;
        if (state == MaxioSubscriptionState.Unpaid) return SubscriptionState.Unpaid;
        if (state == MaxioSubscriptionState.TrialEnded) return SubscriptionState.TrialEnded;
        if (state == MaxioSubscriptionState.OnHold) return SubscriptionState.OnHold;
        if (state == MaxioSubscriptionState.FailedToCreate) return SubscriptionState.FailedToCreate;

        return SubscriptionState.Unknown;
    }

    /// <summary>
    /// Derives the given/family name the provider requires from the eShopOnWeb identity, which is an
    /// email address. Deterministic, so the same user always produces the same customer record.
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(string email, string userReference)
    {
        var source = string.IsNullOrWhiteSpace(email) ? userReference : email;
        var localPart = source.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var first = parts.Length > 0 ? Capitalise(parts[0]) : "eShop";
        var last = parts.Length > 1 ? Capitalise(parts[1]) : "Customer";

        return (first, last);
    }

    private static string Capitalise(string value) =>
        value.Length <= 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value[1..];
}
