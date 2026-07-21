using System;
using System.Collections.Generic;
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
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single Infrastructure class that talks to Maxio Advanced Billing (via the maxio-sdk-clone
/// SDK), behind the provider-agnostic <see cref="IBillingClient"/> seam. Nothing else in the solution
/// references the Maxio SDK. Resolves its own outbound base URL from <see cref="MaxioSettings"/> so
/// the deployment target (prod/dev/mock) is a pure configuration change - see plan.md §2.3/§4.3.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly MeteredComponentValidationCache _componentValidationCache;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(
        HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        MeteredComponentValidationCache componentValidationCache,
        IAppLogger<MaxioBillingClient> logger)
    {
        _settings = settings.Value;
        _componentValidationCache = componentValidationCache;
        _logger = logger;

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = _settings.ApiKey, Password = "x" },
            Environment = _settings.IsEuRegion ? ServerEnvironment.Eu : ServerEnvironment.Us,
        };

        var explicitBaseUrl = _settings.ResolveBaseUrl();
        if (_settings.IsEuRegion)
        {
            clientOptions.Server.Production.Eu.Site = _settings.Subdomain;
            if (explicitBaseUrl is not null)
            {
                clientOptions.Server.Production.Eu.BaseUrl = explicitBaseUrl;
            }
        }
        else
        {
            clientOptions.Server.Production.Us.Site = _settings.Subdomain;
            if (explicitBaseUrl is not null)
            {
                clientOptions.Server.Production.Us.BaseUrl = explicitBaseUrl;
            }
        }

        _client = new MaxioAdvancedBillingClient(httpClient, clientOptions);
    }

    public async Task<IReadOnlyList<BillingPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var handles = new[] { _settings.DefaultProductHandle, _settings.AlternateProductHandle };
        var plans = new List<BillingPlan>(handles.Length);
        foreach (var handle in handles)
        {
            var product = await ReadProductByHandleAsync(handle, cancellationToken).ConfigureAwait(false);
            plans.Add(MapPlan(product));
        }

        return plans;
    }

    public Task ValidateUsageComponentAsync(CancellationToken cancellationToken = default) =>
        _componentValidationCache.EnsureValidatedAsync(() => ValidateUsageComponentInternalAsync(cancellationToken));

    public async Task<BillingCustomer> EnsureCustomerAsync(BillingCustomerProfile profile, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerByReferenceAsync(profile.Reference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        CustomerResponse response;
        try
        {
            var body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = profile.FirstName,
                    LastName = profile.LastName,
                    Email = profile.Email,
                    Reference = profile.Reference,
                },
            };
            response = await _client.Customers.CreateCustomer(body, ct: cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // The typed 422 payload (CustomerErrorResponse1.Errors) cannot carry real per-field
            // customer-validation messages for this operation - it deserializes into unrelated
            // paging/price-point fields. Go straight to the raw body instead.
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError("create customer", raw);
            }

            throw new BillingProviderException("create customer", ex.Message, null, ex);
        }

        var customer = response.Customer ?? throw new BillingProviderException("create customer", "Provider returned no customer payload.");
        return MapCustomer(customer);
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: cancellationToken).ConfigureAwait(false);
            return response.Customer is null ? null : MapCustomer(response.Customer);
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            throw MapRawError("find customer by reference", ex.Error);
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await _client.Customers.ListCustomerSubscriptions(customerId, ct: cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError("list customer subscriptions", ex.Error);
        }

        var subscriptions = new List<BillingSubscription>(responses.Count);
        foreach (var response in responses)
        {
            if (response.Subscription is not null)
            {
                subscriptions.Add(await MapSubscriptionAsync(response.Subscription, cancellationToken).ConfigureAwait(false));
            }
        }

        return subscriptions;
    }

    public async Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: cancellationToken).ConfigureAwait(false);
            if (response.Subscription is null)
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }

            return await MapSubscriptionAsync(response.Subscription, cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }

            throw MapRawError("read subscription", ex.Error);
        }
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default)
    {
        SubscriptionResponse response;
        try
        {
            // This client never sends payment-profile/card-collection attributes (see plan.md), so the
            // provider's default `automatic` payment_collection_method - which requires an on-file
            // payment method for any nonzero balance regardless of the product's require_credit_card
            // flag - must be overridden explicitly. Which non-automatic value is valid depends on the
            // site's billing architecture (CollectionMethod enum: `invoice`/`automatic` for legacy
            // Statements sites, `remittance`/`automatic`/`prepaid` for Relationship Invoicing sites), so
            // resolve it from the live site rather than hardcoding a guess.
            var collectionMethod = await ResolveNoCardCollectionMethodAsync(cancellationToken).ConfigureAwait(false);
            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId,
                    PaymentCollectionMethod = collectionMethod,
                },
            };
            response = await _client.Subscriptions.CreateSubscription(body, ct: cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new BillingProviderException("create subscription", FormatErrors(errorList), 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError("create subscription", raw);
            }

            throw new BillingProviderException("create subscription", ex.Message, null, ex);
        }

        return await RequireSubscriptionAsync(response, "create subscription", cancellationToken).ConfigureAwait(false);
    }

    public async Task<BillingUsage> RecordUsageAsync(int subscriptionId, double quantity, string? memo, CancellationToken cancellationToken = default)
    {
        await ValidateUsageComponentAsync(cancellationToken).ConfigureAwait(false);

        UsageResponse response;
        try
        {
            var body = new CreateUsageRequest
            {
                Usage = new CreateUsage { Quantity = quantity, Memo = memo },
            };
            response = await _client.SubscriptionComponents.CreateUsage(subscriptionId, _settings.MeteredComponentId, body, ct: cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new BillingProviderException("record usage", FormatErrors(errorList), 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError("record usage", raw);
            }

            throw new BillingProviderException("record usage", ex.Message, null, ex);
        }

        var usage = response.Usage ?? throw new BillingProviderException("record usage", "Provider returned no usage payload.");

        int? periodToDateBalance = null;
        try
        {
            var componentResponse = await _client.SubscriptionComponents
                .ReadSubscriptionComponent(subscriptionId, _settings.MeteredComponentId, ct: cancellationToken)
                .ConfigureAwait(false);
            periodToDateBalance = componentResponse.Component?.UnitBalance;
        }
        catch (Exception ex)
        {
            // The usage report itself already succeeded - a failed read-back is reported as an
            // unavailable balance rather than failing the whole operation (UC2 failure scenario).
            _logger.LogWarning("Failed to read back usage balance for subscription {0}: {1}", subscriptionId, ex.Message);
        }

        var quantityValue = quantity;
        if (usage.Quantity is { } reportedQuantity)
        {
            if (reportedQuantity.TryGetInt(out var quantityAsInt))
            {
                quantityValue = quantityAsInt;
            }
            else if (reportedQuantity.TryGetString(out var quantityAsString) && double.TryParse(quantityAsString, out var parsedQuantity))
            {
                quantityValue = parsedQuantity;
            }
        }

        return new BillingUsage(usage.Id ?? 0, quantityValue, usage.Memo, usage.CreatedAt, periodToDateBalance);
    }

    public async Task<BillingComponentBalance> GetUsageBalanceAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionComponents
                .ReadSubscriptionComponent(subscriptionId, _settings.MeteredComponentId, ct: cancellationToken)
                .ConfigureAwait(false);
            var component = response.Component ?? throw new BillingProviderException("read usage balance", "Provider returned no component payload.");
            return new BillingComponentBalance(component.UnitBalance ?? 0);
        }
        catch (SdkException<ReadSubscriptionComponentError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError("read usage balance", raw);
            }

            throw new BillingProviderException("read usage balance", ex.Message, null, ex);
        }
    }

    public async Task<BillingPlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default)
    {
        SubscriptionMigrationPreviewResponse response;
        try
        {
            var body = new SubscriptionMigrationPreviewRequest
            {
                Migration = new SubscriptionMigrationPreviewOptions { ProductHandle = targetPlanHandle },
            };
            response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(subscriptionId, body, ct: cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new BillingProviderException("preview plan change", FormatErrors(errorList), 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError("preview plan change", raw);
            }

            throw new BillingProviderException("preview plan change", ex.Message, null, ex);
        }

        var migration = response.Migration ?? throw new BillingProviderException("preview plan change", "Provider returned no migration preview payload.");

        return new BillingPlanChangePreview(
            targetPlanHandle,
            migration.ProratedAdjustmentInCents ?? 0,
            migration.ChargeInCents ?? 0,
            migration.PaymentDueInCents ?? 0,
            migration.CreditAppliedInCents ?? 0,
            DateTimeOffset.UtcNow);
    }

    public async Task<BillingSubscription> CommitPlanChangeNowAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default)
    {
        SubscriptionResponse response;
        try
        {
            var body = new SubscriptionProductMigrationRequest
            {
                Migration = new SubscriptionProductMigration { ProductHandle = targetPlanHandle },
            };
            response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(subscriptionId, body, ct: cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new BillingProviderException("commit plan change", FormatErrors(errorList), 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError("commit plan change", raw);
            }

            throw new BillingProviderException("commit plan change", ex.Message, null, ex);
        }

        return await RequireSubscriptionAsync(response, "commit plan change", cancellationToken).ConfigureAwait(false);
    }

    public async Task<BillingSubscription> SchedulePlanChangeAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default)
    {
        SubscriptionResponse response;
        try
        {
            var body = new UpdateSubscriptionRequest
            {
                Subscription = new UpdateSubscription { ProductHandle = targetPlanHandle, ProductChangeDelayed = true },
            };
            response = await _client.Subscriptions.UpdateSubscription(subscriptionId, body, ct: cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<UpdateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new BillingProviderException("schedule plan change", FormatErrors(errorList), 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError("schedule plan change", raw);
            }

            throw new BillingProviderException("schedule plan change", ex.Message, null, ex);
        }

        return await RequireSubscriptionAsync(response, "schedule plan change", cancellationToken).ConfigureAwait(false);
    }

    public async Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.PauseSubscription(subscriptionId, body: null, ct: cancellationToken).ConfigureAwait(false);
            return await RequireSubscriptionAsync(response, "pause subscription", cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new BillingProviderException("pause subscription", FormatErrors(errorList), 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError("pause subscription", raw);
            }

            throw new BillingProviderException("pause subscription", ex.Message, null, ex);
        }
    }

    public async Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus
                .ResumeSubscription(subscriptionId, calendarBillingResumptionCharge: null, ct: cancellationToken)
                .ConfigureAwait(false);
            return await RequireSubscriptionAsync(response, "resume subscription", cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new BillingProviderException("resume subscription", FormatErrors(errorList), 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError("resume subscription", raw);
            }

            throw new BillingProviderException("resume subscription", ex.Message, null, ex);
        }
    }

    public async Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default)
    {
        if (endOfPeriod)
        {
            try
            {
                CancellationRequest? body = reason is null
                    ? null
                    : new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = reason } };
                await _client.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId, body, ct: cancellationToken).ConfigureAwait(false);
            }
            catch (SdkException<InitiateDelayedCancellationError> ex)
            {
                if (ex.Error.TryGetNoContent(out _))
                {
                    throw new SubscriptionNotFoundException(subscriptionId);
                }

                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    throw new BillingProviderException("schedule end-of-period cancellation", FormatErrors(errorList), 422, ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw MapRawError("schedule end-of-period cancellation", raw);
                }

                throw new BillingProviderException("schedule end-of-period cancellation", ex.Message, null, ex);
            }

            // InitiateDelayedCancellation returns only a confirmation message - re-read the
            // subscription to reflect the now-updated cancel_at_end_of_period/delayed_cancel_at state.
            return await GetSubscriptionAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            // The immediate-cancel contract requires omitting all schedule params - passing a
            // CancellationOptions body here would switch this same endpoint into scheduling semantics.
            var response = await _client.SubscriptionStatus.CancelSubscription(subscriptionId, body: null, ct: cancellationToken).ConfigureAwait(false);
            return await RequireSubscriptionAsync(response, "cancel subscription", cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }

            if (ex.Error.TryGetCancelSubscriptionErrorResponse(out var cancelError))
            {
                if (cancelError.TryGetErrorListResponse1(out var errorList))
                {
                    throw new BillingProviderException("cancel subscription", FormatErrors(errorList), 422, ex);
                }

                if (cancelError.TryGetSingleErrorResponse1(out var singleError))
                {
                    throw new BillingProviderException("cancel subscription", FormatSingleError(singleError), 422, ex);
                }
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError("cancel subscription", raw);
            }

            throw new BillingProviderException("cancel subscription", ex.Message, null, ex);
        }
    }

    public async Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, body: null, ct: cancellationToken).ConfigureAwait(false);
            return await RequireSubscriptionAsync(response, "reactivate subscription", cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new BillingProviderException("reactivate subscription", FormatErrors(errorList), 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError("reactivate subscription", raw);
            }

            throw new BillingProviderException("reactivate subscription", ex.Message, null, ex);
        }
    }

    private async Task ValidateUsageComponentInternalAsync(CancellationToken cancellationToken)
    {
        Component component;
        try
        {
            var response = await _client.Components.FindComponent(_settings.MeteredComponentHandle, ct: cancellationToken).ConfigureAwait(false);
            component = response.Component ?? throw new BillingConfigurationException(
                $"Metered component handle '{_settings.MeteredComponentHandle}' did not resolve. Verify the sandbox seed (UC0).");
        }
        catch (SdkException<RawError> ex)
        {
            throw MapCatalogLookupError($"validate usage component '{_settings.MeteredComponentHandle}'", ex.Error);
        }

        if (component.Kind != ComponentKind.MeteredComponent)
        {
            throw new BillingConfigurationException(
                $"Component '{_settings.MeteredComponentHandle}' is of kind '{component.Kind}', not Metered. Archive and recreate it as Metered (UC0).");
        }
    }

    private async Task<Product> ReadProductByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Products.ReadProductByHandle(handle, ct: cancellationToken).ConfigureAwait(false);
            return response.Product ?? throw new BillingConfigurationException(
                $"Product handle '{handle}' did not resolve. Verify the sandbox seed (UC0).");
        }
        catch (SdkException<RawError> ex)
        {
            throw MapCatalogLookupError($"read product '{handle}'", ex.Error);
        }
    }

    private async Task<string?> LookupCustomerReferenceAsync(int customerId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomer(customerId, ct: cancellationToken).ConfigureAwait(false);
            return response.Customer?.Reference;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to look up customer {0} reference for ownership verification: {1}", customerId, ex.Message);
            return null;
        }
    }

    private async Task<BillingSubscription> RequireSubscriptionAsync(SubscriptionResponse response, string operation, CancellationToken cancellationToken)
    {
        var subscription = response.Subscription ?? throw new BillingProviderException(operation, "Provider returned no subscription payload.");
        return await MapSubscriptionAsync(subscription, cancellationToken).ConfigureAwait(false);
    }

    private async Task<BillingSubscription> MapSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var customerId = subscription.Customer?.Id ?? 0;
        var customerReference = subscription.Customer?.Reference;
        if (string.IsNullOrEmpty(customerReference) && customerId > 0)
        {
            customerReference = await LookupCustomerReferenceAsync(customerId, cancellationToken).ConfigureAwait(false);
        }

        return new BillingSubscription(
            subscription.Id ?? 0,
            MapState(subscription.State),
            customerId,
            customerReference ?? string.Empty,
            subscription.Product?.Id ?? 0,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            subscription.Product?.PriceInCents ?? 0,
            subscription.BalanceInCents ?? 0,
            subscription.CurrentPeriodEndsAt,
            subscription.NextProductHandle,
            subscription.CancelAtEndOfPeriod ?? false);
    }

    private static BillingPlan MapPlan(Product product) => new(
        product.Id ?? 0,
        product.Handle ?? string.Empty,
        product.Name ?? string.Empty,
        product.PriceInCents ?? 0,
        product.IntervalUnit?.Value ?? string.Empty,
        product.Interval ?? 0,
        product.RequireCreditCard ?? false);

    private static BillingCustomer MapCustomer(Customer customer) => new(
        customer.Id ?? 0,
        customer.Reference ?? string.Empty,
        customer.Email ?? string.Empty);

    private static BillingSubscriptionState MapState(SubscriptionState? state)
    {
        if (state == SubscriptionState.Pending) return BillingSubscriptionState.Pending;
        if (state == SubscriptionState.AwaitingSignup) return BillingSubscriptionState.AwaitingSignup;
        if (state == SubscriptionState.Trialing) return BillingSubscriptionState.Trialing;
        if (state == SubscriptionState.Active) return BillingSubscriptionState.Active;
        if (state == SubscriptionState.Assessing) return BillingSubscriptionState.Assessing;
        if (state == SubscriptionState.SoftFailure) return BillingSubscriptionState.SoftFailure;
        if (state == SubscriptionState.PastDue) return BillingSubscriptionState.PastDue;
        if (state == SubscriptionState.Suspended) return BillingSubscriptionState.Suspended;
        if (state == SubscriptionState.Canceled) return BillingSubscriptionState.Canceled;
        if (state == SubscriptionState.Expired) return BillingSubscriptionState.Expired;
        if (state == SubscriptionState.Paused) return BillingSubscriptionState.Paused;
        if (state == SubscriptionState.OnHold) return BillingSubscriptionState.Paused;
        if (state == SubscriptionState.Unpaid) return BillingSubscriptionState.Unpaid;
        if (state == SubscriptionState.TrialEnded) return BillingSubscriptionState.TrialEnded;
        if (state == SubscriptionState.FailedToCreate) return BillingSubscriptionState.FailedToCreate;
        return BillingSubscriptionState.Unknown;
    }

    /// <summary>
    /// Reads the site's billing architecture (<c>Sites.ReadSite</c> -&gt;
    /// <c>Site.RelationshipInvoicingEnabled</c>) to pick the non-automatic
    /// <c>payment_collection_method</c> subscriptions created without a payment profile must use:
    /// <c>invoice</c> on legacy Statements-architecture sites, <c>remittance</c> on Relationship
    /// Invoicing sites (per the <c>CollectionMethod</c> enum's documented value sets).
    /// </summary>
    private async Task<CollectionMethod> ResolveNoCardCollectionMethodAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Sites.ReadSite(ct: cancellationToken).ConfigureAwait(false);
            return response.Site?.RelationshipInvoicingEnabled == true
                ? CollectionMethod.Remittance
                : CollectionMethod.Invoice;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError("read site", ex.Error);
        }
    }

    private static string FormatErrors(ErrorListResponse1 errors) =>
        errors.Errors is { Count: > 0 } messages ? string.Join("; ", messages) : "Provider rejected the request.";

    private static string FormatSingleError(SingleErrorResponse1 error) =>
        error.Error ?? "Provider rejected the request.";

    private static BillingProviderException MapRawError(string operation, RawError error)
    {
        var body = SafeReadBody(error);
        if (error.StatusCode == HttpStatusCode.NotFound)
        {
            return new BillingProviderException(operation, $"Not found: {body}", (int)error.StatusCode);
        }

        return new BillingProviderException(operation, body, (int)error.StatusCode);
    }

    /// <summary>
    /// A missing/unresolvable configured catalog handle (product family/plan/component) is a
    /// configuration problem pointing back at UC0, not a transient provider failure - surfaced as a
    /// distinct exception type so callers (and the UI) can tell the two apart.
    /// </summary>
    private static Exception MapCatalogLookupError(string operation, RawError error)
    {
        if (error.StatusCode == HttpStatusCode.NotFound)
        {
            return new BillingConfigurationException(
                $"Billing provider could not find the entity for '{operation}'. Verify the sandbox seed (UC0). Details: {SafeReadBody(error)}");
        }

        return MapRawError(operation, error);
    }

    private static string SafeReadBody(RawError error)
    {
        try
        {
            return error.ReadAsString();
        }
        catch
        {
            return "(no response body)";
        }
    }
}
