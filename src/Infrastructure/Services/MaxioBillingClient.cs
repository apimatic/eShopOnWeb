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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The one and only place eShopOnWeb talks to Maxio Advanced Billing. Every provider result is mapped to
/// a provider-agnostic type from ApplicationCore, and every provider failure is surfaced as a
/// <see cref="BillingProviderException"/> so no SDK type escapes this class.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    /// <summary>Prefix Maxio uses to address an entity by its handle instead of its numeric id.</summary>
    private const string HandlePrefix = "handle:";

    /// <summary>Page size used when walking the provider's paged list endpoints.</summary>
    private const int PageSize = 100;

    /// <summary>Upper bound on pages walked, so a misbehaving provider cannot loop forever.</summary>
    private const int MaxPages = 100;

    /// <summary>Longest provider message echoed into an exception.</summary>
    private const int MaxProviderMessageLength = 512;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    private int? _resolvedProductFamilyId;

    public MaxioBillingClient(MaxioAdvancedBillingClient client,
        MaxioSettings settings,
        IAppLogger<MaxioBillingClient> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var plans = new List<BillingPlan>();

        for (var page = 1; page <= MaxPages; page++)
        {
            IReadOnlyList<ProductResponse> pageResults;
            try
            {
                pageResults = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
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
                throw Translate(ex);
            }
            catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
            {
                throw Unreachable(ex);
            }

            if (pageResults is null || pageResults.Count == 0)
            {
                break;
            }

            plans.AddRange(pageResults.Select(r => MapPlan(r.Product)));

            if (pageResults.Count < PageSize)
            {
                break;
            }
        }

        return plans;
    }

    public async Task<BillingPlan?> FindPlanByHandleAsync(string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(p =>
            string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: cancellationToken);
            return response?.Customer is null ? null : MapCustomer(response.Customer);
        }
        catch (SdkException<RawError> ex) when (IsNotFound(ex.Error))
        {
            // An unknown reference is an ordinary outcome of the idempotent lookup, not a failure.
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (Exception ex) when (IsUnreadableResponse(ex))
        {
            throw Unreadable(ex, 502, "The billing provider returned a response that could not be interpreted.");
        }
    }

    public async Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body, ct: cancellationToken);
            if (response?.Customer is null)
            {
                throw new BillingProviderException(502,
                    "The billing provider accepted the customer but returned no customer record.");
            }

            return MapCustomer(response.Customer);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            throw Translate(ex);
        }
        catch (JsonException ex)
        {
            // The generated 422 model for this operation cannot represent the provider's real validation
            // body, so deserializing the error itself fails. Only the 422 branch parses a typed payload
            // here, so a parse failure means the request was rejected as invalid.
            throw Unreadable(ex, 422, "The customer could not be created — validation failed.");
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (Exception ex) when (IsUnreadableResponse(ex))
        {
            throw Unreadable(ex, 502, "The billing provider returned a response that could not be interpreted.");
        }
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle,
        CancellationToken cancellationToken = default)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct: cancellationToken);
            return RequireSubscription(response);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (Exception ex) when (IsUnreadableResponse(ex))
        {
            throw Unreadable(ex, 502, "The billing provider returned a response that could not be interpreted.");
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId, ct: cancellationToken);
            if (response is null)
            {
                return Array.Empty<BillingSubscription>();
            }


            return response
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!))
                .ToList();
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<BillingSubscription>();
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (Exception ex) when (IsUnreadableResponse(ex))
        {
            throw Unreadable(ex, 502, "The billing provider returned a response that could not be interpreted.");
        }
    }

    public async Task<BillingSubscription?> GetSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId, include: null,
                ct: cancellationToken);
            return response?.Subscription is null ? null : MapSubscription(response.Subscription);
        }
        // Only a genuine 404 means "no such subscription"; any other client error is a real failure and
        // must not be reported to the caller as an absent record.
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (Exception ex) when (IsUnreadableResponse(ex))
        {
            throw Unreadable(ex, 502, "The billing provider returned a response that could not be interpreted.");
        }
    }

    public async Task<BillingComponent?> FindMeteredComponentAsync(string componentHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(componentHandle))
        {
            return null;
        }

        // The direct handle lookup is one call and does not depend on the family id being resolvable.
        var direct = await FindComponentByHandleAsync(componentHandle, cancellationToken);
        if (direct is not null)
        {
            return MapComponent(direct);
        }

        // Fall back to walking the configured family, for providers that do not offer the lookup.
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        for (var page = 1; page <= MaxPages; page++)
        {
            IReadOnlyList<ComponentResponse> pageResults;
            try
            {
                pageResults = await _client.Components.ListComponentsForProductFamily(
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
                throw Translate(ex);
            }
            catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
            {
                throw Unreachable(ex);
            }

            if (pageResults is null || pageResults.Count == 0)
            {
                break;
            }

            var match = pageResults
                .Select(r => r.Component)
                .FirstOrDefault(c => string.Equals(c?.Handle, componentHandle, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return MapComponent(match);
            }

            if (pageResults.Count < PageSize)
            {
                break;
            }
        }

        return null;
    }

    /// <summary>
    /// Looks a component up by handle across the site. Returns null when the provider does not know the
    /// handle, or does not offer the lookup at all, so the caller can fall back to the family listing.
    /// </summary>
    private async Task<Component?> FindComponentByHandleAsync(string componentHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Components.FindComponent(componentHandle, ct: cancellationToken);
            return response?.Component;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode >= 400 &&
                                                (int)ex.Error.StatusCode < 500)
        {
            return null;
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (Exception ex) when (IsUnreadableResponse(ex))
        {
            throw Unreadable(ex, 502, "The billing provider returned a response that could not be interpreted.");
        }
    }

    public async Task<UsageReceipt> RecordUsageAsync(int subscriptionId, string componentHandle, decimal quantity,
        string? memo, CancellationToken cancellationToken = default)
    {
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
            var response = await _client.SubscriptionComponents.CreateUsage(
                subscriptionIdOrReference: SubscriptionIdOrReference.Int(subscriptionId),
                componentId: ComponentIdModel.String(HandlePrefix + componentHandle),
                body: body,
                ct: cancellationToken);

            if (response?.Usage is null)
            {
                throw new BillingProviderException(502,
                    "The billing provider accepted the usage but returned no usage record.");
            }

            return MapUsage(response.Usage, subscriptionId, componentHandle, quantity);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (Exception ex) when (IsUnreadableResponse(ex))
        {
            throw Unreadable(ex, 502, "The billing provider returned a response that could not be interpreted.");
        }
    }

    public async Task<decimal?> GetComponentUnitBalanceAsync(int subscriptionId, int componentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionComponents.ReadSubscriptionComponent(subscriptionId,
                componentId, ct: cancellationToken);
            return response?.Component?.UnitBalance;
        }
        catch (SdkException<ReadSubscriptionComponentError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (Exception ex) when (IsUnreadableResponse(ex))
        {
            throw Unreadable(ex, 502, "The billing provider returned a response that could not be interpreted.");
        }
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        var body = new SubscriptionMigrationPreviewRequest
        {
            Migration = new SubscriptionMigrationPreviewOptions
            {
                ProductHandle = targetPlanHandle
            }
        };

        try
        {
            var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(subscriptionId,
                body, ct: cancellationToken);

            var migration = response?.Migration;
            if (migration is null)
            {
                throw new BillingProviderException(502,
                    "The billing provider returned no proration preview for this plan change.");
            }

            return new PlanChangePreview
            {
                SubscriptionId = subscriptionId,
                TargetPlanHandle = targetPlanHandle,
                ProratedAdjustment = FromCents(migration.ProratedAdjustmentInCents),
                Charge = FromCents(migration.ChargeInCents),
                PaymentDue = FromCents(migration.PaymentDueInCents),
                CreditApplied = FromCents(migration.CreditAppliedInCents)
            };
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (Exception ex) when (IsUnreadableResponse(ex))
        {
            throw Unreadable(ex, 502, "The billing provider returned a response that could not be interpreted.");
        }
    }

    public async Task<BillingSubscription> ChangePlanNowAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        var body = new SubscriptionProductMigrationRequest
        {
            Migration = new SubscriptionProductMigration
            {
                ProductHandle = targetPlanHandle
            }
        };

        try
        {
            var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(subscriptionId, body,
                ct: cancellationToken);
            return RequireSubscription(response);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (Exception ex) when (IsUnreadableResponse(ex))
        {
            throw Unreadable(ex, 502, "The billing provider returned a response that could not be interpreted.");
        }
    }

    public async Task<BillingSubscription> ChangePlanAtRenewalAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken cancellationToken = default)
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
            var response = await _client.Subscriptions.UpdateSubscription(subscriptionId, body,
                ct: cancellationToken);
            return RequireSubscription(response);
        }
        catch (SdkException<UpdateSubscriptionError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (Exception ex) when (IsUnreadableResponse(ex))
        {
            throw Unreadable(ex, 502, "The billing provider returned a response that could not be interpreted.");
        }
    }

    public async Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.PauseSubscription(subscriptionId, body: null,
                ct: cancellationToken);
            return RequireSubscription(response);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (Exception ex) when (IsUnreadableResponse(ex))
        {
            throw Unreadable(ex, 502, "The billing provider returned a response that could not be interpreted.");
        }
    }

    public async Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ResumeSubscription(subscriptionId,
                calendarBillingResumptionCharge: null, ct: cancellationToken);
            return RequireSubscription(response);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (Exception ex) when (IsUnreadableResponse(ex))
        {
            throw Unreadable(ex, 502, "The billing provider returned a response that could not be interpreted.");
        }
    }

    public async Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, string? reason,
        CancellationToken cancellationToken = default)
    {
        var body = new CancellationRequest
        {
            Subscription = new CancellationOptions
            {
                CancellationMessage = reason
            }
        };

        try
        {
            var response = await _client.SubscriptionStatus.CancelSubscription(subscriptionId, body,
                ct: cancellationToken);
            return RequireSubscription(response);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (Exception ex) when (IsUnreadableResponse(ex))
        {
            throw Unreadable(ex, 502, "The billing provider returned a response that could not be interpreted.");
        }
    }

    public async Task<BillingSubscription> CancelSubscriptionAtEndOfPeriodAsync(int subscriptionId, string? reason,
        CancellationToken cancellationToken = default)
    {
        var body = new CancellationRequest
        {
            Subscription = new CancellationOptions
            {
                CancellationMessage = reason,
                CancelAtEndOfPeriod = true
            }
        };

        try
        {
            // This operation answers with a bare message, not the subscription, so the new state has to be
            // read back before it can be reported to the caller.
            await _client.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId, body,
                ct: cancellationToken);
        }
        catch (SdkException<InitiateDelayedCancellationError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (Exception ex) when (IsUnreadableResponse(ex))
        {
            throw Unreadable(ex, 502, "The billing provider returned a response that could not be interpreted.");
        }

        var subscription = await GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            throw new BillingProviderException(502,
                $"Cancellation was scheduled for subscription {subscriptionId} but it could not be read back.");
        }

        return subscription;
    }

    public async Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, body: null,
                ct: cancellationToken);
            return RequireSubscription(response);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (Exception ex) when (IsUnreadableResponse(ex))
        {
            throw Unreadable(ex, 502, "The billing provider returned a response that could not be interpreted.");
        }
    }

    /// <summary>
    /// Resolves the numeric product family id. Provider ids are reassigned on a re-seed, so the configured
    /// handle is the durable identifier and an explicitly configured id is only a shortcut.
    /// </summary>
    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_resolvedProductFamilyId.HasValue)
        {
            return _resolvedProductFamilyId.Value;
        }

        if (_settings.ProductFamilyId is > 0)
        {
            _resolvedProductFamilyId = _settings.ProductFamilyId.Value;
            return _resolvedProductFamilyId.Value;
        }

        var handle = _settings.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingConfigurationException(
                $"Neither '{MaxioSettings.ConfigurationSection}:ProductFamilyId' nor '{MaxioSettings.ConfigurationSection}:ProductFamilyHandle' is configured.");
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
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (Exception ex) when (IsUnreadableResponse(ex))
        {
            throw Unreadable(ex, 502, "The billing provider returned a response that could not be interpreted.");
        }

        var match = families?
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => string.Equals(f?.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is null)
        {
            throw new BillingConfigurationException(
                $"Product family '{handle}' does not exist at the billing provider. Seed the product family before using the subscription features.");
        }

        _resolvedProductFamilyId = match.Id.Value;
        return _resolvedProductFamilyId.Value;
    }

    private static BillingSubscription RequireSubscription(SubscriptionResponse? response)
    {
        if (response?.Subscription is null)
        {
            throw new BillingProviderException(502,
                "The billing provider returned no subscription for this operation.");
        }

        return MapSubscription(response.Subscription);
    }

    private static BillingPlan MapPlan(Product product)
    {
        return new BillingPlan
        {
            Id = product.Id ?? 0,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            Price = FromCents(product.PriceInCents),
            Interval = product.Interval ?? 0,
            IntervalUnit = product.IntervalUnit?.Value,
            ProductFamilyHandle = product.ProductFamily?.Handle,
            RequiresPaymentMethod = product.RequireCreditCard ?? false,
            IsArchived = product.ArchivedAt.HasValue
        };
    }

    private static BillingCustomer MapCustomer(Customer customer)
    {
        return new BillingCustomer
        {
            Id = customer.Id ?? 0,
            Reference = customer.Reference,
            Email = customer.Email,
            FirstName = customer.FirstName,
            LastName = customer.LastName
        };
    }

    private static BillingSubscription MapSubscription(Subscription subscription)
    {
        return new BillingSubscription
        {
            Id = subscription.Id ?? 0,
            State = MapState(subscription.State),
            ProviderState = subscription.State?.Value,
            CustomerId = subscription.Customer?.Id ?? 0,
            CustomerReference = subscription.Customer?.Reference,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PlanPrice = FromCents(subscription.Product?.PriceInCents),
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingAt = subscription.NextAssessmentAt,
            Balance = FromCents(subscription.BalanceInCents),
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod ?? false,
            ScheduledCancellationAt = subscription.ScheduledCancellationAt ?? subscription.DelayedCancelAt,
            PendingPlanHandle = subscription.NextProductHandle
        };
    }

    private static BillingComponent MapComponent(Component component)
    {
        var kind = component.Kind?.Value;

        return new BillingComponent
        {
            Id = component.Id ?? 0,
            Handle = component.Handle ?? string.Empty,
            Name = component.Name,
            Kind = kind,
            IsMetered = string.Equals(kind, "metered_component", StringComparison.OrdinalIgnoreCase),
            UnitPrice = ResolveUnitPrice(component),
            PricingScheme = component.PricingScheme?.Value,
            UnitName = component.UnitName
        };
    }

    private static UsageReceipt MapUsage(Usage usage, int subscriptionId, string componentHandle,
        decimal requestedQuantity)
    {
        return new UsageReceipt
        {
            Id = usage.Id ?? 0,
            SubscriptionId = usage.SubscriptionId ?? subscriptionId,
            ComponentId = usage.ComponentId ?? 0,
            ComponentHandle = usage.ComponentHandle ?? componentHandle,
            Quantity = ReadQuantity(usage.Quantity) ?? requestedQuantity,
            Memo = usage.Memo,
            RecordedAt = usage.CreatedAt
        };
    }

    /// <summary>
    /// Usage quantities are written as a number but read back as an int-or-string union.
    /// </summary>
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

    /// <summary>
    /// Prefers the numeric cents field; the textual unit price is only parsed as a fallback.
    /// </summary>
    private static decimal ResolveUnitPrice(Component component)
    {
        if (component.PricePerUnitInCents.HasValue)
        {
            return FromCents(component.PricePerUnitInCents);
        }

        if (!string.IsNullOrWhiteSpace(component.UnitPrice) &&
            decimal.TryParse(component.UnitPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0m;
    }

    /// <summary>
    /// Every money field the provider reports is in minor units (cents); the domain speaks major units.
    /// </summary>
    private static decimal FromCents(long? cents) => (cents ?? 0L) / 100m;

    private static BillingSubscriptionState MapState(MaxioAdvancedBilling.Models.Enums.SubscriptionState? state)
    {
        return state?.Value switch
        {
            "active" => BillingSubscriptionState.Active,
            "trialing" => BillingSubscriptionState.Trialing,
            "pending" or "awaiting_signup" or "assessing" => BillingSubscriptionState.Pending,
            "past_due" or "soft_failure" => BillingSubscriptionState.PastDue,
            // Maxio exposes both "on_hold" and "paused"; the pause endpoint yields "on_hold".
            "on_hold" or "paused" => BillingSubscriptionState.Paused,
            "canceled" => BillingSubscriptionState.Canceled,
            "expired" => BillingSubscriptionState.Expired,
            "unpaid" => BillingSubscriptionState.Unpaid,
            "suspended" => BillingSubscriptionState.Suspended,
            "trial_ended" => BillingSubscriptionState.TrialEnded,
            "failed_to_create" => BillingSubscriptionState.Failed,
            _ => BillingSubscriptionState.Unknown
        };
    }

    /// <summary>
    /// The customer-by-reference lookup is the one call whose not-found status the provider does not
    /// document consistently, so 404 and any other empty-bodied 4xx both count as "no such customer".
    /// A 4xx that carries a body is a real failure and must not be mistaken for an absent record.
    /// </summary>
    private static bool IsNotFound(RawError error)
    {
        if (error.StatusCode == HttpStatusCode.NotFound)
        {
            return true;
        }

        var status = (int)error.StatusCode;
        if (status is < 400 or >= 500)
        {
            return false;
        }

        try
        {
            return string.IsNullOrWhiteSpace(error.ReadAsString());
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// A transport-level failure is not an <c>SdkException</c>, so every call site must catch it separately.
    /// A cancellation the caller asked for is deliberately left to propagate.
    /// </summary>
    private static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return exception is HttpRequestException
            or TaskCanceledException
            or TimeoutException
            or System.IO.IOException;
    }

    /// <summary>
    /// The provider answered with something the generated models cannot represent. That is a failed call,
    /// so it must not escape as an unhandled parse error.
    /// </summary>
    private static bool IsUnreadableResponse(Exception exception) => exception is JsonException;

    /// <summary>
    /// The provider answered, but neither the payload nor its error model could be read. The call did not
    /// succeed, so it must still surface as a typed failure rather than an unhandled parse error.
    /// </summary>
    private BillingProviderException Unreadable(Exception exception, int statusCode, string message)
    {
        _logger.LogWarning($"The billing provider response could not be interpreted: {exception.GetType().Name}.");
        return new BillingProviderException(statusCode, message, exception);
    }

    private BillingProviderException Unreachable(Exception exception)
    {
        _logger.LogWarning($"The billing provider could not be reached: {exception.GetType().Name}.");
        return new BillingProviderException(503, "The billing provider is currently unreachable.", exception);
    }

    private BillingProviderException Translate(SdkException<RawError> exception) =>
        FromRawError(exception.Error, exception);

    private BillingProviderException Translate(SdkException<ListProductsForProductFamilyError> exception)
    {
        if (exception.Error.TryGetString(out var message))
        {
            return Build(404, message, exception);
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            return FromRawError(raw, exception);
        }

        return Build(502, "The billing provider could not list the plans for this product family.", exception);
    }

    private BillingProviderException Translate(SdkException<CreateCustomerError> exception)
    {
        // The generated 422 payload for this operation cannot carry customer validation text, so a generic
        // message is used rather than echoing an empty model.
        if (exception.Error.TryGetCustomerErrorResponse1(out var typed))
        {
            var details = Join(typed?.Errors?.PerPage).Length > 0
                ? Join(typed?.Errors?.PerPage)
                : Join(typed?.Errors?.PricePoint);

            return Build(422,
                details.Length > 0 ? details : "The customer could not be created — validation failed.",
                exception);
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            return FromRawError(raw, exception);
        }

        return Build(502, "The customer could not be created.", exception);
    }

    private BillingProviderException Translate(SdkException<CreateSubscriptionError> exception)
    {
        if (exception.Error.TryGetErrorListResponse1(out var list))
        {
            return Build(422, Join(list?.Errors), exception);
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            return FromRawError(raw, exception);
        }

        return Build(502, "The subscription could not be created.", exception);
    }

    private BillingProviderException Translate(SdkException<CreateUsageError> exception)
    {
        if (exception.Error.TryGetErrorListResponse1(out var list))
        {
            return Build(422, Join(list?.Errors), exception);
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            return FromRawError(raw, exception);
        }

        return Build(502, "The usage could not be recorded.", exception);
    }

    private BillingProviderException Translate(SdkException<ReadSubscriptionComponentError> exception)
    {
        if (exception.Error.TryGetNoContent(out var notFound))
        {
            return FromRawError(notFound, exception, 404);
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            return FromRawError(raw, exception);
        }

        return Build(502, "The component balance could not be read.", exception);
    }

    private BillingProviderException Translate(SdkException<PreviewSubscriptionProductMigrationError> exception)
    {
        if (exception.Error.TryGetErrorListResponse1(out var list))
        {
            return Build(422, Join(list?.Errors), exception);
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            return FromRawError(raw, exception);
        }

        return Build(502, "The plan change could not be previewed.", exception);
    }

    private BillingProviderException Translate(SdkException<MigrateSubscriptionProductError> exception)
    {
        if (exception.Error.TryGetErrorListResponse1(out var list))
        {
            return Build(422, Join(list?.Errors), exception);
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            return FromRawError(raw, exception);
        }

        return Build(502, "The plan change could not be applied.", exception);
    }

    private BillingProviderException Translate(SdkException<UpdateSubscriptionError> exception)
    {
        if (exception.Error.TryGetErrorListResponse1(out var list))
        {
            return Build(422, Join(list?.Errors), exception);
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            return FromRawError(raw, exception);
        }

        return Build(502, "The subscription could not be updated.", exception);
    }

    private BillingProviderException Translate(SdkException<PauseSubscriptionError> exception)
    {
        if (exception.Error.TryGetErrorListResponse1(out var list))
        {
            return Build(422, Join(list?.Errors), exception);
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            return FromRawError(raw, exception);
        }

        return Build(502, "The subscription could not be paused.", exception);
    }

    private BillingProviderException Translate(SdkException<ResumeSubscriptionError> exception)
    {
        if (exception.Error.TryGetErrorListResponse1(out var list))
        {
            return Build(422, Join(list?.Errors), exception);
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            return FromRawError(raw, exception);
        }

        return Build(502, "The subscription could not be resumed.", exception);
    }

    private BillingProviderException Translate(SdkException<ReactivateSubscriptionError> exception)
    {
        if (exception.Error.TryGetErrorListResponse1(out var list))
        {
            return Build(422, Join(list?.Errors), exception);
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            return FromRawError(raw, exception);
        }

        return Build(502, "The subscription could not be reactivated.", exception);
    }

    private BillingProviderException Translate(SdkException<InitiateDelayedCancellationError> exception)
    {
        if (exception.Error.TryGetNoContent(out var notFound))
        {
            return FromRawError(notFound, exception, 404);
        }

        if (exception.Error.TryGetErrorListResponse1(out var list))
        {
            return Build(422, Join(list?.Errors), exception);
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            return FromRawError(raw, exception);
        }

        return Build(502, "The end-of-period cancellation could not be scheduled.", exception);
    }

    private BillingProviderException Translate(SdkException<CancelSubscriptionApiError> exception)
    {
        if (exception.Error.TryGetNoContent(out var notFound))
        {
            return FromRawError(notFound, exception, 404);
        }

        if (exception.Error.TryGetCancelSubscriptionErrorResponse(out var union))
        {
            if (union is not null && union.TryGetErrorListResponse1(out var list))
            {
                return Build(422, Join(list?.Errors), exception);
            }

            if (union is not null && union.TryGetSingleErrorResponse1(out var single))
            {
                return Build(422, single?.Error ?? "The subscription could not be cancelled.", exception);
            }
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            return FromRawError(raw, exception);
        }

        return Build(502, "The subscription could not be cancelled.", exception);
    }

    private BillingProviderException FromRawError(RawError error, Exception exception, int? statusOverride = null)
    {
        var status = statusOverride ?? (int)error.StatusCode;

        string body;
        try
        {
            body = error.ReadAsString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"The billing provider error body could not be read: {ex.GetType().Name}.");
            body = string.Empty;
        }

        return Build(status, body, exception);
    }

    private static string Join(IReadOnlyList<string>? errors) =>
        errors is null ? string.Empty : string.Join("; ", errors.Where(e => !string.IsNullOrWhiteSpace(e)));

    /// <summary>
    /// Builds the domain exception, keeping the provider's own wording but never letting the API key or an
    /// unbounded response body through.
    /// </summary>
    private BillingProviderException Build(int statusCode, string? providerMessage, Exception exception)
    {
        var message = Sanitize(providerMessage);

        return new BillingProviderException(statusCode,
            message.Length > 0
                ? $"The billing provider rejected the request ({statusCode}): {message}"
                : $"The billing provider rejected the request ({statusCode}).",
            exception);
    }

    private string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var sanitized = message.Trim();

        // Defence in depth: the key is only ever sent, never echoed, but it must not survive here if it is.
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            sanitized = sanitized.Replace(_settings.ApiKey, "***", StringComparison.Ordinal);
        }

        return sanitized.Length > MaxProviderMessageLength
            ? sanitized[..MaxProviderMessageLength]
            : sanitized;
    }
}
