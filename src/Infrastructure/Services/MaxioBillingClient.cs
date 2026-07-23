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

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The one class in eShopOnWeb that talks to Maxio Advanced Billing. Implements the
/// provider-agnostic <see cref="IBillingClient"/> seam over the Maxio .NET SDK, translating the
/// provider's shapes into eShopOnWeb's own domain types and its failures into typed exceptions.
/// </summary>
/// <remarks>
/// <para>
/// The outbound base URL is resolved from <see cref="MaxioSettings"/> — an explicit
/// <c>Maxio:BaseUrl</c> is honoured verbatim, otherwise the host is derived from the subdomain and
/// region. Retargeting production, a dev/sandbox tenant, or a local mock is therefore configuration
/// only, and it never leaks beyond this class.
/// </para>
/// <para>
/// The <see cref="HttpClient"/> is supplied by <c>IHttpClientFactory</c> and is neither owned nor
/// disposed here.
/// </para>
/// </remarks>
public class MaxioBillingClient : IBillingClient
{
    /// <summary>Maxio authenticates with the API key as the username and the literal "x" as the password.</summary>
    private const string ApiKeyPasswordSentinel = "x";

    /// <summary>Maxio's identifier for a component that accrues usage.</summary>
    private const string MeteredComponentKind = "metered_component";

    /// <summary>Upper bound for the plan listing; the demo family holds a handful of plans.</summary>
    private const int PlanPageSize = 200;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    /// <summary>
    /// Resolved once per client instance so usage reporting does not re-look-up the component on
    /// every call. A race merely costs a redundant lookup.
    /// </summary>
    private MeteredComponentInfo? _meteredComponent;

    public MaxioBillingClient(
        HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings.Value ?? throw new BillingConfigurationException("Maxio settings are not configured.");
        _logger = logger;
        _client = new MaxioAdvancedBillingClient(httpClient, BuildOptions(_settings));
    }

