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
using DomainMeteredComponent = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.MeteredComponent;
using DomainProductFamily = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.ProductFamily;
using DomainSubscription = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The one and only place eShopOnWeb speaks to Maxio Advanced Billing. Implements the
/// provider-agnostic <see cref="IBillingClient"/> seam over the Maxio SDK, translating the
/// provider's wire shapes into the domain's own types and its failures into typed application
/// exceptions.
/// </summary>
/// <remarks>
/// <para>
/// The outbound base URL is resolved from <see cref="MaxioSettings.ResolveBaseUrl"/>, so the same
/// build targets production, a dev/sandbox tenant, or a local mock purely through configuration.
/// The host is never hardcoded here.
/// </para>
/// <para>
/// Maxio reports money in minor units (cents); every value leaving this class has already been
/// converted to whole currency units.
/// </para>
/// </remarks>
public class MaxioBillingClient : IBillingClient
{
    private const string MeteredComponentKind = "metered_component";

    /// <summary>A defensive cap so a misbehaving pager can never loop forever.</summary>
    private const int MaxCatalogPages = 20;

    private const int CatalogPageSize = 200;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings.Value ?? throw new BillingConfigurationException("Maxio settings are not configured.");
        _client = MaxioClientFactory.Create(httpClient, _settings);
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = await FindProductFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
        if (family is null)
        {
            throw new BillingConfigurationException(
                $"Product family handle '{_settings.ProductFamilyHandle}' does not resolve at Maxio. " +
                "Seed the family (UC0) or correct the configured handle.");
        }

        var plans = await ListFamilyPlansAsync(nameof(ListPlansAsync), family.Id, cancellationToken);

