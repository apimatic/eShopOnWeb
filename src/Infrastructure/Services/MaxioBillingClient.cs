using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Maxio = MaxioAdvancedBilling.Models;
using MaxioEnums = MaxioAdvancedBilling.Models.Enums;
using MaxioUnions = MaxioAdvancedBilling.Models.AnyOf;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The one and only place eShopOnWeb talks to Maxio Advanced Billing (plan.md §2.2/§4.2).
/// </summary>
/// <remarks>
/// <para>
/// Everything provider-specific is confined here: the SDK client, wire enums, money units, envelope
/// shapes and error payloads. Callers see only <see cref="IBillingClient"/> and the domain types, and
/// every failure is normalized into <see cref="BillingProviderException"/> or
/// <see cref="BillingConfigurationException"/>.
/// </para>
/// <para>
/// The outbound base URL comes from <see cref="MaxioSettings.ResolveBaseUrl"/> — an explicit
/// <c>Maxio:BaseUrl</c> wins verbatim, otherwise the host is derived from the subdomain and region — so
/// the same build can target production, a dev/sandbox tenant, or a local mock server through
/// configuration alone (plan.md §2.3).
/// </para>
/// <para>
/// Provider-assigned numeric ids are resolved from durable handles on demand and memoized for the
/// lifetime of this instance (one request), because ids are reassigned whenever the catalog is re-seeded.
/// </para>
/// </remarks>
public class MaxioBillingClient : IBillingClient
{
    /// <summary>
    /// Maxio authenticates the site API key as the HTTP Basic <em>username</em>; the password is the
    /// fixed literal <c>x</c>. It is a protocol constant, not a credential.
    /// </summary>
    private const string ApiKeyBasicAuthPassword = "x";

    /// <summary>Page size used when paging provider list endpoints.</summary>
    private const int PageSize = 100;

    /// <summary>Upper bound on pages walked, so a paging bug can never loop forever.</summary>
    private const int MaxPages = 50;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    private int? _resolvedProductFamilyId;
    private MeteredComponent? _resolvedComponent;

