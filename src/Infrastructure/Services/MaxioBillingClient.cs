using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using MaxioComponent = MaxioAdvancedBilling.Models.Component;
using MaxioCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod;
using MaxioComponentKind = MaxioAdvancedBilling.Models.Enums.ComponentKind;
using MaxioProduct = MaxioAdvancedBilling.Models.Product;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;
using MaxioUsageQuantity = MaxioAdvancedBilling.Models.AnyOf.Quantity1;
using MeteredComponentEntity = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.MeteredComponent;
using SubscriptionEntity = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The one and only place in the solution that talks to Maxio Advanced Billing. It implements the
/// provider-agnostic <see cref="IBillingClient"/> using the Maxio SDK over an injected
/// <see cref="HttpClient"/>, normalizes every response into eShopOnWeb's own domain types, and
/// converts every provider failure into <see cref="BillingProviderException"/> so that no SDK type
/// escapes Infrastructure.
/// </summary>
/// <remarks>
/// The outbound target server is resolved from <see cref="MaxioSettings.ResolveBaseUrl"/>, which
/// honours an explicit <c>Maxio:BaseUrl</c> over the subdomain-derived host. Pointing this build
/// at production, a dev tenant, or a local mock is therefore a configuration change only.
/// </remarks>
public class MaxioBillingClient : IBillingClient
{
    /// <summary>Maxio expects the API key as the Basic-auth user name and a literal "x" password.</summary>
    private const string BasicAuthPasswordPlaceholder = "x";

    /// <summary>Page size used when walking Maxio's manually paginated list endpoints.</summary>
    private const int PageSize = 200;

    /// <summary>Guards against an unbounded loop if a provider page never shrinks.</summary>
    private const int MaxPages = 50;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    /// <summary>
    /// Resolved once per client instance: Maxio reassigns numeric ids on a re-seed, so the family
    /// is always located from its stable handle rather than a configured id.
    /// </summary>
    private int? _productFamilyId;

    /// <summary>
    /// The validated metered component. Cached so the metered-kind check runs once per client
    /// instance rather than on every usage call.
    /// </summary>
    private MeteredComponentEntity? _meteredComponent;

    /// <summary>
    /// How new subscriptions collect payment. Resolved from the site rather than configured,
    /// because the correct value depends on the site's invoicing architecture.
    /// </summary>
    private MaxioCollectionMethod? _paymentCollectionMethod;

    public MaxioBillingClient(
        HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings.Value;
        _logger = logger;
        _client = BuildClient(httpClient, _settings);
    }