    /// <summary>
    /// Builds the SDK options, honouring the configured target server. An explicit base URL is
    /// applied verbatim — the SDK's default host is a template whose <c>{site}</c> token is only
    /// substituted when the site is what we set, so a literal URL is used exactly as given.
    /// </summary>
    private static MaxioAdvancedBillingClientOptions BuildOptions(MaxioSettings settings)
    {
        settings.Validate();

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!,
                Password = ApiKeyPasswordSentinel
            },
            Environment = settings.IsEuRegion ? ServerEnvironment.Eu : ServerEnvironment.Us
        };

        var baseUrl = settings.ResolveBaseUrl();

        // The US and EU option objects are independent; configure the one the environment selects.
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

    // ---------------------------------------------------------------------------------------
    // Plans
    // ---------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        const string operation = "list plans";
        var familyHandle = _settings.ProductFamilyHandle!;

        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: $"handle:{familyHandle}",
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: PlanPageSize,
                ct: cancellationToken);

            return products
                .Select(p => p.Product)
                .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Handle))
                .Select(MapPlan)
                .Where(p => !p.IsArchived)
                .OrderBy(p => p.Price)
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
            {
                throw new BillingConfigurationException(
                    $"Product family '{familyHandle}' does not resolve in Maxio ({notFound}). Verify the seeded product family handle.");
            }

            throw FromRawOrUnknown(operation, ex, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(operation, ex.Error, ex);
        }
        catch (Exception ex) when (IsUnusableResponse(ex, cancellationToken))
        {
            throw Unusable(operation, ex);
        }
    }

    public async Task<BillingPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default)
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
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw($"read plan '{planHandle}'", ex.Error, ex);
        }
        catch (Exception ex) when (IsUnusableResponse(ex, cancellationToken))
        {
            throw Unusable($"read plan '{planHandle}'", ex);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Customers
    // ---------------------------------------------------------------------------------------

    public async Task<BillingCustomer> EnsureCustomerAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var existing = await FindCustomerAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = subscriber.FirstName,
                    LastName = subscriber.LastName,
                    Email = subscriber.Email,
                    Reference = subscriber.Reference
                }
            }, ct: cancellationToken);

            return MapCustomer(created.Customer);
        }
        catch (SdkException<CreateCustomerError> ex) when (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            // The reference was taken between the lookup and the create (concurrent subscribe).
            // Re-read rather than surfacing a duplicate-customer error: creation is idempotent on
            // the user reference by contract.
            var raced = await FindCustomerAsync(subscriber.Reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw new BillingProviderException(
                $"Maxio rejected creating a customer for '{subscriber.Reference}' and no existing customer was found.",
                422,
                ex);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            throw FromRawOrUnknown("create customer", ex, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (JsonException ex)
        {
            // The provider rejected the create with an error body whose shape the SDK could not
            // read. Re-read before giving up: the create may still have won a concurrent race.
            var raced = await FindCustomerAsync(subscriber.Reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw Unusable("create customer", ex);
        }
        catch (Exception ex) when (IsUnusableResponse(ex, cancellationToken))
        {
            throw Unusable("create customer", ex);
        }
    }

    public async Task<BillingCustomer?> FindCustomerAsync(string userReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userReference))
        {
            return null;
        }

        try
        {
            // The lookup endpoint takes the bare reference; it is a query parameter, not an id slot.
            var response = await _client.Customers.ReadCustomerByReference(userReference, ct: cancellationToken);
            return MapCustomer(response.Customer);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw($"look up customer '{userReference}'", ex.Error, ex);
        }
        catch (Exception ex) when (IsUnusableResponse(ex, cancellationToken))
        {
            throw Unusable($"look up customer '{userReference}'", ex);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Subscriptions
    // ---------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsAsync(
        string userReference,
        CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(customer.Id, ct: cancellationToken);

            return responses
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(MapSubscription!)
                .OrderByDescending(s => s.ActivatedAt ?? DateTimeOffset.MinValue)
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw($"list subscriptions for '{userReference}'", ex.Error, ex);
        }
        catch (Exception ex) when (IsUnusableResponse(ex, cancellationToken))
        {
            throw Unusable($"list subscriptions for '{userReference}'", ex);
        }
    }

    public async Task<BillingSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: cancellationToken);
            return response.Subscription is null ? null : MapSubscription(response.Subscription);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw($"read subscription {subscriptionId}", ex.Error, ex);
        }
        catch (Exception ex) when (IsUnusableResponse(ex, cancellationToken))
        {
            throw Unusable($"read subscription {subscriptionId}", ex);
        }
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(
        int customerId,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        const string operation = "create subscription";

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = planHandle,

                    // Invoice-style collection by default, so a plan whose "requires payment method"
                    // toggle is off can be subscribed to without capturing a card. Configurable, so
                    // a site that does charge a stored payment method can select "automatic".
                    PaymentCollectionMethod = CollectionMethod.FromValue(
                        _settings.ResolvePaymentCollectionMethod())
                }
            }, ct: cancellationToken);

            return RequireSubscription(response.Subscription, operation);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw FromErrorList(operation, ex,
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (Exception ex) when (IsUnusableResponse(ex, cancellationToken))
        {
            throw Unusable(operation, ex);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Metered usage
    // ---------------------------------------------------------------------------------------

    public async Task<MeteredComponentInfo> GetMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        if (_meteredComponent is not null)
        {
            return _meteredComponent;
        }

        var handle = _settings.MeteredComponentHandle!;

        Component component;
        try
        {
            // FindComponent is a lookup endpoint: the handle is passed bare, with no "handle:" prefix.
            var response = await _client.Components.FindComponent(handle, ct: cancellationToken);
            component = response.Component;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingConfigurationException(
                $"Metered component '{handle}' does not resolve in Maxio. Seed the component on the product family before reporting usage.");
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw($"read component '{handle}'", ex.Error, ex);
        }
        catch (Exception ex) when (IsUnusableResponse(ex, cancellationToken))
        {
            throw Unusable($"read component '{handle}'", ex);
        }

        var mapped = MapComponent(component, handle);

        if (!mapped.IsMetered)
        {
            throw new BillingConfigurationException(
                $"Component '{handle}' is of kind '{mapped.Kind ?? "(none)"}' but usage can only be reported against a metered component. A component's kind cannot be changed in place — archive it and recreate it as metered.");
        }

        if (mapped.Id <= 0)
        {
            throw new BillingConfigurationException(
                $"Maxio returned no id for component '{handle}', so its usage balance cannot be read back.");
        }

        _meteredComponent = mapped;
        return mapped;
    }

    public async Task<UsageRecordResult> RecordUsageAsync(
        int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        // Refuse to meter against a component that is not actually metered — the provider would
        // otherwise fail late with a confusing error.
        var component = await GetMeteredComponentAsync(cancellationToken);

        const string operation = "record usage";
        Usage usage;

        try
        {
            var response = await _client.SubscriptionComponents.CreateUsage(
                SubscriptionIdOrReference.Int(subscriptionId),
                ComponentIdModel.String($"handle:{component.Handle}"),
                new CreateUsageRequest
                {
                    Usage = new CreateUsage
                    {
                        Quantity = (double)quantity,
                        Memo = memo
                    }
                },
                ct: cancellationToken);

            usage = response.Usage;
        }
        catch (SdkException<CreateUsageError> ex)
        {
            throw FromErrorList(operation, ex,
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (Exception ex) when (IsUnusableResponse(ex, cancellationToken))
        {
            throw Unusable(operation, ex);
        }

        // Best-effort read-back: the units are already recorded, so a failed balance read must not
        // fail the whole operation and must not trigger a resend that would double-bill.
        int? periodToDate;
        try
        {
            periodToDate = await ReadUnitBalanceAsync(subscriptionId, component.Id, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                "Usage was recorded against subscription {SubscriptionId} but the period-to-date balance could not be read back: {Message}",
                subscriptionId,
                ex.Message);
            periodToDate = null;
        }

        return new UsageRecordResult
        {
            UsageId = usage.Id ?? 0,
            SubscriptionId = usage.SubscriptionId ?? subscriptionId,
            ComponentId = usage.ComponentId ?? component.Id,
            ComponentHandle = usage.ComponentHandle ?? component.Handle,
            Quantity = ReadQuantity(usage.Quantity) ?? quantity,
            Memo = usage.Memo ?? memo,
            PeriodToDateUnits = periodToDate,
            PeriodToDateCharge = periodToDate is not null && component.UnitPrice is not null
                ? periodToDate.Value * component.UnitPrice.Value
                : null
        };
    }

    public async Task<int?> GetPeriodToDateUsageAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var component = await GetMeteredComponentAsync(cancellationToken);
        return await ReadUnitBalanceAsync(subscriptionId, component.Id, cancellationToken);
    }

    /// <summary>
    /// Reads the subscription's line item for the metered component. Usage accrues to its unit
    /// balance, which is the running period-to-date total.
    /// </summary>
    private async Task<int?> ReadUnitBalanceAsync(int subscriptionId, int componentId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.SubscriptionComponents.ReadSubscriptionComponent(
                subscriptionId, componentId, ct: cancellationToken);

            return response.Component?.UnitBalance;
        }
        catch (SdkException<ReadSubscriptionComponentError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            // The subscription has no line item for this component yet.
            return null;
        }
        catch (SdkException<ReadSubscriptionComponentError> ex)
        {
            throw FromRawOrUnknown(
                $"read usage balance for subscription {subscriptionId}",
                ex,
                ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (Exception ex) when (IsUnusableResponse(ex, cancellationToken))
        {
            throw Unusable($"read usage balance for subscription {subscriptionId}", ex);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Plan change
    // ---------------------------------------------------------------------------------------

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new BillingProviderException($"Subscription {subscriptionId} does not exist.", 404);

        var targetPlan = await FindPlanByHandleAsync(targetPlanHandle, cancellationToken)
            ?? throw new BillingConfigurationException(
                $"Target plan '{targetPlanHandle}' does not resolve in Maxio. Verify the seeded product handles.");

        // A change deferred to renewal is not prorated: the customer simply pays the new plan price
        // from the next period, so there is nothing for the provider to quote.
        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            return new PlanChangePreview
            {
                SubscriptionId = subscriptionId,
                CurrentPlanHandle = subscription.PlanHandle,
                TargetPlanHandle = targetPlanHandle,
                Timing = timing,
                ProratedAdjustment = 0m,
                Charge = 0m,
                CreditApplied = 0m,
                PaymentDue = 0m,
                TargetPlanPrice = targetPlan.Price,
                EffectiveAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt
            };
        }

        const string operation = "preview plan change";

        try
        {
            var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(
                subscriptionId,
                new SubscriptionMigrationPreviewRequest
                {
                    Migration = new SubscriptionMigrationPreviewOptions
                    {
                        ProductHandle = targetPlanHandle
                    }
                },
                ct: cancellationToken);

            var migration = response.Migration;

            return new PlanChangePreview
            {
                SubscriptionId = subscriptionId,
                CurrentPlanHandle = subscription.PlanHandle,
                TargetPlanHandle = targetPlanHandle,
                Timing = timing,
                ProratedAdjustment = FromCents(migration.ProratedAdjustmentInCents) ?? 0m,
                Charge = FromCents(migration.ChargeInCents) ?? 0m,
                CreditApplied = FromCents(migration.CreditAppliedInCents) ?? 0m,
                PaymentDue = FromCents(migration.PaymentDueInCents) ?? 0m,
                TargetPlanPrice = targetPlan.Price,
                EffectiveAt = null
            };
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            throw FromErrorList(operation, ex,
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (Exception ex) when (IsUnusableResponse(ex, cancellationToken))
        {
            throw Unusable(operation, ex);
        }
    }

    public async Task<BillingSubscription> ChangePlanAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            const string delayedOperation = "schedule plan change";

            try
            {
                var scheduled = await _client.Subscriptions.UpdateSubscription(
                    subscriptionId,
                    new UpdateSubscriptionRequest
                    {
                        Subscription = new UpdateSubscription
                        {
                            ProductHandle = targetPlanHandle,
                            ProductChangeDelayed = true
                        }
                    },
                    ct: cancellationToken);

                return RequireSubscription(scheduled.Subscription, delayedOperation);
            }
            catch (SdkException<UpdateSubscriptionError> ex)
            {
                throw FromErrorList(delayedOperation, ex,
                    ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
            catch (Exception ex) when (IsUnusableResponse(ex, cancellationToken))
            {
                throw Unusable(delayedOperation, ex);
            }

        }

        const string operation = "change plan";

        try
        {
            var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(
                subscriptionId,
                new SubscriptionProductMigrationRequest
                {
                    Migration = new SubscriptionProductMigration
                    {
                        ProductHandle = targetPlanHandle
                    }
                },
                ct: cancellationToken);

            return RequireSubscription(response.Subscription, operation);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            throw FromErrorList(operation, ex,
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (Exception ex) when (IsUnusableResponse(ex, cancellationToken))
        {
            throw Unusable(operation, ex);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------------

    public async Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        const string operation = "pause subscription";

        try
        {
            var response = await _client.SubscriptionStatus.PauseSubscription(subscriptionId, body: null, ct: cancellationToken);
            return RequireSubscription(response.Subscription, operation);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            throw FromErrorList(operation, ex,
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (Exception ex) when (IsUnusableResponse(ex, cancellationToken))
        {
            throw Unusable(operation, ex);
        }
    }

    public async Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        const string operation = "resume subscription";

        try
        {
            var response = await _client.SubscriptionStatus.ResumeSubscription(
                subscriptionId, calendarBillingResumptionCharge: null, ct: cancellationToken);

            return RequireSubscription(response.Subscription, operation);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            throw FromErrorList(operation, ex,
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (Exception ex) when (IsUnusableResponse(ex, cancellationToken))
        {
            throw Unusable(operation, ex);
        }
    }

    public async Task<BillingSubscription> CancelSubscriptionAsync(
        int subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var body = string.IsNullOrWhiteSpace(reason)
            ? null
            : new CancellationRequest
            {
                Subscription = new CancellationOptions { CancellationMessage = reason }
            };

        if (timing == CancellationTiming.EndOfPeriod)
        {
            const string delayedOperation = "schedule end-of-period cancellation";

            try
            {
                await _client.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId, body, ct: cancellationToken);
            }
            catch (SdkException<InitiateDelayedCancellationError> ex)
            {
                throw FromErrorList(delayedOperation, ex,
                    ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
            catch (Exception ex) when (IsUnusableResponse(ex, cancellationToken))
            {
                throw Unusable(delayedOperation, ex);
            }

            // The delayed-cancellation endpoint returns only a message, so the authoritative state
            // has to be re-read.
            return await GetSubscriptionAsync(subscriptionId, cancellationToken)
                ?? throw new BillingProviderException(
                    $"Subscription {subscriptionId} could not be re-read after scheduling its cancellation.", 404);
        }

        const string operation = "cancel subscription";

        try
        {
            var response = await _client.SubscriptionStatus.CancelSubscription(subscriptionId, body, ct: cancellationToken);
            return RequireSubscription(response.Subscription, operation);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            if (ex.Error.TryGetNoContent(out var notFound))
            {
                throw FromRaw(operation, notFound, ex);
            }

            if (ex.Error.TryGetCancelSubscriptionErrorResponse(out var rejected))
            {
                var message = rejected.TryGetErrorListResponse1(out var errors)
                    ? Join(errors.Errors)
                    : "the provider rejected the cancellation";

                throw new BillingProviderException($"Maxio {operation} failed: {message}", 422, ex);
            }

            throw FromRawOrUnknown(operation, ex, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (Exception ex) when (IsUnusableResponse(ex, cancellationToken))
        {
            throw Unusable(operation, ex);
        }
    }

    public async Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        const string operation = "reactivate subscription";

        try
        {
            var response = await _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, body: null, ct: cancellationToken);
            return RequireSubscription(response.Subscription, operation);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            throw FromErrorList(operation, ex,
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (Exception ex) when (IsUnusableResponse(ex, cancellationToken))
        {
            throw Unusable(operation, ex);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Mapping
    // ---------------------------------------------------------------------------------------

    private static BillingPlan MapPlan(Product product) => new()
    {
        Id = product.Id ?? 0,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description,
        Price = FromCents(product.PriceInCents) ?? 0m,
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.Value,
        ProductFamilyHandle = product.ProductFamily?.Handle,
        RequiresPaymentMethod = product.RequireCreditCard ?? product.RequestCreditCard ?? false,
        IsArchived = product.ArchivedAt is not null
    };

    private static BillingCustomer MapCustomer(Customer customer) => new()
    {
        Id = customer.Id ?? 0,
        Reference = customer.Reference,
        Email = customer.Email,
        FirstName = customer.FirstName,
        LastName = customer.LastName,
        CreatedAt = customer.CreatedAt
    };

    private static BillingSubscription MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        State = MapState(subscription.State?.Value),
        ProviderState = subscription.State?.Value,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PlanPrice = FromCents(subscription.Product?.PriceInCents),
        Balance = FromCents(subscription.BalanceInCents),
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        DelayedCancelAt = subscription.DelayedCancelAt ?? subscription.ScheduledCancellationAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod ?? false,
        NextPlanHandle = subscription.NextProductHandle,
        AutomaticallyResumeAt = subscription.AutomaticallyResumeAt
    };

    private static MeteredComponentInfo MapComponent(Component component, string configuredHandle)
    {
        var kind = component.Kind?.Value;

        return new MeteredComponentInfo
        {
            Id = component.Id ?? 0,
            Handle = component.Handle ?? configuredHandle,
            Name = component.Name ?? configuredHandle,
            UnitName = component.UnitName,
            Kind = kind,
            IsMetered = string.Equals(kind, MeteredComponentKind, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(kind, MaxioComponentKind.MeteredComponent.Value, StringComparison.OrdinalIgnoreCase),
            PricingScheme = component.PricingScheme?.Value,
            UnitPrice = ParseUnitPrice(component),
            IsArchived = component.ArchivedAt is not null || (component.Archived ?? false)
        };
    }

    /// <summary>
    /// Maps Maxio's subscription state onto eShopOnWeb's own vocabulary. Maxio reports a paused
    /// subscription as either "on_hold" or "paused" depending on how it was suspended; both mean
    /// paused here. Unrecognised values map to <see cref="SubscriptionState.Unknown"/> rather than
    /// throwing, because the provider may add states this build does not know about.
    /// </summary>
    private static SubscriptionState MapState(string? providerState) => providerState?.ToLowerInvariant() switch
    {
        "pending" => SubscriptionState.Pending,
        "awaiting_signup" => SubscriptionState.AwaitingSignup,
        "trialing" => SubscriptionState.Trialing,
        "trial_ended" => SubscriptionState.TrialEnded,
        "assessing" => SubscriptionState.Assessing,
        "active" => SubscriptionState.Active,
        "soft_failure" => SubscriptionState.SoftFailure,
        "past_due" => SubscriptionState.PastDue,
        "suspended" => SubscriptionState.Suspended,
        "on_hold" or "paused" => SubscriptionState.Paused,
        "unpaid" => SubscriptionState.Unpaid,
        "canceled" or "cancelled" => SubscriptionState.Canceled,
        "expired" => SubscriptionState.Expired,
        "failed_to_create" => SubscriptionState.FailedToCreate,
        _ => SubscriptionState.Unknown
    };

    /// <summary>
    /// Converts Maxio's minor units (cents) to decimal currency units. Uses decimal throughout —
    /// never double — so money is never subject to binary rounding.
    /// </summary>
    private static decimal? FromCents(long? cents) => cents is null ? null : cents.Value / 100m;

    /// <summary>
    /// Reads a component's per-unit price. Maxio reports this as a decimal string in currency units
    /// (not cents), so it is parsed with the invariant culture; the cents field is the fallback.
    /// </summary>
    private static decimal? ParseUnitPrice(Component component)
    {
        if (!string.IsNullOrWhiteSpace(component.UnitPrice) &&
            decimal.TryParse(component.UnitPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return FromCents(component.PricePerUnitInCents);
    }

    /// <summary>
    /// Reads back a recorded usage quantity. The provider models it as either a number or a string,
    /// so both are handled; strings are parsed with the invariant culture.
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

    private static BillingSubscription RequireSubscription(Subscription? subscription, string operation)
        => subscription is null
            ? throw new BillingProviderException($"Maxio {operation} succeeded but returned no subscription.")
            : MapSubscription(subscription);

    // ---------------------------------------------------------------------------------------
    // Error translation
    // ---------------------------------------------------------------------------------------

    private static BillingProviderException FromErrorList(
        string operation,
        Exception inner,
        ErrorListResponse1? errors,
        RawError? raw)
    {
        if (errors is not null)
        {
            return new BillingProviderException($"Maxio {operation} failed: {Join(errors.Errors)}", 422, inner);
        }

        return FromRawOrUnknown(operation, inner, raw);
    }

    private static BillingProviderException FromRawOrUnknown(string operation, Exception inner, RawError? raw)
        => raw is null
            ? new BillingProviderException($"Maxio {operation} failed: {inner.Message}", inner)
            : FromRaw(operation, raw, inner);

    private static BillingProviderException FromRaw(string operation, RawError raw, Exception inner)
        => new($"Maxio {operation} failed: {Describe(raw)}", (int)raw.StatusCode, inner);

    /// <summary>
    /// True when the provider could not be reached, or answered with something this integration
    /// cannot use. These leave the SDK as raw transport / serialization exceptions, and must never
    /// escape the seam untyped — callers only ever handle the billing exception types.
    /// </summary>
    /// <remarks>
    /// A cancellation the caller actually asked for is deliberately excluded, so it keeps
    /// propagating as cancellation rather than being reported as a billing failure.
    /// </remarks>
    private static bool IsUnusableResponse(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return ex is JsonException          // unparseable body, or an error payload of the wrong shape
            or HttpRequestException         // provider unreachable, DNS failure, connection refused
            or TaskCanceledException        // request timed out (no caller cancellation)
            or TimeoutException;
    }

    /// <summary>
    /// Wraps an unreachable provider or an unusable response as the seam's own typed failure.
    /// </summary>
    private static BillingProviderException Unusable(string operation, Exception inner)
        => new($"Maxio {operation} could not be completed: {inner.Message}", inner);

    /// <summary>
    /// Extracts the most useful description available from a raw provider error without ever
    /// letting body parsing mask the original failure — the body may not be JSON at all.
    /// </summary>
    private static string Describe(RawError raw)
    {
        try
        {
            var listed = raw.ReadAsJson<ErrorListResponse1>();
            if (listed?.Errors is { Count: > 0 })
            {
                return Join(listed.Errors);
            }
        }
        catch (Exception)
        {
            // Body was not an error list (HTML error page, empty body); fall through.
        }

        try
        {
            var body = raw.ReadAsString();
            if (!string.IsNullOrWhiteSpace(body))
            {
                return body.Length > 500 ? body[..500] : body;
            }
        }
        catch (Exception)
        {
            // Body was unreadable; the status alone still identifies the failure.
        }

        return $"HTTP {(int)raw.StatusCode}";
    }

    private static string Join(IReadOnlyList<string>? messages)
        => messages is { Count: > 0 } ? string.Join("; ", messages) : "no detail supplied";
}
