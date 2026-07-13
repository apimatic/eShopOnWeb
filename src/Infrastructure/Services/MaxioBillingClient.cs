using System;
using System.Collections.Generic;
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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single class in the solution that talks to Maxio Advanced Billing, behind
/// <see cref="IBillingClient"/> (§2.2). Wraps the AsadAli.AdvancedBilling.Sdk client, resolving the
/// outbound server from <see cref="MaxioSettings"/> so the target (prod / dev / mock) is a
/// configuration change, never a code change (§2.3).
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> settingsOptions)
    {
        _settings = settingsOptions.Value;

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = _settings.IsEuEnvironment ? ServerEnvironment.Eu : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = _settings.ApiKey, Password = "x" }
        };

        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            // Explicit override wins and is honored verbatim, regardless of region (§2.3) — this is
            // the one place a mock/dev/prod host is chosen; it must never fall back to the
            // subdomain-derived host once an override is configured.
            options.Server.Production.Us.BaseUrl = _settings.BaseUrl;
            options.Server.Production.Eu.BaseUrl = _settings.BaseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = _settings.Subdomain;
            options.Server.Production.Eu.Site = _settings.Subdomain;
        }

        _client = new MaxioAdvancedBillingClient(httpClient, options);
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _settings.ProductFamilyId.ToString(),
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

            return products.Select(p => ToBillingPlan(p.Product)).ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
            {
                throw new BillingProviderException($"Configured product family {_settings.ProductFamilyId} does not resolve: {notFound}. Fix the Maxio seed (UC0).", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Wrap("list plans", raw, ex);
            }

            throw new BillingProviderException("Maxio rejected listing plans.", ex);
        }
    }

    public async Task<BillingComponent> GetMeteredUsageComponentAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Components.FindComponent(_settings.MeteredComponentHandle, ct);
            var component = response.Component;
            var kind = component.Kind?.Value;

            if (kind != "metered_component")
            {
                throw new BillingProviderException(
                    $"Configured component '{_settings.MeteredComponentHandle}' resolved to kind '{kind ?? "unknown"}', expected 'metered_component'. Fix the Maxio seed (UC0) before recording usage.");
            }

            return new BillingComponent(component.Id ?? _settings.MeteredComponentId, component.Handle ?? _settings.MeteredComponentHandle, component.Name ?? string.Empty, kind, true);
        }
        catch (SdkException<RawError> ex)
        {
            throw Wrap("resolve the metered usage component", ex.Error, ex);
        }
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct);
            return ToBillingCustomer(response.Customer);
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            throw Wrap("look up the customer", ex.Error, ex);
        }
    }

    public async Task<BillingCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = reference
                }
            }, ct);

            return ToBillingCustomer(response.Customer);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var validation))
            {
                throw new BillingProviderException($"Maxio rejected customer creation for '{reference}': {DescribeCustomerErrors(validation)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Wrap("create the customer", raw, ex);
            }

            throw new BillingProviderException($"Maxio rejected customer creation for '{reference}'.", ex);
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct);
            return subscriptions.Where(s => s.Subscription != null).Select(s => ToBillingSubscription(s.Subscription!)).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw Wrap("list the customer's subscriptions", ex.Error, ex);
        }
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle
                }
            }, ct);

            return MapSubscriptionResponse(response, "subscription creation");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                throw new BillingProviderException($"Maxio rejected enrollment in plan '{productHandle}': {DescribeErrorList(body)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Wrap($"enroll the customer in plan '{productHandle}'", raw, ex);
            }

            throw new BillingProviderException($"Maxio rejected enrollment in plan '{productHandle}'.", ex);
        }
    }

    public async Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: ct);
            return MapSubscriptionResponse(response, "subscription read");
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }

            throw Wrap($"read subscription {subscriptionId}", ex.Error, ex);
        }
    }

    public async Task<BillingPlan?> GetPlanByHandleAsync(string productHandle, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Products.ReadProductByHandle(productHandle, ct);
            return ToBillingPlan(response.Product);
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            throw Wrap($"read plan '{productHandle}'", ex.Error, ex);
        }
    }

    public async Task<BillingUsage> RecordUsageAsync(int subscriptionId, double quantity, string? memo, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionComponents.CreateUsage(
                SubscriptionIdOrReference.Int(subscriptionId),
                ComponentIdModel.Int(_settings.MeteredComponentId),
                new CreateUsageRequest
                {
                    Usage = new CreateUsage
                    {
                        Quantity = quantity,
                        Memo = memo
                    }
                },
                ct);

            return new BillingUsage(response.Usage.Id ?? 0, quantity, memo);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                throw new BillingProviderException($"Maxio rejected recording usage on subscription {subscriptionId}: {DescribeErrorList(body)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Wrap($"record usage on subscription {subscriptionId}", raw, ex);
            }

            throw new BillingProviderException($"Maxio rejected recording usage on subscription {subscriptionId}.", ex);
        }
    }

    public async Task<int?> TryGetComponentPeriodToDateUsageAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionComponents.ReadSubscriptionComponent(subscriptionId, _settings.MeteredComponentId, ct);
            return response.Component?.UnitBalance;
        }
        catch (Exception)
        {
            // Best-effort read-back only (UC2): usage was already recorded successfully, so a failure
            // here must not fail the overall operation — report the total as unavailable instead.
            return null;
        }
    }

    public async Task<BillingPlanChangePreview> PreviewPlanChangeNowAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(subscriptionId, new SubscriptionMigrationPreviewRequest
            {
                Migration = new SubscriptionMigrationPreviewOptions
                {
                    ProductHandle = targetProductHandle
                }
            }, ct);

            var migration = response.Migration;
            return new BillingPlanChangePreview(migration.ProratedAdjustmentInCents, migration.ChargeInCents, migration.PaymentDueInCents, migration.CreditAppliedInCents);
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                throw new BillingProviderException($"Maxio rejected the plan change preview for subscription {subscriptionId}: {DescribeErrorList(body)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Wrap($"preview the plan change for subscription {subscriptionId}", raw, ex);
            }

            throw new BillingProviderException($"Maxio rejected the plan change preview for subscription {subscriptionId}.", ex);
        }
    }

    public async Task<BillingSubscription> CommitPlanChangeNowAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(subscriptionId, new SubscriptionProductMigrationRequest
            {
                Migration = new SubscriptionProductMigration
                {
                    ProductHandle = targetProductHandle
                }
            }, ct);

            return MapSubscriptionResponse(response, "plan change");
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                throw new BillingProviderException($"Maxio rejected the plan change for subscription {subscriptionId}: {DescribeErrorList(body)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Wrap($"apply the plan change for subscription {subscriptionId}", raw, ex);
            }

            throw new BillingProviderException($"Maxio rejected the plan change for subscription {subscriptionId}.", ex);
        }
    }

    public async Task<BillingSubscription> SchedulePlanChangeAtRenewalAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Subscriptions.UpdateSubscription(subscriptionId, new UpdateSubscriptionRequest
            {
                Subscription = new UpdateSubscription
                {
                    ProductHandle = targetProductHandle,
                    ProductChangeDelayed = true
                }
            }, ct);

            return MapSubscriptionResponse(response, "delayed plan change");
        }
        catch (SdkException<UpdateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                throw new BillingProviderException($"Maxio rejected scheduling the plan change for subscription {subscriptionId}: {DescribeErrorList(body)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Wrap($"schedule the plan change for subscription {subscriptionId}", raw, ex);
            }

            throw new BillingProviderException($"Maxio rejected scheduling the plan change for subscription {subscriptionId}.", ex);
        }
    }

    public async Task<BillingSubscription> PauseAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.PauseSubscription(subscriptionId, body: null, ct: ct);
            return MapSubscriptionResponse(response, "pause");
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                throw new BillingProviderException($"Maxio rejected pausing subscription {subscriptionId}: {DescribeErrorList(body)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Wrap($"pause subscription {subscriptionId}", raw, ex);
            }

            throw new BillingProviderException($"Maxio rejected pausing subscription {subscriptionId}.", ex);
        }
    }

    public async Task<BillingSubscription> ResumeAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ResumeSubscription(subscriptionId, calendarBillingResumptionCharge: null, ct: ct);
            return MapSubscriptionResponse(response, "resume");
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                throw new BillingProviderException($"Maxio rejected resuming subscription {subscriptionId}: {DescribeErrorList(body)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Wrap($"resume subscription {subscriptionId}", raw, ex);
            }

            throw new BillingProviderException($"Maxio rejected resuming subscription {subscriptionId}.", ex);
        }
    }

    public async Task<BillingSubscription> CancelNowAsync(int subscriptionId, string? reason, CancellationToken ct = default)
    {
        try
        {
            var body = string.IsNullOrWhiteSpace(reason)
                ? null
                : new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = reason } };

            var response = await _client.SubscriptionStatus.CancelSubscription(subscriptionId, body, ct);
            return MapSubscriptionResponse(response, "cancellation");
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }

            if (ex.Error.TryGetCancelSubscriptionErrorResponse(out var validation))
            {
                throw new BillingProviderException($"Maxio rejected cancelling subscription {subscriptionId}: {DescribeCancelErrors(validation)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Wrap($"cancel subscription {subscriptionId}", raw, ex);
            }

            throw new BillingProviderException($"Maxio rejected cancelling subscription {subscriptionId}.", ex);
        }
    }

    public async Task<BillingSubscription> CancelAtEndOfPeriodAsync(int subscriptionId, string? reason, CancellationToken ct = default)
    {
        try
        {
            await _client.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId, new CancellationRequest
            {
                Subscription = new CancellationOptions
                {
                    CancellationMessage = reason,
                    CancelAtEndOfPeriod = true
                }
            }, ct);
        }
        catch (SdkException<InitiateDelayedCancellationError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }

            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                throw new BillingProviderException($"Maxio rejected scheduling end-of-period cancellation for subscription {subscriptionId}: {DescribeErrorList(body)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Wrap($"schedule end-of-period cancellation for subscription {subscriptionId}", raw, ex);
            }

            throw new BillingProviderException($"Maxio rejected scheduling end-of-period cancellation for subscription {subscriptionId}.", ex);
        }

        // InitiateDelayedCancellation returns only a confirmation message; re-read the subscription so
        // the caller sees the provider's authoritative state (delayed cancel timestamp, current status).
        return await GetSubscriptionAsync(subscriptionId, ct);
    }

    public async Task<BillingSubscription> ReactivateAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, new ReactivateSubscriptionRequest(), ct);
            return MapSubscriptionResponse(response, "reactivation");
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                throw new BillingProviderException($"Maxio rejected reactivating subscription {subscriptionId}: {DescribeErrorList(body)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Wrap($"reactivate subscription {subscriptionId}", raw, ex);
            }

            throw new BillingProviderException($"Maxio rejected reactivating subscription {subscriptionId}.", ex);
        }
    }

    private static BillingSubscription MapSubscriptionResponse(SubscriptionResponse response, string operation)
    {
        if (response.Subscription is null)
        {
            throw new BillingProviderException($"Maxio returned no subscription data for {operation}.");
        }

        return ToBillingSubscription(response.Subscription);
    }

    private static BillingSubscription ToBillingSubscription(Subscription s) => new(
        s.Id ?? 0,
        s.Customer?.Id ?? 0,
        s.Customer?.Reference,
        s.Product?.Id,
        s.Product?.Handle,
        s.Product?.Name,
        s.State?.Value ?? "unknown",
        s.ProductPriceInCents,
        s.CurrentPeriodEndsAt,
        s.NextAssessmentAt,
        s.DelayedCancelAt);

    private static BillingCustomer ToBillingCustomer(Customer c) => new(c.Id ?? 0, c.Reference ?? string.Empty, c.Email ?? string.Empty);

    private static BillingPlan ToBillingPlan(Product p) => new(
        p.Id ?? 0,
        p.Handle ?? string.Empty,
        p.Name ?? string.Empty,
        p.PriceInCents ?? 0,
        p.Interval ?? 1,
        p.IntervalUnit?.Value ?? "month");

    private static string DescribeErrorList(ErrorListResponse1 body) => string.Join("; ", body.Errors);

    private static string DescribeCustomerErrors(CustomerErrorResponse1 body)
    {
        var errors = body.Errors;
        if (errors is null) return "validation failed";

        var messages = (errors.PerPage ?? Enumerable.Empty<string>()).Concat(errors.PricePoint ?? Enumerable.Empty<string>());
        var joined = string.Join("; ", messages);
        return string.IsNullOrEmpty(joined) ? "validation failed" : joined;
    }

    private static string DescribeCancelErrors(CancelSubscriptionErrorResponse body)
    {
        if (body.TryGetErrorListResponse1(out var list))
        {
            return DescribeErrorList(list);
        }

        if (body.TryGetSingleErrorResponse1(out var single))
        {
            return single.Error;
        }

        return "cancellation was rejected";
    }

    private static BillingProviderException Wrap(string operation, RawError raw, Exception inner) =>
        new($"Maxio failed to {operation}: HTTP {(int)raw.StatusCode} {raw.ReadAsString()}", inner);
}
