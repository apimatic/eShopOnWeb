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
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;
using MaxioSubscriptionState = MaxioAdvancedBilling.Models.Enums.SubscriptionState;
using MaxioIntervalUnit = MaxioAdvancedBilling.Models.Enums.IntervalUnit;
using CoreSubscription = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription;
using CoreMeteredComponent = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.MeteredComponent;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single concrete class in the solution that talks to Maxio Advanced Billing (§2.2), via the
/// maxio-sdk (AsadAli.AdvancedBilling.Sdk) client. Resolves plans/family/component live by their
/// configured handles (never hard-coded numeric ids, per §1.3/§UC0) and maps every Maxio SDK type to
/// a plain ApplicationCore DTO so the domain layer never references the provider SDK.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> options)
    {
        _settings = options.Value;

        var region = string.Equals(_settings.Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? ServerEnvironment.Eu
            : ServerEnvironment.Us;

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = region,
            BasicAuth = new BasicAuthCredentials { Username = _settings.ApiKey, Password = "x" }
        };

        // Resolution order per §2.3: an explicit Maxio:BaseUrl always wins verbatim; only when it is
        // absent do we derive the host from Subdomain (+ region). This is the one place retargeting
        // (prod / dev tenant / local mock) happens - never hard-code the host.
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            if (region == ServerEnvironment.Us)
            {
                clientOptions.Server.Production.Us.BaseUrl = _settings.BaseUrl;
            }
            else
            {
                clientOptions.Server.Production.Eu.BaseUrl = _settings.BaseUrl;
            }
        }
        else
        {
            if (region == ServerEnvironment.Us)
            {
                clientOptions.Server.Production.Us.Site = _settings.Subdomain;
            }
            else
            {
                clientOptions.Server.Production.Eu.Site = _settings.Subdomain;
            }
        }

        _client = new MaxioAdvancedBillingClient(httpClient, clientOptions);
    }

    public async Task ValidateConfigurationAsync(CancellationToken ct = default)
    {
        await ResolveProductFamilyIdAsync(ct);
        await _client.Products.ReadProductByHandle(_settings.DefaultProductHandle, ct);
        await _client.Products.ReadProductByHandle(_settings.AlternateProductHandle, ct);
        await GetMeteredComponentAsync(ct);
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ProductResponse> products;
        try
        {
            // "handle:{handle}" is accepted in place of the numeric id for this path parameter (Api/ProductFamilies.cs),
            // so the family's live products resolve straight from its stable handle - no separate lookup needed.
            products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: $"handle:{_settings.ProductFamilyHandle}",
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 50,
                ct: ct);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFoundMessage))
            {
                throw new BillingConfigurationException(
                    $"Product family '{_settings.ProductFamilyHandle}' could not be resolved: {notFoundMessage}. Re-run UC0 seeding or fix Maxio:ProductFamilyHandle.", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Unable to list plans (HTTP {(int)raw.StatusCode}).", ex);
            }
            throw new BillingProviderException("Unable to list plans.", ex);
        }

        return products.Select(MapProduct).ToList();
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string userReference, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(userReference, ct);
            return MapCustomer(response.Customer, userReference);
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            throw new BillingProviderException($"Unable to look up billing customer (HTTP {(int)ex.Error.StatusCode}).", ex);
        }
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(string userReference, string email, CancellationToken ct = default)
    {
        var existing = await FindCustomerByReferenceAsync(userReference, ct);
        if (existing is not null)
        {
            return existing;
        }

        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var createBody = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = localPart,
                LastName = "eShopOnWeb",
                Email = email,
                Reference = userReference
            }
        };

        try
        {
            var created = await _client.Customers.CreateCustomer(createBody, ct);
            return MapCustomer(created.Customer, userReference)
                   ?? throw new BillingProviderException("Maxio did not return the created customer.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var validation))
            {
                throw new BillingProviderException($"Unable to create billing customer: {DescribeValidationError(validation)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Unable to create billing customer (HTTP {(int)raw.StatusCode}).", ex);
            }
            throw new BillingProviderException("Unable to create billing customer.", ex);
        }
    }

    public async Task<IReadOnlyList<CoreSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct);
            return subscriptions.Select(r => MapSubscription(r.Subscription)).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Unable to list subscriptions for customer {customerId} (HTTP {(int)ex.Error.StatusCode}).", ex);
        }
    }

    public async Task<CoreSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct = default)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                // The seeded plans have RequireCreditCard=false (UC0) so the demo subscribes without card
                // capture or 3-DS (§1.3); collecting by invoice rather than the site's automatic card charge
                // is what actually makes that true at signup time.
                PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct);
            return MapSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Unable to create subscription: {string.Join("; ", errors.Errors)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Unable to create subscription (HTTP {(int)raw.StatusCode}).", ex);
            }
            throw new BillingProviderException("Unable to create subscription.", ex);
        }
    }

    public async Task<CoreSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: ct);
            return MapSubscription(response.Subscription);
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }
            throw new BillingProviderException($"Unable to read subscription {subscriptionId} (HTTP {(int)ex.Error.StatusCode}).", ex);
        }
    }

    public async Task<CoreMeteredComponent> GetMeteredComponentAsync(CancellationToken ct = default)
    {
        ComponentResponse response;
        try
        {
            response = await _client.Components.FindComponent(_settings.MeteredComponentHandle, ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingConfigurationException(
                $"Metered component handle '{_settings.MeteredComponentHandle}' could not be resolved (HTTP {(int)ex.Error.StatusCode}). Re-run UC0 seeding or fix Maxio:MeteredComponentHandle.", ex);
        }

        var component = response.Component;
        if (component?.Id is not { } id || component.Kind != MaxioAdvancedBilling.Models.Enums.ComponentKind.MeteredComponent)
        {
            throw new BillingConfigurationException(
                $"Component '{_settings.MeteredComponentHandle}' exists but is not a metered component (kind: {component?.Kind?.Value ?? "unknown"}). Re-run UC0 seeding to recreate it as Metered.");
        }

        return new CoreMeteredComponent(id, component.Handle ?? _settings.MeteredComponentHandle, component.UnitPrice);
    }

    public async Task<UsageRecordResult> RecordUsageAsync(int subscriptionId, int componentId, int quantity, string? memo, CancellationToken ct = default)
    {
        var body = new CreateUsageRequest
        {
            Usage = new CreateUsage
            {
                Quantity = quantity,
                Memo = memo
            }
        };

        UsageResponse response;
        try
        {
            response = await _client.SubscriptionComponents.CreateUsage(subscriptionId, componentId, body, ct);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Unable to record usage: {string.Join("; ", errors.Errors)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Unable to record usage (HTTP {(int)raw.StatusCode}).", ex);
            }
            throw new BillingProviderException("Unable to record usage.", ex);
        }

        long? periodToDate = null;
        try
        {
            periodToDate = await ReadPeriodToDateUsageAsync(subscriptionId, componentId, ct);
        }
        catch
        {
            // Best-effort read-back only (UC2 failure scenarios): the usage record above already
            // succeeded, so any failure here must not fail the whole operation - just mark the total unavailable.
        }

        return new UsageRecordResult(quantity, response.Usage.Memo, response.Usage.CreatedAt, periodToDate);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken ct = default)
    {
        var body = new SubscriptionMigrationPreviewRequest
        {
            Migration = new SubscriptionMigrationPreviewOptions
            {
                ProductHandle = targetProductHandle,
                PreservePeriod = !applyImmediately
            }
        };

        try
        {
            var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(subscriptionId, body, ct);
            var preview = response.Migration;
            return new PlanChangePreview(
                targetProductHandle,
                applyImmediately,
                preview.ProratedAdjustmentInCents ?? 0,
                preview.ChargeInCents ?? 0,
                preview.PaymentDueInCents ?? 0,
                preview.CreditAppliedInCents ?? 0);
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Unable to preview plan change: {string.Join("; ", errors.Errors)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Unable to preview plan change (HTTP {(int)raw.StatusCode}).", ex);
            }
            throw new BillingProviderException("Unable to preview plan change.", ex);
        }
    }

    public async Task<CoreSubscription> CommitPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken ct = default)
    {
        var body = new SubscriptionProductMigrationRequest
        {
            Migration = new SubscriptionProductMigration
            {
                ProductHandle = targetProductHandle,
                PreservePeriod = !applyImmediately
            }
        };

        try
        {
            var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(subscriptionId, body, ct);
            return MapSubscription(response.Subscription);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Unable to change plan: {string.Join("; ", errors.Errors)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Unable to change plan (HTTP {(int)raw.StatusCode}).", ex);
            }
            throw new BillingProviderException("Unable to change plan.", ex);
        }
    }

    public async Task<CoreSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.PauseSubscription(subscriptionId, body: null, ct: ct);
            return MapSubscription(response.Subscription);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Unable to pause subscription: {string.Join("; ", errors.Errors)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Unable to pause subscription (HTTP {(int)raw.StatusCode}).", ex);
            }
            throw new BillingProviderException("Unable to pause subscription.", ex);
        }
    }

    public async Task<CoreSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ResumeSubscription(subscriptionId, calendarBillingResumptionCharge: null, ct: ct);
            return MapSubscription(response.Subscription);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Unable to resume subscription: {string.Join("; ", errors.Errors)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Unable to resume subscription (HTTP {(int)raw.StatusCode}).", ex);
            }
            throw new BillingProviderException("Unable to resume subscription.", ex);
        }
    }

    public async Task<CoreSubscription> CancelSubscriptionAsync(int subscriptionId, bool cancelAtEndOfPeriod, string? reason, CancellationToken ct = default)
    {
        var body = new CancellationRequest
        {
            Subscription = new CancellationOptions
            {
                CancelAtEndOfPeriod = cancelAtEndOfPeriod,
                CancellationMessage = reason
            }
        };

        try
        {
            var response = await _client.SubscriptionStatus.CancelSubscription(subscriptionId, body, ct);
            return MapSubscription(response.Subscription);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }
            if (ex.Error.TryGetCancelSubscriptionErrorResponse(out var validation))
            {
                throw new BillingProviderException($"Unable to cancel subscription: {DescribeCancelError(validation)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Unable to cancel subscription (HTTP {(int)raw.StatusCode}).", ex);
            }
            throw new BillingProviderException("Unable to cancel subscription.", ex);
        }
    }

    public async Task<CoreSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, body: null, ct: ct);
            return MapSubscription(response.Subscription);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Unable to reactivate subscription: {string.Join("; ", errors.Errors)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Unable to reactivate subscription (HTTP {(int)raw.StatusCode}).", ex);
            }
            throw new BillingProviderException("Unable to reactivate subscription.", ex);
        }
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Unable to list product families (HTTP {(int)ex.Error.StatusCode}).", ex);
        }

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null && string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is not { } id)
        {
            throw new BillingConfigurationException(
                $"No product family with handle '{_settings.ProductFamilyHandle}' was found on the configured Maxio site. Re-run UC0 seeding or fix Maxio:ProductFamilyHandle.");
        }

        return id;
    }

    private async Task<long?> ReadPeriodToDateUsageAsync(int subscriptionId, int componentId, CancellationToken ct)
    {
        var subscriptionResponse = await _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: ct);
        var periodStart = subscriptionResponse.Subscription?.CurrentPeriodStartedAt;

        long total = 0;
        var page = 1;
        const int perPage = 200;
        while (true)
        {
            var usages = await _client.SubscriptionComponents.ListUsages(
                subscriptionIdOrReference: subscriptionId,
                componentId: componentId,
                sinceId: null,
                maxId: null,
                sinceDate: periodStart,
                untilDate: null,
                page: page,
                perPage: perPage,
                ct: ct);

            foreach (var usageResponse in usages)
            {
                if (usageResponse.Usage.Quantity is { } quantity)
                {
                    if (quantity.TryGetInt(out var intQuantity))
                    {
                        total += intQuantity;
                    }
                    else if (quantity.TryGetString(out var stringQuantity) && long.TryParse(stringQuantity, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        total += parsed;
                    }
                }
            }

            if (usages.Count < perPage)
            {
                break;
            }

            page++;
        }

        return total;
    }

    private static BillingPlan MapProduct(ProductResponse response)
    {
        var product = response.Product;
        return new BillingPlan(
            product.Handle ?? string.Empty,
            product.Name ?? string.Empty,
            product.PriceInCents ?? 0,
            product.Interval ?? 1,
            MapIntervalUnit(product.IntervalUnit));
    }

    private static BillingCustomer? MapCustomer(Customer? customer, string fallbackReference)
    {
        if (customer?.Id is not { } id)
        {
            return null;
        }

        return new BillingCustomer(id, customer.Reference ?? fallbackReference, customer.Email ?? string.Empty);
    }

    private static CoreSubscription MapSubscription(MaxioSubscription? subscription)
    {
        if (subscription is null)
        {
            throw new BillingProviderException("Maxio returned an empty subscription payload.");
        }

        return new CoreSubscription(
            subscription.Id ?? 0,
            subscription.Customer?.Id ?? 0,
            subscription.Customer?.Reference ?? string.Empty,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            subscription.Product?.PriceInCents ?? 0,
            MapState(subscription.State),
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt,
            subscription.CancelAtEndOfPeriod ?? false);
    }

    private static SubscriptionState MapState(MaxioSubscriptionState? state) => state?.Value switch
    {
        "pending" => SubscriptionState.Pending,
        "failed_to_create" => SubscriptionState.FailedToCreate,
        "trialing" => SubscriptionState.Trialing,
        "assessing" => SubscriptionState.Assessing,
        "active" => SubscriptionState.Active,
        "soft_failure" => SubscriptionState.SoftFailure,
        "past_due" => SubscriptionState.PastDue,
        "suspended" => SubscriptionState.Suspended,
        "canceled" => SubscriptionState.Canceled,
        "expired" => SubscriptionState.Expired,
        "paused" => SubscriptionState.Paused,
        "unpaid" => SubscriptionState.Unpaid,
        "trial_ended" => SubscriptionState.TrialEnded,
        "on_hold" => SubscriptionState.OnHold,
        "awaiting_signup" => SubscriptionState.AwaitingSignup,
        _ => SubscriptionState.Other
    };

    private static IntervalUnit MapIntervalUnit(MaxioIntervalUnit? unit) => unit?.Value switch
    {
        "day" => IntervalUnit.Day,
        _ => IntervalUnit.Month
    };

    private static string DescribeValidationError(CustomerErrorResponse1 error) =>
        error.Errors is null ? "validation failed" : System.Text.Json.JsonSerializer.Serialize(error.Errors);

    private static string DescribeCancelError(CancelSubscriptionErrorResponse response)
    {
        if (response.TryGetErrorListResponse1(out var list))
        {
            return string.Join("; ", list.Errors);
        }
        if (response.TryGetSingleErrorResponse1(out var single))
        {
            return single.Error;
        }
        return "unknown error";
    }
}
