using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using SdkSubscription = MaxioAdvancedBilling.Models.Subscription;
using DomainSubscription = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single Infrastructure class that talks to Maxio Advanced Billing (via the
/// <c>AsadAli.AdvancedBilling.Sdk</c> / MaxioAdvancedBilling .NET SDK). Implements the
/// provider-agnostic <see cref="IBillingClient"/> seam; nothing else in the solution is allowed
/// to reference the Maxio SDK directly (§2.2).
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private const int UsageListPerPage = 50;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> options)
    {
        _settings = options.Value;

        var isEu = string.Equals(_settings.Environment, "EU", StringComparison.OrdinalIgnoreCase);
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = _settings.ApiKey, Password = "x" },
            Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us
        };

        // §2.3: the resolved base URL (explicit Maxio:BaseUrl override, else subdomain+region
        // derived) always replaces the SDK's whole server template — never partially, so a mock
        // server override can never be silently ignored in favor of a derived host.
        var resolvedBaseUrl = _settings.ResolveBaseUrl();
        if (isEu)
        {
            clientOptions.Server.Production.Eu.BaseUrl = resolvedBaseUrl;
        }
        else
        {
            clientOptions.Server.Production.Us.BaseUrl = resolvedBaseUrl;
        }

        _client = new MaxioAdvancedBillingClient(httpClient, clientOptions);
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var plans = new List<BillingPlan>();
        var page = 1;
        while (true)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: _settings.ProductFamilyId.ToString(CultureInfo.InvariantCulture),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: null,
                    include: null,
                    page: page,
                    perPage: UsageListPerPage,
                    ct: cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                throw ex.Error.TryGetString(out var notFoundMessage)
                    ? new BillingProviderException(notFoundMessage, 404)
                    : Fallback(ex.Error);
            }

            plans.AddRange(batch.Select(r => MapPlan(r.Product)));

            if (batch.Count < UsageListPerPage)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<BillingComponentInfo> GetMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        ComponentResponse response;
        try
        {
            response = await _client.Components.FindComponent(_settings.MeteredComponentHandle, cancellationToken);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new BillingConfigurationException(
                $"Metered component handle '{_settings.MeteredComponentHandle}' was not found on the billing provider. Re-run UC0 seeding.");
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error);
        }

        var component = response.Component;
        return new BillingComponentInfo(component.Handle ?? _settings.MeteredComponentHandle, component.Kind == ComponentKind.MeteredComponent);
    }

    public async Task EnsureCustomerAsync(string customerReference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        if (await TryResolveCustomerIdAsync(customerReference, email, cancellationToken) is not null)
        {
            return;
        }

        try
        {
            await _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer { FirstName = firstName, LastName = lastName, Email = email, Reference = customerReference }
                },
                cancellationToken);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            throw ex.Error.TryGetRawError(out var raw)
                ? FromRaw(raw)
                : new BillingProviderException("Maxio rejected the customer create request (422 Unprocessable Entity).", 422);
        }
    }

    public async Task<IReadOnlyList<DomainSubscription>> ListCustomerSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var customerId = await TryResolveCustomerIdAsync(customerReference, customerReference, cancellationToken);
        if (customerId is null)
        {
            return Array.Empty<DomainSubscription>();
        }

        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId.Value, cancellationToken);
            return subscriptions.Select(r => MapSubscription(r.Subscription!, customerReference)).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error);
        }
    }

    public async Task<DomainSubscription> CreateSubscriptionAsync(string customerReference, string planHandle, CancellationToken cancellationToken = default)
    {
        ProductResponse productResponse;
        try
        {
            productResponse = await _client.Products.ReadProductByHandle(planHandle, cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound
                ? new BillingConfigurationException($"Plan handle '{planHandle}' was not found on the billing provider. Re-run UC0 seeding.")
                : FromRaw(ex.Error);
        }

        // RequireCreditCard is the hard signup gate; RequestCreditCard only means the hosted
        // signup page would show a card field, it does not block an API-driven signup that omits
        // payment fields entirely (confirmed live: the seeded plans have RequestCreditCard=true
        // but RequireCreditCard=false, and subscribe without payment fields succeeds).
        if (productResponse.Product.RequireCreditCard == true)
        {
            throw new BillingConfigurationException(
                $"Plan '{planHandle}' requires a payment method; the demo enrollment path does not collect card details. Fix the seed (UC0) so this plan does not require a payment method.");
        }

        SubscriptionResponse response;
        try
        {
            response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerReference = customerReference,
                        // Confirmed live: even with RequireCreditCard=false, Maxio's default
                        // Automatic collection method still declines signup with "No payment
                        // method was on file" when the first period's charge is due immediately
                        // and no card is attached. Invoice collection defers that charge instead
                        // of blocking activation — the correct match for "no card capture" (§UC0).
                        PaymentCollectionMethod = CollectionMethod.Invoice
                    }
                },
                cancellationToken);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw ex.Error.TryGetErrorListResponse1(out var errors) ? Describe(errors) : Fallback(ex.Error);
        }

        return MapSubscription(response.Subscription!, customerReference);
    }

    public async Task<DomainSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: cancellationToken);
            return MapSubscription(response.Subscription!, customerReference: null);
        }
        catch (SdkException<RawError> ex)
        {
            throw ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound
                ? new SubscriptionNotFoundException(subscriptionId)
                : FromRaw(ex.Error);
        }
    }

    public async Task<UsageRecordResult> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        SubscriptionIdOrReference subscriptionRef = subscriptionId;
        ComponentIdModel componentRef = _settings.MeteredComponentId;

        UsageResponse response;
        try
        {
            response = await _client.SubscriptionComponents.CreateUsage(
                subscriptionRef,
                componentRef,
                new CreateUsageRequest { Usage = new CreateUsage { Quantity = (double)quantity, Memo = memo } },
                cancellationToken);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            throw ex.Error.TryGetErrorListResponse1(out var errors) ? Describe(errors) : Fallback(ex.Error);
        }

        int? periodToDateUnits = null;
        var periodToDateAvailable = false;
        try
        {
            var componentResponse = await _client.SubscriptionComponents.ReadSubscriptionComponent(subscriptionId, _settings.MeteredComponentId, cancellationToken);
            periodToDateUnits = componentResponse.Component?.UnitBalance;
            periodToDateAvailable = true;
        }
        catch
        {
            // §UC2 failure scenarios: a failed read-back must not fail the usage report that
            // already succeeded — the caller sees PeriodToDateAvailable = false instead.
        }

        var usage = response.Usage;
        return new UsageRecordResult(
            usage.Id ?? 0,
            quantity,
            usage.CreatedAt ?? DateTimeOffset.UtcNow,
            periodToDateUnits,
            periodToDateAvailable);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow, CancellationToken cancellationToken = default)
    {
        var current = await GetSubscriptionAsync(subscriptionId, cancellationToken);

        if (!applyNow)
        {
            // No provider endpoint previews the "next renewal, no proration" path (Trap note 11):
            // it is deterministic (full price, no proration), so it is computed locally.
            var plans = await ListPlansAsync(cancellationToken);
            var targetPlan = plans.FirstOrDefault(p => string.Equals(p.Handle, targetPlanHandle, StringComparison.OrdinalIgnoreCase));
            var paymentDue = targetPlan?.Price ?? 0m;
            return new PlanChangePreview(current.PlanHandle, targetPlanHandle, applyNow: false, proratedAmount: 0m, paymentDueAmount: paymentDue, creditAppliedAmount: 0m,
                effectiveDate: current.CurrentPeriodEndsAt ?? DateTimeOffset.UtcNow);
        }

        SubscriptionMigrationPreviewResponse response;
        try
        {
            response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(
                subscriptionId,
                new SubscriptionMigrationPreviewRequest
                {
                    Migration = new SubscriptionMigrationPreviewOptions { ProductHandle = targetPlanHandle, PreservePeriod = true }
                },
                cancellationToken);
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            throw ex.Error.TryGetErrorListResponse1(out var errors) ? Describe(errors) : Fallback(ex.Error);
        }

        var migration = response.Migration;
        return new PlanChangePreview(
            current.PlanHandle,
            targetPlanHandle,
            applyNow: true,
            proratedAmount: FromCents(migration.ProratedAdjustmentInCents),
            paymentDueAmount: FromCents(migration.PaymentDueInCents),
            creditAppliedAmount: FromCents(migration.CreditAppliedInCents),
            effectiveDate: DateTimeOffset.UtcNow);
    }

    public async Task<DomainSubscription> CommitPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow, CancellationToken cancellationToken = default)
    {
        if (!applyNow)
        {
            try
            {
                var response = await _client.Subscriptions.UpdateSubscription(
                    subscriptionId,
                    new UpdateSubscriptionRequest
                    {
                        Subscription = new UpdateSubscription { ProductHandle = targetPlanHandle, ProductChangeDelayed = true }
                    },
                    cancellationToken);
                return MapSubscription(response.Subscription!, customerReference: null);
            }
            catch (SdkException<UpdateSubscriptionError> ex)
            {
                throw ex.Error.TryGetErrorListResponse1(out var errors) ? Describe(errors) : Fallback(ex.Error);
            }
        }

        try
        {
            var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(
                subscriptionId,
                new SubscriptionProductMigrationRequest
                {
                    Migration = new SubscriptionProductMigration { ProductHandle = targetPlanHandle, PreservePeriod = true }
                },
                cancellationToken);
            return MapSubscription(response.Subscription!, customerReference: null);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            throw ex.Error.TryGetErrorListResponse1(out var errors) ? Describe(errors) : Fallback(ex.Error);
        }
    }

    public async Task<DomainSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.PauseSubscription(subscriptionId, new PauseRequest(), cancellationToken);
            return MapSubscription(response.Subscription!, customerReference: null);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            throw ex.Error.TryGetErrorListResponse1(out var errors) ? Describe(errors) : Fallback(ex.Error);
        }
    }

    public async Task<DomainSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ResumeSubscription(subscriptionId, calendarBillingResumptionCharge: null, ct: cancellationToken);
            return MapSubscription(response.Subscription!, customerReference: null);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            throw ex.Error.TryGetErrorListResponse1(out var errors) ? Describe(errors) : Fallback(ex.Error);
        }
    }

    public async Task<DomainSubscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default)
    {
        var cancellationRequest = new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = reason } };

        if (endOfPeriod)
        {
            try
            {
                await _client.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId, cancellationRequest, cancellationToken);
            }
            catch (SdkException<InitiateDelayedCancellationError> ex)
            {
                if (ex.Error.TryGetNoContent(out _))
                {
                    throw new SubscriptionNotFoundException(subscriptionId);
                }

                throw ex.Error.TryGetErrorListResponse1(out var errors) ? Describe(errors) : Fallback(ex.Error);
            }

            return await GetSubscriptionAsync(subscriptionId, cancellationToken);
        }

        try
        {
            var response = await _client.SubscriptionStatus.CancelSubscription(subscriptionId, cancellationRequest, cancellationToken);
            return MapSubscription(response.Subscription!, customerReference: null);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }

            if (ex.Error.TryGetCancelSubscriptionErrorResponse(out var cancelError))
            {
                if (cancelError.TryGetErrorListResponse1(out var errors))
                {
                    throw Describe(errors);
                }

                if (cancelError.TryGetSingleErrorResponse1(out var single))
                {
                    throw new BillingProviderException(single.Error, 422);
                }
            }

            throw Fallback(ex.Error);
        }
    }

    public async Task<DomainSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, new ReactivateSubscriptionRequest(), cancellationToken);
            return MapSubscription(response.Subscription!, customerReference: null);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            throw ex.Error.TryGetErrorListResponse1(out var errors) ? Describe(errors) : Fallback(ex.Error);
        }
    }

    /// <summary>Resolves a customer's Maxio id from the app's stable reference, or null if no such customer exists yet.</summary>
    private async Task<int?> TryResolveCustomerIdAsync(string customerReference, string email, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(customerReference, cancellationToken);
            return response.Customer.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // fall through to the fuzzy email search below
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error);
        }

        IReadOnlyList<CustomerResponse> matches;
        try
        {
            matches = await _client.Customers.ListCustomers(
                direction: null, dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null,
                q: email, page: 1, perPage: 50, ct: cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error);
        }

        var exactMatches = matches.Where(m => string.Equals(m.Customer.Email, email, StringComparison.OrdinalIgnoreCase)).ToList();
        return exactMatches.Count == 1 ? exactMatches[0].Customer.Id : null;
    }

    private static BillingPlan MapPlan(Product product) => new(
        product.Handle ?? string.Empty,
        product.Name ?? string.Empty,
        FromCents(product.PriceInCents),
        product.IntervalUnit == IntervalUnit.Day ? "day" : "month",
        product.Interval ?? 1,
        product.RequireCreditCard ?? false);

    private static DomainSubscription MapSubscription(SdkSubscription sdkSubscription, string? customerReference)
    {
        var reference = customerReference ?? sdkSubscription.Customer?.Reference ?? sdkSubscription.Customer?.Email ?? string.Empty;
        return new DomainSubscription(
            sdkSubscription.Id ?? 0,
            reference,
            sdkSubscription.Product?.Handle ?? string.Empty,
            sdkSubscription.Product?.Name ?? string.Empty,
            FromCents(sdkSubscription.Product?.PriceInCents),
            MapStatus(sdkSubscription.State),
            sdkSubscription.CurrentPeriodEndsAt,
            sdkSubscription.CancelAtEndOfPeriod ?? false);
    }

    private static SubscriptionStatus MapStatus(SubscriptionState? state)
    {
        if (state is null) return SubscriptionStatus.Other;
        if (state == SubscriptionState.Pending) return SubscriptionStatus.Pending;
        if (state == SubscriptionState.AwaitingSignup) return SubscriptionStatus.AwaitingSignup;
        if (state == SubscriptionState.Trialing) return SubscriptionStatus.Trialing;
        if (state == SubscriptionState.Assessing) return SubscriptionStatus.Assessing;
        if (state == SubscriptionState.Active) return SubscriptionStatus.Active;
        if (state == SubscriptionState.SoftFailure) return SubscriptionStatus.SoftFailure;
        if (state == SubscriptionState.PastDue) return SubscriptionStatus.PastDue;
        if (state == SubscriptionState.Suspended) return SubscriptionStatus.Suspended;
        // Both wire values signal "paused" in practice (Trap note re: Paused vs OnHold).
        if (state == SubscriptionState.Paused || state == SubscriptionState.OnHold) return SubscriptionStatus.Paused;
        if (state == SubscriptionState.Unpaid) return SubscriptionStatus.Unpaid;
        if (state == SubscriptionState.TrialEnded) return SubscriptionStatus.TrialEnded;
        if (state == SubscriptionState.Canceled) return SubscriptionStatus.Canceled;
        if (state == SubscriptionState.Expired) return SubscriptionStatus.Expired;
        if (state == SubscriptionState.FailedToCreate) return SubscriptionStatus.FailedToCreate;
        return SubscriptionStatus.Other;
    }

    private static decimal FromCents(long? cents) => cents.HasValue ? cents.Value / 100m : 0m;

    private static BillingProviderException Describe(ErrorListResponse1 errors) =>
        new(string.Join("; ", errors.Errors), 422);

    private static BillingProviderException Fallback(ApiError error)
    {
        error.TryGetRawError(out var raw);
        return FromRaw(raw);
    }

    private static BillingProviderException FromRaw(RawError raw) =>
        new($"Maxio request failed ({(int)raw.StatusCode}): {raw.ReadAsString()}", (int)raw.StatusCode);
}