        return plans.Where(p => !p.IsArchived).ToArray();
    }

    public async Task<SubscriptionPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        // Resolved within the configured product family, never site-wide. Maxio's own handle
        // lookup spans the whole site, so a same-handle plan in another family would resolve
        // happily — and a subscription on it would silently lose access to the metered component,
        // which lives on this family. Scoping the lookup makes that impossible.
        var family = await FindProductFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
        if (family is null)
        {
            return null;
        }

        var plans = await ListFamilyPlansAsync(nameof(FindPlanByHandleAsync), family.Id, cancellationToken);

        return plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Lists every product in a family, archived ones included, following the provider's paging.
    /// Callers filter archived plans out when they are offering them for sale.
    /// </summary>
    private async Task<IReadOnlyList<SubscriptionPlan>> ListFamilyPlansAsync(
        string operation,
        int familyId,
        CancellationToken cancellationToken)
    {
        var id = familyId.ToString(CultureInfo.InvariantCulture);
        var plans = new List<SubscriptionPlan>();

        for (var page = 1; page <= MaxCatalogPages; page++)
        {
            var pageNumber = page;
            var responses = await CallAsync(
                operation,
                ct => _client.ProductFamilies.ListProductsForProductFamily(
                    id,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: true,
                    include: null,
                    page: pageNumber,
                    perPage: CatalogPageSize,
                    ct: ct),
                cancellationToken);

            if (responses is null || responses.Count == 0)
            {
                break;
            }

            plans.AddRange(responses
                .Select(r => r.Product)
                .Where(p => p is not null)
                .Select(p => MapPlan(p!)));

            if (responses.Count < CatalogPageSize)
            {
                break;
            }
        }

        return plans;
    }

    public async Task<DomainProductFamily?> FindProductFamilyAsync(string familyHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            return null;
        }

        // The SDK's read-by-id operation takes an int, so a handle is resolved by listing the
        // site's families and matching client-side.
        var families = await CallAsync(
            nameof(FindProductFamilyAsync),
            ct => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct),
            cancellationToken);

        var match = families?
            .Select(r => r.ProductFamily)
            .FirstOrDefault(f => f is not null
                && string.Equals(f.Handle, familyHandle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is null)
        {
            return null;
        }

        return new DomainProductFamily
        {
            Id = match.Id.Value,
            Handle = match.Handle ?? familyHandle,
            Name = match.Name ?? familyHandle,
            Description = match.Description
        };
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(BillingCustomerDetails details, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);

        var existing = await FindCustomerByReferenceAsync(details.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = details.FirstName,
                LastName = details.LastName,
                Email = details.Email,
                Reference = details.Reference
            }
        };

        try
        {
            var created = await CallAsync<CustomerResponse, CreateCustomerError>(
                nameof(EnsureCustomerAsync),
                ct => _client.Customers.CreateCustomer(body, ct),
                DescribeCreateCustomerError,
                cancellationToken);

            return MapCustomer(created.Customer, details.Reference);
        }
        catch (BillingProviderException)
        {
            // A concurrent subscribe may have created the same reference in the gap between the
            // lookup and the create. Re-read before giving up so the caller still gets a customer.
            // Any create failure is worth re-checking, not just a recognisably-duplicate one: the
            // SDK cannot always decode Maxio's rejection reason for customer writes.
            var raced = await FindCustomerByReferenceAsync(details.Reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var response = await CallAllowingNotFoundAsync(
            nameof(FindCustomerByReferenceAsync),
            ct => _client.Customers.ReadCustomerByReference(reference, ct),
            cancellationToken);

        return response?.Customer is null ? null : MapCustomer(response.Customer, reference);
    }

    public async Task<IReadOnlyList<DomainSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var responses = await CallAsync(
            nameof(ListSubscriptionsForCustomerAsync),
            ct => _client.Customers.ListCustomerSubscriptions(customerId, ct),
            cancellationToken);

        if (responses is null)
        {
            return Array.Empty<DomainSubscription>();
        }

        return responses
            .Select(r => r.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToArray();
    }

    public async Task<DomainSubscription?> FindSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var response = await CallAllowingNotFoundAsync(
            nameof(FindSubscriptionAsync),
            ct => _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: ct),
            cancellationToken);

        return response?.Subscription is null ? null : MapSubscription(response.Subscription);
    }

    public async Task<DomainSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default)
    {
        // Enrol by resolved id rather than handle: the id is unambiguous and is guaranteed to be a
        // plan inside the configured family, so the metered component stays available (UC2).
        var productId = await ResolvePlanIdAsync(nameof(CreateSubscriptionAsync), planHandle, cancellationToken);

        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                CustomerId = customerId,
                ProductId = productId,
                PaymentCollectionMethod = ResolveCollectionMethod()
            }
        };

        var response = await CallAsync<SubscriptionResponse, CreateSubscriptionError>(
            nameof(CreateSubscriptionAsync),
            ct => _client.Subscriptions.CreateSubscription(body, ct),
            e => DescribeErrorList(e.TryGetErrorListResponse1(out var list) ? list : null, e.TryGetRawError(out var raw) ? raw : null),
            cancellationToken);

        return RequireSubscription(nameof(CreateSubscriptionAsync), response);
    }

    public async Task<DomainMeteredComponent?> FindComponentByHandleAsync(string componentHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(componentHandle))
        {
            return null;
        }

        var response = await CallAllowingNotFoundAsync(
            nameof(FindComponentByHandleAsync),
            ct => _client.Components.FindComponent(componentHandle, ct),
            cancellationToken);

        return response?.Component is null ? null : MapComponent(response.Component);
    }

    public async Task<UsageRecord> RecordUsageAsync(
        int subscriptionId,
        int componentId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        var body = new CreateUsageRequest
        {
            Usage = new CreateUsage
            {
                Quantity = (double)quantity,
                Memo = memo
            }
        };

        var response = await CallAsync<UsageResponse, CreateUsageError>(
            nameof(RecordUsageAsync),
            ct => _client.SubscriptionComponents.CreateUsage(
                SubscriptionIdOrReference.Int(subscriptionId),
                ComponentIdModel.Int(componentId),
                body,
                ct),
            e => DescribeErrorList(e.TryGetErrorListResponse1(out var list) ? list : null, e.TryGetRawError(out var raw) ? raw : null),
            cancellationToken);

        var usage = response.Usage;

        return new UsageRecord
        {
            Id = usage.Id ?? 0,
            SubscriptionId = usage.SubscriptionId ?? subscriptionId,
            ComponentId = usage.ComponentId ?? componentId,
            ComponentHandle = usage.ComponentHandle,
            Quantity = ReadQuantity(usage.Quantity) ?? quantity,
            Memo = usage.Memo ?? memo,
            RecordedAt = usage.CreatedAt
        };
    }

    public async Task<int?> GetPeriodToDateUnitsAsync(int subscriptionId, int componentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await CallAsync<SubscriptionComponentResponse, ReadSubscriptionComponentError>(
                nameof(GetPeriodToDateUnitsAsync),
                ct => _client.SubscriptionComponents.ReadSubscriptionComponent(subscriptionId, componentId, ct),
                e => e.TryGetNoContent(out var missing)
                    ? DescribeRaw(missing)
                    : DescribeRaw(e.TryGetRawError(out var raw) ? raw : null),
                cancellationToken);

            // The provider accumulates each usage event's quantity onto the component line item's
            // unit balance, which is exactly the period-to-date total.
            return response.Component?.UnitBalance;
        }
        catch (BillingProviderNotFoundException)
        {
            // No line item for this component on this subscription yet: nothing has been used.
            return null;
        }
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        var subscription = await FindSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new BillingProviderNotFoundException(
                nameof(PreviewPlanChangeAsync), $"No subscription with id {subscriptionId} exists at Maxio.");

        var currentPlanHandle = subscription.PlanHandle ?? string.Empty;

        if (timing == PlanChangeTiming.NextRenewal)
        {
            // Deferring to the next renewal raises no proration at all: nothing is due now, and
            // the new plan's own price applies from the start of the next period.
            var targetPlan = await FindPlanByHandleAsync(targetPlanHandle, cancellationToken)
                ?? throw new BillingConfigurationException(
                    $"Target plan handle '{targetPlanHandle}' does not resolve at Maxio.");

            return new PlanChangePreview
            {
                SubscriptionId = subscriptionId,
                CurrentPlanHandle = currentPlanHandle,
                TargetPlanHandle = targetPlanHandle,
                Timing = timing,
                ProratedAdjustment = 0m,
                Charge = targetPlan.Price,
                PaymentDue = 0m,
                CreditApplied = 0m,
                EffectiveAt = subscription.CurrentPeriodEndsAt
            };
        }

        var targetProductId = await ResolvePlanIdAsync(nameof(PreviewPlanChangeAsync), targetPlanHandle, cancellationToken);

        var body = new SubscriptionMigrationPreviewRequest
        {
            Migration = new SubscriptionMigrationPreviewOptions
            {
                ProductId = targetProductId
            }
        };

        var response = await CallAsync<SubscriptionMigrationPreviewResponse, PreviewSubscriptionProductMigrationError>(
            nameof(PreviewPlanChangeAsync),
            ct => _client.SubscriptionProducts.PreviewSubscriptionProductMigration(subscriptionId, body, ct),
            e => DescribeErrorList(e.TryGetErrorListResponse1(out var list) ? list : null, e.TryGetRawError(out var raw) ? raw : null),
            cancellationToken);

        var migration = response.Migration;

        return new PlanChangePreview
        {
            SubscriptionId = subscriptionId,
            CurrentPlanHandle = currentPlanHandle,
            TargetPlanHandle = targetPlanHandle,
            Timing = timing,
            ProratedAdjustment = FromCents(migration.ProratedAdjustmentInCents),
            Charge = FromCents(migration.ChargeInCents),
            PaymentDue = FromCents(migration.PaymentDueInCents),
            CreditApplied = FromCents(migration.CreditAppliedInCents),
            EffectiveAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<DomainSubscription> ChangePlanAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        var targetProductId = await ResolvePlanIdAsync(nameof(ChangePlanAsync), targetPlanHandle, cancellationToken);

        if (timing == PlanChangeTiming.NextRenewal)
        {
            // Schedule the product change for the next renewal; no proration is raised.
            var updateBody = new UpdateSubscriptionRequest
            {
                Subscription = new UpdateSubscription
                {
                    ProductId = targetProductId,
                    ProductChangeDelayed = true
                }
            };

            var updated = await CallAsync<SubscriptionResponse, UpdateSubscriptionError>(
                nameof(ChangePlanAsync),
                ct => _client.Subscriptions.UpdateSubscription(subscriptionId, updateBody, ct),
                e => DescribeErrorList(e.TryGetErrorListResponse1(out var list) ? list : null, e.TryGetRawError(out var raw) ? raw : null),
                cancellationToken);

            return RequireSubscription(nameof(ChangePlanAsync), updated);
        }

        var migrationBody = new SubscriptionProductMigrationRequest
        {
            Migration = new SubscriptionProductMigration
            {
                ProductId = targetProductId
            }
        };

        var migrated = await CallAsync<SubscriptionResponse, MigrateSubscriptionProductError>(
            nameof(ChangePlanAsync),
            ct => _client.SubscriptionProducts.MigrateSubscriptionProduct(subscriptionId, migrationBody, ct),
            e => DescribeErrorList(e.TryGetErrorListResponse1(out var list) ? list : null, e.TryGetRawError(out var raw) ? raw : null),
            cancellationToken);

        return RequireSubscription(nameof(ChangePlanAsync), migrated);
    }

    public async Task<DomainSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var response = await CallAsync<SubscriptionResponse, PauseSubscriptionError>(
            nameof(PauseSubscriptionAsync),
            ct => _client.SubscriptionStatus.PauseSubscription(subscriptionId, body: null, ct: ct),
            e => DescribeErrorList(e.TryGetErrorListResponse1(out var list) ? list : null, e.TryGetRawError(out var raw) ? raw : null),
            cancellationToken);

        return RequireSubscription(nameof(PauseSubscriptionAsync), response);
    }

    public async Task<DomainSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var response = await CallAsync<SubscriptionResponse, ResumeSubscriptionError>(
            nameof(ResumeSubscriptionAsync),
            ct => _client.SubscriptionStatus.ResumeSubscription(subscriptionId, calendarBillingResumptionCharge: null, ct: ct),
            e => DescribeErrorList(e.TryGetErrorListResponse1(out var list) ? list : null, e.TryGetRawError(out var raw) ? raw : null),
            cancellationToken);

        return RequireSubscription(nameof(ResumeSubscriptionAsync), response);
    }

    public async Task<DomainSubscription> CancelSubscriptionAsync(int subscriptionId, string? reason, CancellationToken cancellationToken = default)
    {
        var body = new CancellationRequest
        {
            Subscription = new CancellationOptions
            {
                CancellationMessage = reason
            }
        };

        var response = await CallAsync<SubscriptionResponse, CancelSubscriptionApiError>(
            nameof(CancelSubscriptionAsync),
            ct => _client.SubscriptionStatus.CancelSubscription(subscriptionId, body, ct),
            DescribeCancelSubscriptionError,
            cancellationToken);

        return RequireSubscription(nameof(CancelSubscriptionAsync), response);
    }

    public async Task<DomainSubscription> CancelSubscriptionAtEndOfPeriodAsync(int subscriptionId, string? reason, CancellationToken cancellationToken = default)
    {
        var body = new CancellationRequest
        {
            Subscription = new CancellationOptions
            {
                CancellationMessage = reason,
                CancelAtEndOfPeriod = true
            }
        };

        await CallAsync<DelayedCancellationResponse, InitiateDelayedCancellationError>(
            nameof(CancelSubscriptionAtEndOfPeriodAsync),
            ct => _client.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId, body, ct),
            e => e.TryGetNoContent(out var missing)
                ? DescribeRaw(missing)
                : DescribeErrorList(e.TryGetErrorListResponse1(out var list) ? list : null, e.TryGetRawError(out var raw) ? raw : null),
            cancellationToken);

        // The delayed-cancel endpoint answers with a message, not a subscription, so the caller's
        // view of the scheduled cancellation comes from a fresh read.
        return await FindSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new BillingProviderNotFoundException(
                nameof(CancelSubscriptionAtEndOfPeriodAsync),
                $"Subscription {subscriptionId} could not be re-read after scheduling its cancellation.");
    }

    public async Task<DomainSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var response = await CallAsync<SubscriptionResponse, ReactivateSubscriptionError>(
            nameof(ReactivateSubscriptionAsync),
            ct => _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, body: null, ct: ct),
            e => DescribeErrorList(e.TryGetErrorListResponse1(out var list) ? list : null, e.TryGetRawError(out var raw) ? raw : null),
            cancellationToken);

        return RequireSubscription(nameof(ReactivateSubscriptionAsync), response);
    }

    /// <summary>
    /// Maps the configured collection method onto the provider's vocabulary, falling back to
    /// remittance (invoice the customer) for an unrecognised value rather than silently switching
    /// to automatic collection, which would demand a payment method the demo never captures.
    /// </summary>
    private MaxioAdvancedBilling.Models.Enums.CollectionMethod ResolveCollectionMethod()
    {
        var configured = _settings.PaymentCollectionMethod?.Trim();

        if (string.IsNullOrEmpty(configured))
        {
            return MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance;
        }

        var known = MaxioAdvancedBilling.Models.Enums.CollectionMethod.FromValue(configured.ToLowerInvariant());

        return known.IsKnownValue()
            ? known
            : MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance;
    }

    /// <summary>
    /// Resolves a plan handle to the live product id inside the configured family. The provider
    /// reassigns ids whenever the catalog is re-created, so this is always resolved fresh rather
    /// than read from configuration.
    /// </summary>
    /// <exception cref="BillingConfigurationException">The handle names no plan in the family.</exception>
    private async Task<int> ResolvePlanIdAsync(string operation, string planHandle, CancellationToken cancellationToken)
    {
        var plan = await FindPlanByHandleAsync(planHandle, cancellationToken);

        if (plan is null)
        {
            throw new BillingConfigurationException(
                $"Cannot perform {operation}: plan handle '{planHandle}' does not resolve to a plan in " +
                $"product family '{_settings.ProductFamilyHandle}' at Maxio. Re-seed the catalog (UC0) " +
                "or correct the configured handles.");
        }

        return plan.Id;
    }

    // ---------------------------------------------------------------------------------------
    // Mapping: Maxio wire shapes -> domain types
    // ---------------------------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(Product product) => new()
    {
        Id = product.Id ?? 0,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        Price = FromCents(product.PriceInCents),
        Interval = product.Interval ?? 1,
        IntervalUnit = product.IntervalUnit?.Value ?? "month",
        ProductFamilyHandle = product.ProductFamily?.Handle,
        // Only require_credit_card blocks signup without a payment method; the sibling
        // request_credit_card merely offers the card form and does not gate enrollment.
        RequiresPaymentMethod = product.RequireCreditCard ?? false,
        IsArchived = product.ArchivedAt.HasValue
    };

    private static BillingCustomer MapCustomer(Customer customer, string fallbackReference) => new()
    {
        Id = customer.Id ?? 0,
        Reference = customer.Reference ?? fallbackReference,
        Email = customer.Email,
        FirstName = customer.FirstName,
        LastName = customer.LastName
    };

    private static DomainSubscription MapSubscription(MaxioAdvancedBilling.Models.Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        State = MapState(subscription.State?.Value),
        ProviderState = subscription.State?.Value,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PlanPrice = FromCents(subscription.Product?.PriceInCents),
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CanceledAt = subscription.CanceledAt,
        DelayedCancelAt = subscription.DelayedCancelAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod ?? subscription.DelayedCancelAt.HasValue,
        PendingPlanHandle = subscription.NextProductHandle
    };

    private static DomainMeteredComponent MapComponent(Component component)
    {
        var kind = component.Kind?.Value;

        return new DomainMeteredComponent
        {
            Id = component.Id ?? 0,
            Handle = component.Handle ?? string.Empty,
            Name = component.Name ?? string.Empty,
            IsMetered = string.Equals(kind, MeteredComponentKind, StringComparison.OrdinalIgnoreCase),
            Kind = kind,
            UnitPrice = ParseDecimalUnits(component.UnitPrice) ?? FromCentsOrNull(component.PricePerUnitInCents),
            UnitName = component.UnitName,
            ProductFamilyHandle = component.ProductFamilyHandle,
            IsArchived = component.ArchivedAt.HasValue || (component.Archived ?? false)
        };
    }

    /// <summary>
    /// Maps Maxio's state vocabulary onto the domain's. A hold is reported as
    /// <c>on_hold</c> rather than <c>paused</c>, and both mean the same thing here.
    /// </summary>
    private static SubscriptionState MapState(string? providerState) => providerState switch
    {
        "pending" => SubscriptionState.Pending,
        "trialing" => SubscriptionState.Trialing,
        "assessing" => SubscriptionState.Active,
        "active" => SubscriptionState.Active,
        "soft_failure" => SubscriptionState.PastDue,
        "past_due" => SubscriptionState.PastDue,
        "suspended" => SubscriptionState.Suspended,
        "canceled" => SubscriptionState.Canceled,
        "expired" => SubscriptionState.Expired,
        "paused" => SubscriptionState.Paused,
        "on_hold" => SubscriptionState.Paused,
        "unpaid" => SubscriptionState.Unpaid,
        "trial_ended" => SubscriptionState.TrialEnded,
        "failed_to_create" => SubscriptionState.Failed,
        "awaiting_signup" => SubscriptionState.Pending,
        _ => SubscriptionState.Unknown
    };

    /// <summary>Converts a minor-unit (cents) amount to whole currency units.</summary>
    private static decimal FromCents(long? cents) => (cents ?? 0L) / 100m;

    private static decimal? FromCentsOrNull(long? cents) => cents.HasValue ? cents.Value / 100m : null;

    /// <summary>Parses a decimal amount the provider sends as text, for example "0.01".</summary>
    private static decimal? ParseDecimalUnits(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    /// <summary>Reads a quantity the provider models as either a number or a string.</summary>
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

        return quantity.TryGetString(out var asString) ? ParseDecimalUnits(asString) : null;
    }

    private static DomainSubscription RequireSubscription(string operation, SubscriptionResponse response)
    {
        if (response.Subscription is null)
        {
            throw new BillingProviderException(
                operation, "Maxio accepted the request but returned no subscription payload.");
        }

        return MapSubscription(response.Subscription);
    }

    // ---------------------------------------------------------------------------------------
    // Error translation: SDK exceptions -> typed application exceptions
    // ---------------------------------------------------------------------------------------

    /// <summary>What the provider said went wrong, normalised across its several error shapes.</summary>
    private readonly record struct ProviderError(int? StatusCode, string Message, IReadOnlyList<string> Details)
    {
        public static ProviderError Unknown { get; } =
            new(null, "Maxio rejected the request but supplied no diagnostic detail.", Array.Empty<string>());
    }

    /// <summary>Runs an operation whose failures are all reported as raw provider errors.</summary>
    private async Task<TResult> CallAsync<TResult>(
        string operation,
        Func<CancellationToken, Task<TResult>> call,
        CancellationToken cancellationToken)
    {
        try
        {
            return await call(cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(operation, DescribeRaw(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            throw TranslateUnreadablePayload(operation, ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw TranslateTransport(operation, ex);
        }
    }

    /// <summary>
    /// Runs an operation that has a typed error body, falling back to the raw shape for statuses
    /// the operation does not model.
    /// </summary>
    private async Task<TResult> CallAsync<TResult, TError>(
        string operation,
        Func<CancellationToken, Task<TResult>> call,
        Func<TError, ProviderError> describe,
        CancellationToken cancellationToken)
    {
        try
        {
            return await call(cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<TError> ex)
        {
            throw Translate(operation, describe(ex.Error), ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(operation, DescribeRaw(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            throw TranslateUnreadablePayload(operation, ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw TranslateTransport(operation, ex);
        }
    }

    /// <summary>
    /// Runs a read whose "no such entity" answer is a 404, returning null for that case instead of
    /// throwing. Every other failure still surfaces as a typed exception.
    /// </summary>
    private async Task<TResult?> CallAllowingNotFoundAsync<TResult>(
        string operation,
        Func<CancellationToken, Task<TResult>> call,
        CancellationToken cancellationToken)
        where TResult : class
    {
        try
        {
            return await CallAsync(operation, call, cancellationToken).ConfigureAwait(false);
        }
        catch (BillingProviderNotFoundException)
        {
            return null;
        }
    }

    private static bool IsTransport(Exception ex, CancellationToken cancellationToken)
    {
        // A caller-requested cancellation is not a provider failure and must propagate untouched.
        if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return ex is HttpRequestException or OperationCanceledException or TimeoutException;
    }

    /// <summary>
    /// Handles a response body the SDK's generated models cannot deserialize. This is reachable in
    /// practice: the SDK's typed 422 shape for customer writes does not match what Maxio actually
    /// sends, so the SDK throws while building its own error object and no <c>SdkException</c> is
    /// ever raised. Left uncaught that would escape as a raw <see cref="JsonException"/>; it is
    /// converted here so every provider failure still reaches callers as one typed exception.
    /// </summary>
    private static BillingProviderException TranslateUnreadablePayload(string operation, JsonException ex) =>
        new BillingProviderException(
            operation,
            $"Maxio returned a response for {operation} that could not be interpreted. " +
            "The request was rejected, but the provider's own message could not be recovered. " +
            $"Underlying error: {ex.Message}",
            statusCode: null,
            innerException: ex);

    private static BillingProviderException TranslateTransport(string operation, Exception ex) =>
        new BillingProviderUnavailableException(
            operation,
            $"Maxio could not be reached while performing {operation}: {ex.Message}",
            statusCode: null,
            innerException: ex);

    private static BillingProviderException Translate(string operation, ProviderError error, Exception inner)
    {
        var message = $"Maxio rejected {operation}: {error.Message}";

        return error.StatusCode switch
        {
            (int)HttpStatusCode.NotFound =>
                new BillingProviderNotFoundException(operation, message, inner),

            (int)HttpStatusCode.BadRequest or (int)HttpStatusCode.UnprocessableEntity or (int)HttpStatusCode.Conflict =>
                new BillingProviderValidationException(operation, message, error.Details, error.StatusCode, inner),

            (int)HttpStatusCode.RequestTimeout or (int)HttpStatusCode.TooManyRequests
                or >= 500 and <= 599 =>
                new BillingProviderUnavailableException(operation, message, error.StatusCode, inner),

            // A 422 body can arrive without a status when the operation models it as a typed shape.
            null when error.Details.Count > 0 =>
                new BillingProviderValidationException(operation, message, error.Details, null, inner),

            _ => new BillingProviderException(operation, message, error.StatusCode, inner)
        };
    }

    private static ProviderError DescribeRaw(RawError? raw)
    {
        if (raw is null)
        {
            return ProviderError.Unknown;
        }

        // The body may not be JSON at all (a 401 can be HTML), so it is only ever read as text.
        string body;
        try
        {
            body = raw.ReadAsString();
        }
        catch (Exception)
        {
            body = string.Empty;
        }

        var status = (int)raw.StatusCode;
        var message = string.IsNullOrWhiteSpace(body)
            ? $"HTTP {status} ({raw.StatusCode})."
            : $"HTTP {status} ({raw.StatusCode}): {Truncate(body)}";

        return new ProviderError(status, message, Array.Empty<string>());
    }

    private static ProviderError DescribeErrorList(ErrorListResponse1? list, RawError? raw)
    {
        if (list?.Errors is { Count: > 0 } messages)
        {
            return new ProviderError(
                (int)HttpStatusCode.UnprocessableEntity,
                string.Join("; ", messages),
                messages.ToArray());
        }

        return DescribeRaw(raw);
    }

    private static ProviderError DescribeCreateCustomerError(CreateCustomerError error)
    {
        // The generated 422 shape for customers carries no general message list, so the raw body
        // is the only reliable source of text.
        if (error.TryGetRawError(out var raw))
        {
            return DescribeRaw(raw);
        }

        if (error.TryGetCustomerErrorResponse1(out _))
        {
            return new ProviderError(
                (int)HttpStatusCode.UnprocessableEntity,
                "Maxio rejected the customer details as invalid.",
                Array.Empty<string>());
        }

        return ProviderError.Unknown;
    }

    private static ProviderError DescribeCancelSubscriptionError(CancelSubscriptionApiError error)
    {
        if (error.TryGetNoContent(out var missing))
        {
            return DescribeRaw(missing);
        }

        if (error.TryGetCancelSubscriptionErrorResponse(out var cancelError))
        {
            if (cancelError.TryGetErrorListResponse1(out var list) && list.Errors is { Count: > 0 } messages)
            {
                return new ProviderError(
                    (int)HttpStatusCode.UnprocessableEntity,
                    string.Join("; ", messages),
                    messages.ToArray());
            }

            if (cancelError.TryGetSingleErrorResponse1(out var single))
            {
                return new ProviderError(
                    (int)HttpStatusCode.UnprocessableEntity,
                    single.Error,
                    new[] { single.Error });
            }
        }

        return DescribeRaw(error.TryGetRawError(out var raw) ? raw : null);
    }

    private static string Truncate(string value) =>
        value.Length <= 512 ? value : value[..512] + "...";
}