    /// <summary>
    /// Builds the SDK client. The resolved base URL is written onto the server options for the
    /// configured region, because that — not <see cref="HttpClient.BaseAddress"/> — is what the
    /// SDK composes request URLs from. Writing the fully resolved URL means an explicit
    /// <c>Maxio:BaseUrl</c> can never be silently overridden by the subdomain template.
    /// </summary>
    private static MaxioAdvancedBillingClient BuildClient(HttpClient httpClient, MaxioSettings settings)
    {
        var baseUrl = settings.ResolveBaseUrl();

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = BasicAuthPasswordPlaceholder
            },
            Environment = settings.IsEuropeanRegion ? ServerEnvironment.Eu : ServerEnvironment.Us,
            Retry = BuildRetryOptions(settings)
        };

        if (settings.IsEuropeanRegion)
        {
            options.Server.Production.Eu.BaseUrl = baseUrl;
            options.Server.Production.Eu.Site = settings.Subdomain;
        }
        else
        {
            options.Server.Production.Us.BaseUrl = baseUrl;
            options.Server.Production.Us.Site = settings.Subdomain;
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }

    /// <summary>
    /// Bounds the SDK's retry policy. Only idempotent verbs are retried by default, so a
    /// subscribe, a usage report, or a cancellation is never silently re-sent and cannot
    /// double-bill; this simply caps how long a failing read is allowed to stall a page.
    /// </summary>
    private static RetryOptions BuildRetryOptions(MaxioSettings settings)
    {
        var defaults = RetryOptions.Default();

        // The underlying retry strategy rejects an attempt count below one, so retries are turned
        // off by leaving nothing for it to match on rather than by zeroing the count.
        if (settings.MaxRetries <= 0)
        {
            return defaults with
            {
                MaxRetries = 1,
                StatusCodesToRetry = Array.Empty<HttpStatusCode>()
            };
        }

        return defaults with { MaxRetries = settings.MaxRetries };
    }

    public async Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

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
            "list the plans in the configured product family");

        return responses
            .Select(response => MapPlan(response.Product))
            .Where(plan => !plan.IsArchived)
            .OrderBy(plan => plan.PriceInCents)
            .ToArray();
    }

    public async Task<BillingPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planHandle);

        var response = await InvokeAllowingNotFoundAsync(
            () => _client.Products.ReadProductByHandle(planHandle, ct: cancellationToken),
            $"read the plan '{planHandle}'");

        return response is null ? null : MapPlan(response.Product);
    }

    public async Task<MeteredComponentEntity?> FindMeteredComponentAsync(
        string componentHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentHandle);

        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        // Listed through the family rather than looked up site-wide, so a component that exists
        // but hangs off the wrong family is correctly reported as missing.
        var responses = await InvokeAsync(
            () => _client.Components.ListComponentsForProductFamily(
                familyId,
                includeArchived: true,
                filter: null,
                dateField: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                page: 1,
                perPage: PageSize,
                ct: cancellationToken),
            "list the components on the configured product family");

        var match = responses
            .Select(response => response.Component)
            .FirstOrDefault(component => string.Equals(component.Handle, componentHandle, StringComparison.OrdinalIgnoreCase));

        return match is null ? null : MapComponent(match);
    }

    public async Task<MeteredComponentEntity> GetConfiguredMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        if (_meteredComponent is not null)
        {
            return _meteredComponent;
        }

        var handle = _settings.MeteredComponentHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingConfigurationException(
                $"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.MeteredComponentHandle)}' is not configured, so pay-as-you-go usage cannot be recorded.");
        }

        var component = await FindMeteredComponentAsync(handle, cancellationToken)
            ?? throw new BillingConfigurationException(
                $"Metered component '{handle}' does not exist on product family '{_settings.ProductFamilyHandle}'. Re-seed the family before recording usage.");

        if (component.IsArchived)
        {
            throw new BillingConfigurationException(
                $"Component '{handle}' is archived and cannot accept usage. Recreate it as a metered component on '{_settings.ProductFamilyHandle}'.");
        }

        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"Component '{handle}' exists but is not of metered kind, so usage cannot be recorded against it. A component's kind cannot be changed in place — archive it and recreate it as metered.");
        }

        _meteredComponent = component;

        return component;
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(
        string customerReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerReference);

        var response = await InvokeAllowingNotFoundAsync(
            () => _client.Customers.ReadCustomerByReference(customerReference, ct: cancellationToken),
            "look up the billing customer");

        return response is null ? null : MapCustomer(response.Customer);
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(
        BillingCustomerRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Reference);

        var existing = await FindCustomerByReferenceAsync(registration.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await InvokeAsync(
                () => _client.Customers.CreateCustomer(
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
                    ct: cancellationToken),
                "create the billing customer");

            return MapCustomer(created.Customer);
        }
        catch (BillingProviderException)
        {
            // Two concurrent subscribes race to create the same reference; the loser is rejected
            // for a duplicate reference. Re-reading keeps the operation idempotent either way.
            var raced = await FindCustomerByReferenceAsync(registration.Reference, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation(
                    "Billing customer for reference {0} already existed; reusing it.",
                    registration.Reference);

                return raced;
            }

            throw;
        }
    }

    public async Task<SubscriptionEntity> CreateSubscriptionAsync(
        int customerId,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planHandle);

        var collectionMethod = await ResolvePaymentCollectionMethodAsync(cancellationToken);

        var response = await InvokeAsync(
            () => _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        CustomerId = customerId,
                        ProductHandle = planHandle,

                        // Bill by invoice rather than charging a card. The demo plans require no
                        // payment method, and without this the site's automatic collection default
                        // would reject the enrollment for having no card on file.
                        PaymentCollectionMethod = collectionMethod
                    }
                },
                ct: cancellationToken),
            $"subscribe customer {customerId} to plan '{planHandle}'");

        return RequireSubscription(response.Subscription, "create the subscription");
    }

    public async Task<IReadOnlyCollection<SubscriptionEntity>> ListSubscriptionsForCustomerAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var responses = await InvokeAsync(
            () => _client.Customers.ListCustomerSubscriptions(customerId, ct: cancellationToken),
            $"list the subscriptions of customer {customerId}");

        return responses
            .Select(response => response.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => MapSubscription(subscription!))
            .OrderByDescending(subscription => subscription.Id)
            .ToArray();
    }

    public async Task<SubscriptionEntity?> GetSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAllowingNotFoundAsync(
            () => _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: cancellationToken),
            $"read subscription {subscriptionId}");

        if (response?.Subscription is null)
        {
            return null;
        }

        return MapSubscription(response.Subscription);
    }

    public async Task<UsageRecord> RecordUsageAsync(
        int subscriptionId,
        int componentId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(
            () => _client.SubscriptionComponents.CreateUsage(
                subscriptionId,
                componentId,
                new CreateUsageRequest
                {
                    Usage = new CreateUsage
                    {
                        Quantity = (double)quantity,
                        Memo = memo
                    }
                },
                ct: cancellationToken),
            $"record usage on subscription {subscriptionId}");

        var usage = response.Usage;

        return new UsageRecord(
            Id: usage.Id ?? 0L,
            SubscriptionId: usage.SubscriptionId ?? subscriptionId,
            ComponentId: usage.ComponentId ?? componentId,
            Quantity: ReadQuantity(usage.Quantity) ?? quantity,
            Memo: usage.Memo ?? memo,
            RecordedAt: usage.CreatedAt);
    }

    public async Task<decimal> GetPeriodToDateUsageAsync(
        int subscriptionId,
        int componentId,
        DateTimeOffset? periodStart,
        DateTimeOffset? periodEnd,
        CancellationToken cancellationToken = default)
    {
        var total = 0m;

        // Maxio does not stamp a billing period onto a usage record, so the current period is
        // expressed as a date window and the records are walked and summed here.
        for (var page = 1; page <= MaxPages; page++)
        {
            var pageNumber = page;

            var responses = await InvokeAsync(
                () => _client.SubscriptionComponents.ListUsages(
                    subscriptionId,
                    componentId,
                    sinceId: null,
                    maxId: null,
                    sinceDate: periodStart,
                    untilDate: periodEnd,
                    page: pageNumber,
                    perPage: PageSize,
                    ct: cancellationToken),
                $"read the period-to-date usage of subscription {subscriptionId}");

            foreach (var response in responses)
            {
                total += ReadQuantity(response.Usage.Quantity) ?? 0m;
            }

            if (responses.Count < PageSize)
            {
                return total;
            }
        }

        _logger.LogWarning(
            "Stopped summing usage for subscription {0} after {1} pages; the reported period-to-date total may be incomplete.",
            subscriptionId,
            MaxPages);

        return total;
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPlanHandle);

        var subscription = await GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw NotFound($"Subscription {subscriptionId} was not found.");

        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            // A deferred change is never prorated: nothing is owed now, and the customer simply
            // starts paying the target plan's price from the next period.
            var targetPlan = await FindPlanByHandleAsync(targetPlanHandle, cancellationToken)
                ?? throw new BillingConfigurationException(
                    $"Plan '{targetPlanHandle}' does not exist in the billing provider.");

            return new PlanChangePreview(
                SubscriptionId: subscriptionId,
                CurrentPlanHandle: subscription.PlanHandle,
                TargetPlanHandle: targetPlanHandle,
                Timing: timing,
                ProratedAdjustmentInCents: 0L,
                ChargeInCents: targetPlan.PriceInCents,
                CreditAppliedInCents: 0L,
                PaymentDueInCents: 0L);
        }

        var response = await InvokeAsync(
            () => _client.SubscriptionProducts.PreviewSubscriptionProductMigration(
                subscriptionId,
                new SubscriptionMigrationPreviewRequest
                {
                    Migration = new SubscriptionMigrationPreviewOptions
                    {
                        ProductHandle = targetPlanHandle
                    }
                },
                ct: cancellationToken),
            $"preview moving subscription {subscriptionId} to plan '{targetPlanHandle}'");

        var migration = response.Migration;

        return new PlanChangePreview(
            SubscriptionId: subscriptionId,
            CurrentPlanHandle: subscription.PlanHandle,
            TargetPlanHandle: targetPlanHandle,
            Timing: timing,
            ProratedAdjustmentInCents: migration.ProratedAdjustmentInCents ?? 0L,
            ChargeInCents: migration.ChargeInCents ?? 0L,
            CreditAppliedInCents: migration.CreditAppliedInCents ?? 0L,
            PaymentDueInCents: migration.PaymentDueInCents ?? 0L);
    }

    public async Task<SubscriptionEntity> ChangePlanAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPlanHandle);

        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            var deferred = await InvokeAsync(
                () => _client.Subscriptions.UpdateSubscription(
                    subscriptionId,
                    new UpdateSubscriptionRequest
                    {
                        Subscription = new UpdateSubscription
                        {
                            ProductHandle = targetPlanHandle,
                            ProductChangeDelayed = true
                        }
                    },
                    ct: cancellationToken),
                $"schedule subscription {subscriptionId} to move to plan '{targetPlanHandle}' at renewal");

            return RequireSubscription(deferred.Subscription, "schedule the plan change");
        }

        var migrated = await InvokeAsync(
            () => _client.SubscriptionProducts.MigrateSubscriptionProduct(
                subscriptionId,
                new SubscriptionProductMigrationRequest
                {
                    Migration = new SubscriptionProductMigration
                    {
                        ProductHandle = targetPlanHandle,

                        // Start a fresh period so the change is prorated against the plan the
                        // customer was previewed, rather than silently keeping the old period.
                        PreservePeriod = false
                    }
                },
                ct: cancellationToken),
            $"move subscription {subscriptionId} to plan '{targetPlanHandle}'");

        return RequireSubscription(migrated.Subscription, "apply the plan change");
    }

    public async Task<SubscriptionEntity> PauseSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(
            () => _client.SubscriptionStatus.PauseSubscription(
                subscriptionId,
                new PauseRequest(),
                ct: cancellationToken),
            $"pause subscription {subscriptionId}");

        return RequireSubscription(response.Subscription, "pause the subscription");
    }

    public async Task<SubscriptionEntity> ResumeSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(
            () => _client.SubscriptionStatus.ResumeSubscription(
                subscriptionId,
                calendarBillingResumptionCharge: null,
                ct: cancellationToken),
            $"resume subscription {subscriptionId}");

        return RequireSubscription(response.Subscription, "resume the subscription");
    }

    public async Task<SubscriptionEntity> CancelSubscriptionAsync(
        int subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (timing == CancellationTiming.EndOfPeriod)
        {
            // The delayed-cancellation endpoint answers with a message rather than the
            // subscription, so the provider's own view is re-read and returned as the truth.
            await InvokeAsync(
                () => _client.SubscriptionStatus.InitiateDelayedCancellation(
                    subscriptionId,
                    new CancellationRequest
                    {
                        Subscription = new CancellationOptions
                        {
                            CancellationMessage = reason,
                            CancelAtEndOfPeriod = true
                        }
                    },
                    ct: cancellationToken),
                $"schedule subscription {subscriptionId} to cancel at the end of the period");

            return await GetSubscriptionAsync(subscriptionId, cancellationToken)
                ?? throw NotFound($"Subscription {subscriptionId} was not found after scheduling its cancellation.");
        }

        var response = await InvokeAsync(
            () => _client.SubscriptionStatus.CancelSubscription(
                subscriptionId,
                new CancellationRequest
                {
                    Subscription = new CancellationOptions
                    {
                        CancellationMessage = reason
                    }
                },
                ct: cancellationToken),
            $"cancel subscription {subscriptionId}");

        return RequireSubscription(response.Subscription, "cancel the subscription");
    }

    public async Task<SubscriptionEntity> ReactivateSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(
            () => _client.SubscriptionStatus.ReactivateSubscription(
                subscriptionId,
                new ReactivateSubscriptionRequest(),
                ct: cancellationToken),
            $"reactivate subscription {subscriptionId}");

        return RequireSubscription(response.Subscription, "reactivate the subscription");
    }

    /// <summary>
    /// Locates the configured product family by its stable handle. Handles survive a sandbox
    /// re-seed; numeric ids do not, so the configured id is never trusted as the lookup key.
    /// </summary>
    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_productFamilyId.HasValue)
        {
            return _productFamilyId.Value;
        }

        var handle = _settings.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingConfigurationException(
                $"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)}' is not configured, so the plan catalogue cannot be resolved.");
        }

        var families = await InvokeAsync(
            () => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: cancellationToken),
            "list the product families");

        var match = families
            .Select(response => response.ProductFamily)
            .FirstOrDefault(family =>
                family is not null &&
                string.Equals(family.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is null)
        {
            throw new BillingConfigurationException(
                $"Product family '{handle}' does not exist in the billing provider. Seed the family before using the subscription module.");
        }

        _productFamilyId = match.Id.Value;

        return _productFamilyId.Value;
    }

    /// <summary>
    /// Decides how new subscriptions collect payment, by asking the site which invoicing
    /// architecture it runs. Relationship Invoicing sites express "bill by invoice" as
    /// <c>remittance</c>; legacy Statements sites call the same thing <c>invoice</c>. Reading it
    /// rather than hardcoding it means the integration works against either kind of site.
    /// </summary>
    private async Task<MaxioCollectionMethod> ResolvePaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        if (_paymentCollectionMethod is not null)
        {
            return _paymentCollectionMethod;
        }

        var response = await InvokeAsync(
            () => _client.Sites.ReadSite(ct: cancellationToken),
            "read the billing site configuration");

        _paymentCollectionMethod = response.Site?.RelationshipInvoicingEnabled ?? false
            ? MaxioCollectionMethod.Remittance
            : MaxioCollectionMethod.Invoice;

        return _paymentCollectionMethod;
    }

    private static BillingPlan MapPlan(MaxioProduct product) => new(
        Id: product.Id ?? 0,
        Handle: product.Handle ?? string.Empty,
        Name: product.Name ?? string.Empty,
        Description: product.Description,
        PriceInCents: product.PriceInCents ?? 0L,
        Interval: product.Interval ?? 1,
        IntervalUnit: product.IntervalUnit?.Value ?? "month",
        RequiresPaymentMethod: product.RequireCreditCard ?? false,
        ArchivedAt: product.ArchivedAt);

    private static MeteredComponentEntity MapComponent(MaxioComponent component) => new(
        Id: component.Id ?? 0,
        Handle: component.Handle ?? string.Empty,
        Name: component.Name ?? string.Empty,
        UnitName: component.UnitName,
        UnitPriceInCents: component.PricePerUnitInCents ?? ParseDollarsToCents(component.UnitPrice),
        IsMetered: component.Kind == MaxioComponentKind.MeteredComponent,
        IsArchived: component.Archived ?? component.ArchivedAt.HasValue);

    private static BillingCustomer MapCustomer(Customer customer) => new(
        Id: customer.Id ?? 0,
        Reference: customer.Reference ?? string.Empty,
        Email: customer.Email ?? string.Empty,
        FirstName: customer.FirstName ?? string.Empty,
        LastName: customer.LastName ?? string.Empty);

    private static SubscriptionEntity MapSubscription(MaxioSubscription subscription) => new(
        Id: subscription.Id ?? 0,
        State: MapState(subscription.State?.Value),
        CustomerId: subscription.Customer?.Id ?? 0,
        CustomerReference: subscription.Customer?.Reference,
        PlanId: subscription.Product?.Id ?? 0,
        PlanHandle: subscription.Product?.Handle ?? string.Empty,
        PlanName: subscription.Product?.Name ?? string.Empty,

        // Prefer the denormalized subscription price; fall back to the nested product's price.
        PlanPriceInCents: subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0L,
        CurrentPeriodStartedAt: subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
        NextAssessmentAt: subscription.NextAssessmentAt,
        CancelAtEndOfPeriod: subscription.CancelAtEndOfPeriod ?? false,
        CanceledAt: subscription.CanceledAt,
        NextPlanHandle: subscription.NextProductHandle);

    /// <summary>
    /// Normalizes Maxio's subscription state onto eShopOnWeb's own vocabulary. Maxio models a
    /// held subscription as either <c>on_hold</c> or <c>paused</c>, so both map to
    /// <see cref="SubscriptionState.Paused"/>. An unrecognized value degrades to
    /// <see cref="SubscriptionState.Unknown"/> rather than throwing, so a new provider state can
    /// never break a page that is only reading.
    /// </summary>
    private static SubscriptionState MapState(string? state) => state switch
    {
        "active" or "assessing" => SubscriptionState.Active,
        "trialing" => SubscriptionState.Trialing,
        "pending" or "awaiting_signup" => SubscriptionState.Pending,
        "past_due" or "soft_failure" => SubscriptionState.PastDue,
        "suspended" => SubscriptionState.Suspended,
        "on_hold" or "paused" => SubscriptionState.Paused,
        "canceled" => SubscriptionState.Canceled,
        "expired" => SubscriptionState.Expired,
        "unpaid" => SubscriptionState.Unpaid,
        "trial_ended" => SubscriptionState.TrialEnded,
        "failed_to_create" => SubscriptionState.Failed,
        _ => SubscriptionState.Unknown
    };

    /// <summary>
    /// Reads a usage quantity, which Maxio returns as either a number or a decimal string.
    /// Returns <see langword="null"/> when neither representation is present, so an unreadable
    /// record is skipped rather than silently counted as zero-with-confidence.
    /// </summary>
    private static decimal? ReadQuantity(MaxioUsageQuantity? quantity)
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
    /// Converts a decimal money string in the site's currency unit into cents. Used only as a
    /// fallback when the provider omits the explicit cents field.
    /// </summary>
    private static long ParseDollarsToCents(string? dollars) =>
        decimal.TryParse(dollars, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero)
            : 0L;

    private static SubscriptionEntity RequireSubscription(MaxioSubscription? subscription, string operation) =>
        subscription is null
            ? throw new BillingProviderException(
                $"The billing provider accepted the request to {operation} but returned no subscription.")
            : MapSubscription(subscription);

    private static BillingProviderException NotFound(string message) =>
        new(message, statusCode: (int)HttpStatusCode.NotFound, providerErrors: null, innerException: null);

    /// <summary>
    /// Runs a provider call, translating every SDK or transport failure into
    /// <see cref="BillingProviderException"/>.
    /// </summary>
    private async Task<T> InvokeAsync<T>(Func<Task<T>> operation, string description)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (TryDescribeSdkFailure(exception, out var failure))
        {
            throw ToBillingException(description, failure, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new BillingProviderException(
                $"The billing provider could not be reached while trying to {description}.",
                exception);
        }
        catch (JsonException exception)
        {
            // A payload the SDK cannot deserialize — most often an error body whose real shape
            // differs from the one the SDK models — would otherwise escape as a bare
            // JsonException. It is normalized here so callers only ever handle one error type.
            throw new BillingProviderException(
                $"The billing provider returned a response that could not be understood while trying to {description}.",
                exception);
        }
    }

    /// <summary>
    /// Runs a read that is allowed to miss, returning <see langword="null"/> on a 404 instead of
    /// throwing. Every other failure still surfaces as <see cref="BillingProviderException"/>.
    /// </summary>
    private async Task<T?> InvokeAllowingNotFoundAsync<T>(Func<Task<T>> operation, string description)
        where T : class
    {
        try
        {
            return await InvokeAsync(operation, description);
        }
        catch (BillingProviderException exception) when (exception.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static BillingProviderException ToBillingException(
        string description,
        SdkFailureDetail failure,
        Exception exception)
    {
        var summary = failure.Messages.Any()
            ? $"The billing provider rejected the request to {description}: {string.Join(" ", failure.Messages)}"
            : $"The billing provider failed the request to {description}.";

        return new BillingProviderException(summary, failure.StatusCode, failure.Messages, exception);
    }

    /// <summary>
    /// Recognizes the SDK's single generic exception, <c>SdkException&lt;TError&gt;</c>, and pulls
    /// the status code and any provider messages out of its error payload.
    /// </summary>
    /// <remarks>
    /// The generic argument varies per operation and the open generic cannot be caught directly,
    /// so the exception is matched structurally. The payload is read through the SDK's own
    /// <c>TryGet…</c> accessors, which is the only supported route: on a status the SDK models
    /// (typically a 422 validation body) the raw response is not retained, so
    /// <c>TryGetRawError</c> deliberately reports nothing and the typed payload is the sole
    /// source of the provider's message.
    /// </remarks>
    private static bool TryDescribeSdkFailure(Exception exception, out SdkFailureDetail failure)
    {
        failure = new SdkFailureDetail();

        var exceptionType = exception.GetType();
        if (!exceptionType.IsGenericType ||
            exceptionType.GetGenericTypeDefinition() != typeof(SdkException<>))
        {
            return false;
        }

        var error = exceptionType
            .GetProperty(nameof(SdkException<RawError>.Error))
            ?.GetValue(exception);

        CollectFailureDetail(error, failure, depth: 0);

        return true;
    }

    /// <summary>
    /// Walks an SDK error payload, collecting the HTTP status and any human-readable messages.
    /// </summary>
    private static void CollectFailureDetail(object? node, SdkFailureDetail failure, int depth)
    {
        const int maxDepth = 3;

        if (node is null || depth > maxDepth)
        {
            return;
        }

        switch (node)
        {
            case RawError raw:
                failure.StatusCode ??= (int)raw.StatusCode;
                return;

            case string text:
                failure.Add(text);
                return;

            case IEnumerable<string> texts:
                foreach (var text in texts)
                {
                    failure.Add(text);
                }

                return;
        }

        // The SDK exposes each modeled status through its own TryGet accessor; probing them all
        // is the only way to reach the payload without hard-coding one error type per operation.
        foreach (var accessor in node.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!accessor.Name.StartsWith("TryGet", StringComparison.Ordinal) ||
                accessor.ReturnType != typeof(bool))
            {
                continue;
            }

            var parameters = accessor.GetParameters();
            if (parameters.Length != 1 || !parameters[0].IsOut)
            {
                continue;
            }

            var arguments = new object?[] { null };

            try
            {
                if (accessor.Invoke(node, arguments) is true)
                {
                    CollectFailureDetail(arguments[0], failure, depth + 1);
                }
            }
            catch (TargetInvocationException)
            {
                // An accessor that cannot materialize its payload simply yields no detail.
            }
        }

        if (depth == 0)
        {
            return;
        }

        // Typed payloads carry their text in plain string / string-collection properties
        // (for example ErrorListResponse1.Errors and SingleErrorResponse1.Error).
        foreach (var property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0 ||
                (property.PropertyType != typeof(string) &&
                 !typeof(IEnumerable<string>).IsAssignableFrom(property.PropertyType)))
            {
                continue;
            }

            try
            {
                CollectFailureDetail(property.GetValue(node), failure, depth + 1);
            }
            catch (TargetInvocationException)
            {
                // A property that throws on read contributes nothing.
            }
        }
    }

    /// <summary>Accumulates what could be learned about a provider failure.</summary>
    private sealed class SdkFailureDetail
    {
        private readonly List<string> _messages = new();

        public int? StatusCode { get; set; }

        public IReadOnlyCollection<string> Messages => _messages;

        public void Add(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message) && !_messages.Contains(message))
            {
                _messages.Add(message);
            }
        }
    }
}