    public MaxioBillingClient(
        HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings.Value ?? throw new BillingConfigurationException(
            $"The '{MaxioSettings.SectionName}' configuration section is missing.");
        _settings.Validate();
        _logger = logger;
        _client = new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(_settings));
    }

    /// <summary>
    /// Builds the SDK options. The resolved base URL is applied to both regional server entries so the
    /// override is honored whichever region is selected.
    /// </summary>
    private static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioSettings settings)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = ApiKeyBasicAuthPassword
            },
            Environment = settings.IsEuropeanRegion ? ServerEnvironment.Eu : ServerEnvironment.Us
        };

        var subdomain = settings.Subdomain?.Trim() ?? string.Empty;
        options.Server.Production.Us.Site = subdomain;
        options.Server.Production.Eu.Site = subdomain;

        // ResolveBaseUrl() has already applied the "explicit override wins, else derive from subdomain"
        // rule, so the value assigned here is always the final host.
        var baseUrl = settings.ResolveBaseUrl();
        options.Server.Production.Us.BaseUrl = baseUrl;
        options.Server.Production.Eu.BaseUrl = baseUrl;

        return options;
    }

    // ---------------------------------------------------------------------------------------------
    // Catalog
    // ---------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(
        CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken).ConfigureAwait(false);

        var responses = await InvokeAsync(
            () => _client.ProductFamilies.ListProductsForProductFamily(
                familyId.ToString(CultureInfo.InvariantCulture),
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
                ct: cancellationToken),
            "list plans",
            (ListProductsForProductFamilyError error) => Describe(error),
            cancellationToken).ConfigureAwait(false);

        return responses
            .Select(response => MapPlan(response.Product))
            .OfType<SubscriptionPlan>()
            .OrderByDescending(plan => plan.PriceInCents)
            .ToList();
    }

    public async Task<SubscriptionPlan?> FindPlanAsync(
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        var handle = planHandle.Trim();

        Maxio.ProductResponse response;
        try
        {
            response = await InvokeAsync(
                () => _client.Products.ReadProductByHandle(handle, ct: cancellationToken),
                $"resolve plan '{handle}'",
                cancellationToken).ConfigureAwait(false);
        }
        catch (BillingProviderException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }

        var plan = MapPlan(response.Product);

        // A plan outside the configured product family is not part of this integration's catalog and
        // must never be enrolled against, even if the handle happens to exist on the site.
        if (plan is not null &&
            !string.IsNullOrEmpty(plan.ProductFamilyHandle) &&
            !string.Equals(plan.ProductFamilyHandle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Plan '{0}' belongs to product family '{1}', not the configured '{2}'; ignoring it.",
                handle, plan.ProductFamilyHandle, _settings.ProductFamilyHandle);
            return null;
        }

        return plan;
    }

    public async Task<MeteredComponent> GetMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        if (_resolvedComponent is not null)
        {
            return _resolvedComponent;
        }

        var handle = _settings.MeteredComponentHandle.Trim();

        Maxio.ComponentResponse response;
        try
        {
            response = await InvokeAsync(
                () => _client.Components.FindComponent(handle, ct: cancellationToken),
                $"resolve usage component '{handle}'",
                cancellationToken).ConfigureAwait(false);
        }
        catch (BillingProviderException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            throw BillingConfigurationException.UnresolvedHandle("usage component", handle);
        }

        var component = response.Component;
        var kind = component.Kind?.Value;

        _resolvedComponent = new MeteredComponent
        {
            Handle = component.Handle ?? handle,
            ProviderId = component.Id,
            Name = component.Name ?? handle,
            Kind = kind,
            IsMetered = string.Equals(
                kind, MaxioEnums.ComponentKind.MeteredComponent.Value, StringComparison.OrdinalIgnoreCase),
            PricingScheme = component.PricingScheme?.Value,
            UnitPriceInCents = component.PricePerUnitInCents ?? DollarsToCents(component.UnitPrice),
            UnitName = component.UnitName
        };

        return _resolvedComponent;
    }

    // ---------------------------------------------------------------------------------------------
    // Customers
    // ---------------------------------------------------------------------------------------------

    public async Task<BillingCustomer?> FindCustomerAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var key = reference.Trim();

        Maxio.CustomerResponse response;
        try
        {
            response = await InvokeAsync(
                () => _client.Customers.ReadCustomerByReference(key, ct: cancellationToken),
                "look up billing customer",
                cancellationToken).ConfigureAwait(false);
        }
        catch (BillingProviderException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }

        // A response without a usable id means the provider has no customer for this reference.
        return response.Customer?.Id is null ? null : MapCustomer(response.Customer);
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(
        BillingCustomerRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var existing = await FindCustomerAsync(registration.Reference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        Maxio.CustomerResponse response;
        try
        {
            response = await InvokeAsync(
                () => _client.Customers.CreateCustomer(
                    new Maxio.CreateCustomerRequest
                    {
                        Customer = new Maxio.CreateCustomer
                        {
                            FirstName = registration.FirstName,
                            LastName = registration.LastName,
                            Email = registration.Email,
                            Reference = registration.Reference
                        }
                    },
                    ct: cancellationToken),
                "create billing customer",
                (CreateCustomerError error) => Describe(error),
                cancellationToken).ConfigureAwait(false);
        }
        catch (BillingProviderException)
        {
            // The reference is unique per customer, so a rejected create most often means a concurrent
            // request won the race. Re-read once before surfacing the failure — this is deliberately not
            // conditioned on the status, because the provider's rejection body is not reliably typed.
            var raced = await FindCustomerAsync(registration.Reference, cancellationToken)
                .ConfigureAwait(false);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }

        if (response.Customer?.Id is null)
        {
            throw new BillingProviderException(
                "create billing customer",
                null,
                "The billing provider accepted the customer but returned no customer id.");
        }

        return MapCustomer(response.Customer);
    }

    // ---------------------------------------------------------------------------------------------
    // Subscriptions
    // ---------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var responses = await InvokeAsync(
            () => _client.Customers.ListCustomerSubscriptions(customerId, ct: cancellationToken),
            "list customer subscriptions",
            cancellationToken).ConfigureAwait(false);

        return responses
            .Select(response => response.Subscription)
            .OfType<Maxio.Subscription>()
            .Select(MapSubscription)
            .ToList();
    }

    public async Task<Subscription?> GetSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await InvokeAsync(
                () => _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: cancellationToken),
                "read subscription",
                cancellationToken).ConfigureAwait(false);

            return response.Subscription is null ? null : MapSubscription(response.Subscription);
        }
        catch (BillingProviderException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Subscription> CreateSubscriptionAsync(
        int customerId,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(
            () => _client.Subscriptions.CreateSubscription(
                new Maxio.CreateSubscriptionRequest
                {
                    Subscription = new Maxio.CreateSubscription
                    {
                        CustomerId = customerId,
                        ProductHandle = planHandle,

                        // eShopOnWeb never captures or stores card details, so the subscription is billed
                        // by remittance (invoice) rather than automatically against a stored payment
                        // method. Without this the provider refuses to open a subscription that carries an
                        // immediate balance, because no payment profile exists to charge.
                        PaymentCollectionMethod = MaxioEnums.CollectionMethod.Remittance
                    }
                },
                ct: cancellationToken),
            "create subscription",
            (CreateSubscriptionError error) => Describe(error),
            cancellationToken).ConfigureAwait(false);

        return RequireSubscription(response.Subscription, "create subscription");
    }

    // ---------------------------------------------------------------------------------------------
    // Pay-as-you-go usage
    // ---------------------------------------------------------------------------------------------

    public async Task<UsageRecord> RecordUsageAsync(
        int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0m)
        {
            throw new InvalidUsageQuantityException(quantity);
        }

        var component = await RequireMeteredComponentAsync(cancellationToken).ConfigureAwait(false);
        var componentId = RequireComponentId(component);

        var response = await InvokeAsync(
            () => _client.SubscriptionComponents.CreateUsage(
                MaxioUnions.SubscriptionIdOrReference.Int(subscriptionId),
                MaxioUnions.ComponentIdModel.Int(componentId),
                new Maxio.CreateUsageRequest
                {
                    Usage = new Maxio.CreateUsage
                    {
                        Quantity = (double)quantity,
                        Memo = memo
                    }
                },
                ct: cancellationToken),
            "record usage",
            (CreateUsageError error) => Describe(error),
            cancellationToken).ConfigureAwait(false);

        return MapUsage(response.Usage, subscriptionId, quantity, memo, component.Handle);
    }

    public async Task<decimal?> GetPeriodToDateUsageAsync(
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var component = await RequireMeteredComponentAsync(cancellationToken).ConfigureAwait(false);
        var componentId = RequireComponentId(component);

        try
        {
            var response = await InvokeAsync(
                () => _client.SubscriptionComponents.ReadSubscriptionComponent(
                    subscriptionId, componentId, ct: cancellationToken),
                "read period-to-date usage",
                (ReadSubscriptionComponentError error) => Describe(error),
                cancellationToken).ConfigureAwait(false);

            if (response.Component?.UnitBalance is { } unitBalance)
            {
                return unitBalance;
            }
        }
        catch (BillingProviderException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            // The component has not been touched on this subscription yet; fall through to the usage log.
            _logger.LogInformation(
                "Subscription {0} has no allocation for component {1} yet; summing recorded usage instead.",
                subscriptionId, component.Handle);
        }

        return await SumRecordedUsageAsync(subscriptionId, componentId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fallback period-to-date total: walk the usage log and sum it. Paging is explicit because the SDK
    /// exposes no auto-paginating variant, and a single unpaged read would silently undercount.
    /// </summary>
    private async Task<decimal?> SumRecordedUsageAsync(
        int subscriptionId,
        int componentId,
        CancellationToken cancellationToken)
    {
        decimal total = 0m;

        for (var page = 1; page <= MaxPages; page++)
        {
            var pageNumber = page;
            var usages = await InvokeAsync(
                () => _client.SubscriptionComponents.ListUsages(
                    MaxioUnions.SubscriptionIdOrReference.Int(subscriptionId),
                    MaxioUnions.ComponentIdModel.Int(componentId),
                    sinceId: null,
                    maxId: null,
                    sinceDate: null,
                    untilDate: null,
                    page: pageNumber,
                    perPage: PageSize,
                    ct: cancellationToken),
                "list recorded usage",
                cancellationToken).ConfigureAwait(false);

            foreach (var usage in usages)
            {
                total += ReadQuantity(usage.Usage.Quantity) ?? 0m;
            }

            if (usages.Count < PageSize)
            {
                return total;
            }
        }

        _logger.LogWarning(
            "Stopped summing usage for subscription {0} after {1} pages; the total may be incomplete.",
            subscriptionId, MaxPages);

        return total;
    }

    // ---------------------------------------------------------------------------------------------
    // Plan change
    // ---------------------------------------------------------------------------------------------

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(
        int subscriptionId,
        string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(
            () => _client.SubscriptionProducts.PreviewSubscriptionProductMigration(
                subscriptionId,
                new Maxio.SubscriptionMigrationPreviewRequest
                {
                    Migration = new Maxio.SubscriptionMigrationPreviewOptions
                    {
                        ProductHandle = targetPlanHandle,
                        IncludeTrial = false,
                        IncludeInitialCharge = false,
                        IncludeCoupons = true,
                        PreservePeriod = false
                    }
                },
                ct: cancellationToken),
            "preview plan change",
            (PreviewSubscriptionProductMigrationError error) => Describe(error),
            cancellationToken).ConfigureAwait(false);

        var migration = response.Migration;

        return new PlanChangePreview
        {
            SubscriptionId = subscriptionId,
            TargetPlanHandle = targetPlanHandle,
            ChargeInCents = migration.ChargeInCents ?? 0L,
            CreditAppliedInCents = migration.CreditAppliedInCents ?? 0L,
            PaymentDueInCents = migration.PaymentDueInCents ?? 0L,
            ProratedAdjustmentInCents = migration.ProratedAdjustmentInCents ?? 0L,
            PreviewedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<Subscription> ChangePlanImmediatelyAsync(
        int subscriptionId,
        string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(
            () => _client.SubscriptionProducts.MigrateSubscriptionProduct(
                subscriptionId,
                new Maxio.SubscriptionProductMigrationRequest
                {
                    // The same selector and flags the preview used, so the committed amount matches it.
                    Migration = new Maxio.SubscriptionProductMigration
                    {
                        ProductHandle = targetPlanHandle,
                        IncludeTrial = false,
                        IncludeInitialCharge = false,
                        IncludeCoupons = true,
                        PreservePeriod = false
                    }
                },
                ct: cancellationToken),
            "change plan",
            (MigrateSubscriptionProductError error) => Describe(error),
            cancellationToken).ConfigureAwait(false);

        return RequireSubscription(response.Subscription, "change plan");
    }

    public async Task<Subscription> SchedulePlanChangeAsync(
        int subscriptionId,
        string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(
            () => _client.Subscriptions.UpdateSubscription(
                subscriptionId,
                new Maxio.UpdateSubscriptionRequest
                {
                    Subscription = new Maxio.UpdateSubscription
                    {
                        ProductHandle = targetPlanHandle,

                        // Defers the change to the next renewal, so no proration is charged.
                        ProductChangeDelayed = true
                    }
                },
                ct: cancellationToken),
            "schedule plan change",
            (UpdateSubscriptionError error) => Describe(error),
            cancellationToken).ConfigureAwait(false);

        return RequireSubscription(response.Subscription, "schedule plan change");
    }

    // ---------------------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------------------

    public async Task<Subscription> PauseSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(
            // A null body holds the subscription indefinitely, with no automatic resume date.
            () => _client.SubscriptionStatus.PauseSubscription(subscriptionId, null, ct: cancellationToken),
            "pause subscription",
            (PauseSubscriptionError error) => Describe(error),
            cancellationToken).ConfigureAwait(false);

        return RequireSubscription(response.Subscription, "pause subscription");
    }

    public async Task<Subscription> ResumeSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(
            () => _client.SubscriptionStatus.ResumeSubscription(subscriptionId, null, ct: cancellationToken),
            "resume subscription",
            (ResumeSubscriptionError error) => Describe(error),
            cancellationToken).ConfigureAwait(false);

        return RequireSubscription(response.Subscription, "resume subscription");
    }

    public async Task<Subscription> CancelSubscriptionAsync(
        int subscriptionId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(
            () => _client.SubscriptionStatus.CancelSubscription(
                subscriptionId, BuildCancellationRequest(reason), ct: cancellationToken),
            "cancel subscription",
            (CancelSubscriptionApiError error) => Describe(error),
            cancellationToken).ConfigureAwait(false);

        return RequireSubscription(response.Subscription, "cancel subscription");
    }

    public async Task<Subscription> CancelSubscriptionAtPeriodEndAsync(
        int subscriptionId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        await InvokeAsync(
            () => _client.SubscriptionStatus.InitiateDelayedCancellation(
                subscriptionId, BuildCancellationRequest(reason), ct: cancellationToken),
            "schedule end-of-period cancellation",
            (InitiateDelayedCancellationError error) => Describe(error),
            cancellationToken).ConfigureAwait(false);

        // The delayed-cancellation endpoint answers with a message, not a subscription, so the caller's
        // view is refreshed from the provider rather than assumed.
        return await GetSubscriptionAsync(subscriptionId, cancellationToken).ConfigureAwait(false)
            ?? throw new BillingProviderException(
                "schedule end-of-period cancellation",
                null,
                $"Subscription {subscriptionId} could not be re-read after scheduling its cancellation.");
    }

    public async Task<Subscription> ReactivateSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(
            () => _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, null, ct: cancellationToken),
            "reactivate subscription",
            (ReactivateSubscriptionError error) => Describe(error),
            cancellationToken).ConfigureAwait(false);

        return RequireSubscription(response.Subscription, "reactivate subscription");
    }

    private static Maxio.CancellationRequest BuildCancellationRequest(string? reason) => new()
    {
        Subscription = new Maxio.CancellationOptions
        {
            CancellationMessage = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
        }
    };

    // ---------------------------------------------------------------------------------------------
    // Handle resolution
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves the product family by its durable handle. The configured numeric id is only used when the
    /// handle cannot be found, because ids are reassigned on a re-seed while handles are stable.
    /// </summary>
    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_resolvedProductFamilyId is { } cached)
        {
            return cached;
        }

        var handle = _settings.ProductFamilyHandle.Trim();

        var families = await InvokeAsync(
            () => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: cancellationToken),
            "list product families",
            cancellationToken).ConfigureAwait(false);

        var match = families
            .Select(response => response.ProductFamily)
            .FirstOrDefault(family =>
                family?.Id is not null &&
                string.Equals(family.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is { } resolved)
        {
            _resolvedProductFamilyId = resolved;
            return resolved;
        }

        if (_settings.ProductFamilyId is { } configured)
        {
            _logger.LogWarning(
                "Product family handle '{0}' did not resolve; falling back to the configured id {1}.",
                handle, configured);
            _resolvedProductFamilyId = configured;
            return configured;
        }

        throw BillingConfigurationException.UnresolvedHandle("product family", handle);
    }

    /// <summary>
    /// Resolves the usage component and refuses to go further unless it is metered — a non-metered
    /// component cannot accrue per-unit consumption (plan.md UC2 preconditions).
    /// </summary>
    private async Task<MeteredComponent> RequireMeteredComponentAsync(CancellationToken cancellationToken)
    {
        var component = await GetMeteredComponentAsync(cancellationToken).ConfigureAwait(false);
        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"The configured usage component '{component.Handle}' is of kind " +
                $"'{component.Kind ?? "unknown"}', not metered, so usage cannot be recorded. " +
                "Archive it and recreate it as metered (see plan.md UC0).");
        }

        return component;
    }

    private static int RequireComponentId(MeteredComponent component) =>
        component.ProviderId ?? throw new BillingConfigurationException(
            $"The billing provider did not report an id for usage component '{component.Handle}'.");

    // ---------------------------------------------------------------------------------------------
    // Mapping
    // ---------------------------------------------------------------------------------------------

    private static SubscriptionPlan? MapPlan(Maxio.Product? product)
    {
        if (product?.Handle is null || product.ArchivedAt is not null)
        {
            return null;
        }

        return new SubscriptionPlan
        {
            Handle = product.Handle,
            ProviderId = product.Id,
            Name = product.Name ?? product.Handle,
            Description = product.Description,
            PriceInCents = product.PriceInCents ?? 0L,
            Interval = product.Interval ?? 0,
            IntervalUnit = product.IntervalUnit?.Value ?? string.Empty,
            RequiresPaymentMethod = product.RequireCreditCard ?? false,
            ProductFamilyHandle = product.ProductFamily?.Handle
        };
    }

    private static BillingCustomer MapCustomer(Maxio.Customer customer) => new()
    {
        Id = customer.Id!.Value,
        Reference = customer.Reference ?? customer.Email ?? string.Empty,
        Email = customer.Email,
        FirstName = customer.FirstName,
        LastName = customer.LastName
    };

    private static Subscription MapSubscription(Maxio.Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        CustomerId = subscription.Customer?.Id,
        CustomerReference = subscription.Customer?.Reference,
        State = MapState(subscription.State?.Value, subscription.OnHoldAt),
        ProviderState = subscription.State?.Value,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PlanPriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0L,
        BalanceInCents = subscription.BalanceInCents ?? 0L,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod ?? false,
        ScheduledCancellationAt = subscription.ScheduledCancellationAt ?? subscription.DelayedCancelAt,
        CanceledAt = subscription.CanceledAt,
        ScheduledPlanHandle = subscription.NextProductHandle,
        PausedAt = subscription.OnHoldAt,
        AutomaticallyResumeAt = subscription.AutomaticallyResumeAt
    };

    /// <summary>
    /// Normalizes the provider's state string. A held subscription is reported as either <c>paused</c> or
    /// <c>on_hold</c> depending on the endpoint, so both map to <see cref="SubscriptionState.Paused"/>,
    /// and an unrecognized state with a hold timestamp is treated as paused rather than unknown.
    /// </summary>
    private static SubscriptionState MapState(string? providerState, DateTimeOffset? onHoldAt)
    {
        var state = providerState switch
        {
            "active" or "assessing" => SubscriptionState.Active,
            "trialing" => SubscriptionState.Trialing,
            "past_due" => SubscriptionState.PastDue,
            "paused" or "on_hold" => SubscriptionState.Paused,
            "canceled" or "cancelled" => SubscriptionState.Canceled,
            "expired" => SubscriptionState.Expired,
            "trial_ended" => SubscriptionState.TrialEnded,
            "unpaid" => SubscriptionState.Unpaid,
            "suspended" => SubscriptionState.Suspended,
            "pending" or "awaiting_signup" => SubscriptionState.Pending,
            "failed_to_create" or "soft_failure" => SubscriptionState.Failed,
            _ => SubscriptionState.Unknown
        };

        return state == SubscriptionState.Unknown && onHoldAt is not null
            ? SubscriptionState.Paused
            : state;
    }

    private static UsageRecord MapUsage(
        Maxio.Usage usage,
        int subscriptionId,
        decimal requestedQuantity,
        string? requestedMemo,
        string componentHandle) => new()
        {
            Id = usage.Id,
            SubscriptionId = usage.SubscriptionId ?? subscriptionId,
            ComponentHandle = usage.ComponentHandle ?? componentHandle,
            Quantity = ReadQuantity(usage.Quantity) ?? requestedQuantity,
            Memo = usage.Memo ?? requestedMemo,
            RecordedAt = usage.CreatedAt
        };

    /// <summary>Reads a usage quantity, which the provider models as either a number or a string.</summary>
    private static decimal? ReadQuantity(MaxioUnions.Quantity1? quantity)
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

    /// <summary>
    /// Converts a dollar-denominated decimal string (for example <c>"0.01"</c>) into whole cents.
    /// The provider reports component unit prices this way when it has no <c>_in_cents</c> sibling.
    /// </summary>
    private static long? DollarsToCents(string? dollars)
    {
        if (string.IsNullOrWhiteSpace(dollars) ||
            !decimal.TryParse(dollars, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return (long)decimal.Round(value * 100m, 0, MidpointRounding.AwayFromZero);
    }

    private static Subscription RequireSubscription(Maxio.Subscription? subscription, string operation) =>
        subscription is null
            ? throw new BillingProviderException(
                operation, null, $"The billing provider returned no subscription for '{operation}'.")
            : MapSubscription(subscription);

    // ---------------------------------------------------------------------------------------------
    // Error translation
    // ---------------------------------------------------------------------------------------------

    /// <summary>A normalized provider failure: the HTTP status, when known, and a safe description.</summary>
    private readonly record struct ProviderFailure(int? StatusCode, string? Message);

    /// <summary>Invokes an operation whose only failure type is the untyped provider error.</summary>
    private async Task<TResult> InvokeAsync<TResult>(
        Func<Task<TResult>> call,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(operation, FromRaw(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            throw Unreadable(operation, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(operation, ex);
        }
    }

    /// <summary>
    /// Invokes an operation that also has a typed error payload. <paramref name="describe"/> reads the
    /// operation-specific accessors, which live on the concrete error type and cannot be reached generically.
    /// </summary>
    private async Task<TResult> InvokeAsync<TResult, TError>(
        Func<Task<TResult>> call,
        string operation,
        Func<TError, ProviderFailure> describe,
        CancellationToken cancellationToken)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (SdkException<TError> ex)
        {
            throw Translate(operation, describe(ex.Error), ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(operation, FromRaw(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            throw Unreadable(operation, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(operation, ex);
        }
    }

    /// <summary>
    /// True for connection-level failures, which never surface as a provider error. A cancellation the
    /// caller actually requested is deliberately excluded so it propagates unchanged.
    /// </summary>
    private static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException ||
        (exception is TaskCanceledException or OperationCanceledException &&
         !cancellationToken.IsCancellationRequested);

    private BillingProviderException Translate(string operation, ProviderFailure failure, Exception inner)
    {
        _logger.LogWarning(
            "Maxio operation '{0}' failed with status {1}: {2}",
            operation,
            failure.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "none",
            failure.Message ?? "no detail supplied");

        var message = string.IsNullOrWhiteSpace(failure.Message)
            ? $"The billing provider rejected the request to {operation}."
            : $"The billing provider rejected the request to {operation}: {failure.Message}";

        return new BillingProviderException(operation, failure.StatusCode, message, inner);
    }

    /// <summary>
    /// The provider answered, but its body did not match the shape the SDK models — which happens for
    /// error payloads in particular. Callers still get one typed billing failure rather than a raw
    /// deserialization exception escaping the seam.
    /// </summary>
    private BillingProviderException Unreadable(string operation, Exception inner)
    {
        _logger.LogWarning(
            "Maxio returned a response for '{0}' that could not be interpreted: {1}", operation, inner.Message);

        return new BillingProviderException(
            operation,
            null,
            $"The billing provider returned an unexpected response while trying to {operation}.",
            inner);
    }

    private BillingProviderException Unreachable(string operation, Exception inner)
    {
        _logger.LogWarning("Maxio could not be reached while trying to {0}: {1}", operation, inner.Message);

        return new BillingProviderException(
            operation, null, $"The billing provider could not be reached while trying to {operation}.", inner);
    }

    private static ProviderFailure FromRaw(RawError raw) =>
        new((int)raw.StatusCode, Summarize(raw.ReadAsString()));

    private static ProviderFailure FromValidation(Maxio.ErrorListResponse1 payload) =>
        new(
            (int)HttpStatusCode.UnprocessableEntity,
            payload.Errors.Count == 0 ? null : Summarize(string.Join("; ", payload.Errors)));

    /// <summary>
    /// Trims a provider message down to something safe to surface: single-line and length-capped, so a
    /// large or hostile error body cannot flood a page or a log.
    /// </summary>
    private static string? Summarize(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var collapsed = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= 500 ? collapsed : collapsed[..500] + "...";
    }

    // One reader per operation: the typed accessors live on the concrete error type, so a shared helper
    // could only ever reach TryGetRawError and would silently drop every validation payload.

    private static ProviderFailure Describe(ListProductsForProductFamilyError error) =>
        error.TryGetString(out var notFound) ? new ProviderFailure((int)HttpStatusCode.NotFound, Summarize(notFound))
        : error.TryGetRawError(out var raw) ? FromRaw(raw)
        : default;

    private static ProviderFailure Describe(CreateCustomerError error)
    {
        if (error.TryGetCustomerErrorResponse1(out var payload))
        {
            var messages = new List<string>();
            if (payload.Errors?.PerPage is { } perPage)
            {
                messages.AddRange(perPage);
            }

            if (payload.Errors?.PricePoint is { } pricePoint)
            {
                messages.AddRange(pricePoint);
            }

            return new ProviderFailure(
                (int)HttpStatusCode.UnprocessableEntity,
                messages.Count == 0 ? null : Summarize(string.Join("; ", messages)));
        }

        return error.TryGetRawError(out var raw) ? FromRaw(raw) : default;
    }

    private static ProviderFailure Describe(CreateSubscriptionError error) =>
        error.TryGetErrorListResponse1(out var validation) ? FromValidation(validation)
        : error.TryGetRawError(out var raw) ? FromRaw(raw)
        : default;

    private static ProviderFailure Describe(UpdateSubscriptionError error) =>
        error.TryGetErrorListResponse1(out var validation) ? FromValidation(validation)
        : error.TryGetRawError(out var raw) ? FromRaw(raw)
        : default;

    private static ProviderFailure Describe(CreateUsageError error) =>
        error.TryGetErrorListResponse1(out var validation) ? FromValidation(validation)
        : error.TryGetRawError(out var raw) ? FromRaw(raw)
        : default;

    private static ProviderFailure Describe(ReadSubscriptionComponentError error) =>
        error.TryGetNoContent(out var notFound) ? new ProviderFailure((int)HttpStatusCode.NotFound, null)
        : error.TryGetRawError(out var raw) ? FromRaw(raw)
        : default;

    private static ProviderFailure Describe(PreviewSubscriptionProductMigrationError error) =>
        error.TryGetErrorListResponse1(out var validation) ? FromValidation(validation)
        : error.TryGetRawError(out var raw) ? FromRaw(raw)
        : default;

    private static ProviderFailure Describe(MigrateSubscriptionProductError error) =>
        error.TryGetErrorListResponse1(out var validation) ? FromValidation(validation)
        : error.TryGetRawError(out var raw) ? FromRaw(raw)
        : default;

    private static ProviderFailure Describe(PauseSubscriptionError error) =>
        error.TryGetErrorListResponse1(out var validation) ? FromValidation(validation)
        : error.TryGetRawError(out var raw) ? FromRaw(raw)
        : default;

    private static ProviderFailure Describe(ResumeSubscriptionError error) =>
        error.TryGetErrorListResponse1(out var validation) ? FromValidation(validation)
        : error.TryGetRawError(out var raw) ? FromRaw(raw)
        : default;

    private static ProviderFailure Describe(ReactivateSubscriptionError error) =>
        error.TryGetErrorListResponse1(out var validation) ? FromValidation(validation)
        : error.TryGetRawError(out var raw) ? FromRaw(raw)
        : default;

    private static ProviderFailure Describe(InitiateDelayedCancellationError error) =>
        error.TryGetNoContent(out _) ? new ProviderFailure((int)HttpStatusCode.NotFound, null)
        : error.TryGetErrorListResponse1(out var validation) ? FromValidation(validation)
        : error.TryGetRawError(out var raw) ? FromRaw(raw)
        : default;

    private static ProviderFailure Describe(CancelSubscriptionApiError error)
    {
        if (error.TryGetNoContent(out _))
        {
            return new ProviderFailure((int)HttpStatusCode.NotFound, null);
        }

        if (error.TryGetCancelSubscriptionErrorResponse(out var payload))
        {
            if (payload.TryGetErrorListResponse1(out var validation))
            {
                return FromValidation(validation);
            }

            if (payload.TryGetSingleErrorResponse1(out var single))
            {
                return new ProviderFailure((int)HttpStatusCode.UnprocessableEntity, Summarize(single.Error));
            }

            return new ProviderFailure((int)HttpStatusCode.UnprocessableEntity, null);
        }

        return error.TryGetRawError(out var raw) ? FromRaw(raw) : default;
    }
}
