using System;
using System.Collections.Generic;
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
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single Infrastructure seam that talks to Maxio Advanced Billing, via the <c>AsadAli.AdvancedBilling.Sdk</c>
/// (v4-sdk-marketplace "maxio-sdk" plugin) client. ApplicationCore depends only on <see cref="IBillingClient"/> —
/// no Maxio type, and no raw transport exception, ever crosses that boundary: every call here is wrapped so a
/// network failure or an API error alike surfaces as <see cref="BillingProviderException"/>. This class also
/// owns resolving the outbound target server: an explicit <see cref="MaxioSettings.BaseUrl"/> always wins
/// verbatim; otherwise the host is derived from <see cref="MaxioSettings.Subdomain"/> for the configured
/// region (plan §2.3).
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private const string MeteredComponentValidatedCacheKey = "Maxio:MeteredComponentValidated";
    private const string MeteredComponentIdCacheKey = "Maxio:MeteredComponentId";
    private static readonly TimeSpan ComponentCacheDuration = TimeSpan.FromMinutes(5);

    // Subscription states in which the provider considers the customer to already have a live enrollment —
    // used to make Subscribe idempotent (UC1). Mirrors MaxioAdvancedBilling.Models.Enums.SubscriptionState.
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "past_due", "assessing", "soft_failure", "unpaid", "trial_ended", "on_hold"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> options, IMemoryCache cache)
    {
        _settings = options.Value;
        _cache = cache;

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = string.Equals(_settings.Environment, "EU", StringComparison.OrdinalIgnoreCase)
                ? ServerEnvironment.Eu
                : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = _settings.ApiKey, Password = "x" }
        };

        // §2.3 hard requirement: an explicit Maxio:BaseUrl always wins, verbatim, over the Subdomain-derived
        // host, for whichever region is configured (BaseUrl and Environment/region are independent axes).
        // Only when no override is configured do we derive the host from the configured Subdomain.
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            clientOptions.Server.Production.Us.BaseUrl = _settings.BaseUrl;
            clientOptions.Server.Production.Eu.BaseUrl = _settings.BaseUrl;
        }
        else
        {
            clientOptions.Server.Production.Us.Site = _settings.Subdomain;
            clientOptions.Server.Production.Eu.Site = _settings.Subdomain;
        }

        _client = new MaxioAdvancedBillingClient(httpClient, clientOptions);
    }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            try
            {
                var products = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: $"handle:{_settings.ProductFamilyHandle}",
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    ct: ct);

                return (IReadOnlyList<BillingPlan>)products.Where(p => p.Product is not null).Select(p => MapPlan(p.Product)).ToList();
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFound))
                {
                    throw new BillingProviderException($"Maxio product family '{_settings.ProductFamilyHandle}' was not found: {notFound}");
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new BillingProviderException($"Maxio failed to list plans ({(int)raw.StatusCode}): {raw.ReadAsString()}");
                }
                throw new BillingProviderException("Maxio failed to list plans with an unrecognized error.");
            }
        }, "list plans");

    public async Task ValidateMeteredComponentAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(MeteredComponentValidatedCacheKey, out _))
        {
            return;
        }

        var component = await FindMeteredComponentAsync(ct);

        if (component.Kind != ComponentKind.MeteredComponent)
        {
            throw new BillingProviderException(
                $"Maxio component '{_settings.MeteredComponentHandle}' is of kind '{component.Kind?.Value ?? "unknown"}', not metered. Fix the seed (UC0) before recording usage.");
        }

        _cache.Set(MeteredComponentValidatedCacheKey, true, ComponentCacheDuration);
    }

    public Task<int?> FindCustomerIdByReferenceAsync(string customerReference, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            try
            {
                var response = await _client.Customers.ReadCustomerByReference(customerReference, ct);
                return response.Customer.Id;
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (SdkException<RawError> ex)
            {
                throw new BillingProviderException($"Maxio failed to look up customer '{customerReference}' ({(int)ex.Error.StatusCode}): {ex.Error.ReadAsString()}");
            }
        }, "look up customer");

    public async Task<int> EnsureCustomerAsync(string customerReference, string email, string firstName, string lastName, CancellationToken ct = default)
    {
        var existingId = await FindCustomerIdByReferenceAsync(customerReference, ct);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        return await ExecuteAsync(async () =>
        {
            try
            {
                var response = await _client.Customers.CreateCustomer(new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = string.IsNullOrWhiteSpace(firstName) ? customerReference : firstName,
                        LastName = string.IsNullOrWhiteSpace(lastName) ? "eShopOnWeb" : lastName,
                        Email = email,
                        Reference = customerReference
                    }
                }, ct);

                return response.Customer.Id ?? throw new BillingProviderException("Maxio created a customer with no id.");
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                if (ex.Error.TryGetCustomerErrorResponse1(out var errorResponse))
                {
                    throw new BillingProviderException($"Maxio rejected customer creation for '{customerReference}': {JsonSerializer.Serialize(errorResponse)}");
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new BillingProviderException($"Maxio customer creation failed ({(int)raw.StatusCode}): {raw.ReadAsString()}");
                }
                throw new BillingProviderException("Maxio customer creation failed with an unrecognized error.");
            }
        }, "create customer");
    }

    public async Task<BillingSubscription?> FindActiveSubscriptionAsync(int customerId, CancellationToken ct = default)
    {
        var subscriptions = await ListSubscriptionsForCustomerAsync(customerId, ct);
        return subscriptions.FirstOrDefault(s => LiveSubscriptionStates.Contains(s.State));
    }

    public Task<BillingSubscription> CreateSubscriptionAsync(string customerReference, string planHandle, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            try
            {
                var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerReference = customerReference,
                        // The seeded plans have "requires payment method" off (plan §UC0) — invoice collection
                        // lets the subscription activate without a card on file, matching the demo's intent
                        // that Subscribe never triggers card capture or 3-DS.
                        PaymentCollectionMethod = CollectionMethod.Invoice
                    }
                }, ct);

                return MapSubscription(response.Subscription)
                    ?? throw new BillingProviderException("Maxio created a subscription with no id.");
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    throw new BillingProviderException($"Maxio rejected the subscription: {string.Join("; ", errorList.Errors)}");
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new BillingProviderException($"Maxio subscription creation failed ({(int)raw.StatusCode}): {raw.ReadAsString()}");
                }
                throw new BillingProviderException("Maxio subscription creation failed with an unrecognized error.");
            }
        }, "create subscription");

    public Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            try
            {
                var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct);
                return (IReadOnlyList<BillingSubscription>)subscriptions
                    .Select(r => MapSubscription(r.Subscription))
                    .Where(s => s is not null)
                    .Select(s => s!)
                    .ToList();
            }
            catch (SdkException<RawError> ex)
            {
                throw new BillingProviderException($"Maxio failed to list subscriptions for customer {customerId} ({(int)ex.Error.StatusCode}): {ex.Error.ReadAsString()}");
            }
        }, "list subscriptions");

    public Task<BillingSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            try
            {
                var response = await _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct);
                return MapSubscription(response.Subscription);
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (SdkException<RawError> ex)
            {
                throw new BillingProviderException($"Maxio failed to read subscription {subscriptionId} ({(int)ex.Error.StatusCode}): {ex.Error.ReadAsString()}");
            }
        }, "read subscription");

    public Task<UsageRecord> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            try
            {
                var response = await _client.SubscriptionComponents.CreateUsage(
                    subscriptionIdOrReference: subscriptionId,
                    componentId: $"handle:{_settings.MeteredComponentHandle}",
                    body: new CreateUsageRequest
                    {
                        Usage = new CreateUsage
                        {
                            Quantity = quantity,
                            Memo = memo
                        }
                    },
                    ct: ct);

                return new UsageRecord(response.Usage.Id ?? 0, quantity, memo, null);
            }
            catch (SdkException<CreateUsageError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    throw new BillingProviderException($"Maxio rejected the usage record: {string.Join("; ", errorList.Errors)}");
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new BillingProviderException($"Maxio usage record failed ({(int)raw.StatusCode}): {raw.ReadAsString()}");
                }
                throw new BillingProviderException("Maxio usage record failed with an unrecognized error.");
            }
        }, "record usage");

    public Task<int?> GetMeteredUsageBalanceAsync(int subscriptionId, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            var componentId = await ResolveMeteredComponentIdAsync(ct);
            try
            {
                var response = await _client.SubscriptionComponents.ReadSubscriptionComponent(subscriptionId, componentId, ct);
                return response.Component?.UnitBalance;
            }
            catch (SdkException<ReadSubscriptionComponentError> ex)
            {
                if (ex.Error.TryGetNoContent(out _))
                {
                    return null;
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new BillingProviderException($"Maxio failed to read the usage balance for subscription {subscriptionId} ({(int)raw.StatusCode}): {raw.ReadAsString()}");
                }
                throw new BillingProviderException("Maxio failed to read the usage balance with an unrecognized error.");
            }
        }, "read usage balance");

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow, CancellationToken ct = default)
    {
        if (!applyNow)
        {
            // "At next renewal, without proration" has no monetary preview to fetch from the provider — the
            // change is later scheduled via UpdateSubscription(product_change_delayed: true), which prices
            // nothing until the renewal itself.
            var subscription = await GetSubscriptionAsync(subscriptionId, ct)
                ?? throw new BillingProviderException($"Maxio subscription {subscriptionId} was not found.");

            return new PlanChangePreview(
                subscriptionId,
                subscription.ProductHandle,
                targetPlanHandle,
                ApplyNow: false,
                ProratedAdjustmentInCents: 0,
                ChargeInCents: 0,
                PaymentDueInCents: 0,
                CreditAppliedInCents: 0,
                EffectiveAt: subscription.CurrentPeriodEndsAt ?? DateTimeOffset.UtcNow,
                PreviewToken: string.Empty);
        }

        return await ExecuteAsync(async () =>
        {
            try
            {
                var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(subscriptionId,
                    new SubscriptionMigrationPreviewRequest
                    {
                        Migration = new SubscriptionMigrationPreviewOptions
                        {
                            ProductHandle = targetPlanHandle,
                            PreservePeriod = true
                        }
                    }, ct);

                var migration = response.Migration;
                return new PlanChangePreview(
                    subscriptionId,
                    string.Empty,
                    targetPlanHandle,
                    ApplyNow: true,
                    migration.ProratedAdjustmentInCents ?? 0,
                    migration.ChargeInCents ?? 0,
                    migration.PaymentDueInCents ?? 0,
                    migration.CreditAppliedInCents ?? 0,
                    DateTimeOffset.UtcNow,
                    string.Empty);
            }
            catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    throw new BillingProviderException($"Maxio rejected the plan-change preview: {string.Join("; ", errorList.Errors)}");
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new BillingProviderException($"Maxio plan-change preview failed ({(int)raw.StatusCode}): {raw.ReadAsString()}");
                }
                throw new BillingProviderException("Maxio plan-change preview failed with an unrecognized error.");
            }
        }, "preview plan change");
    }

    public Task<BillingSubscription> CommitPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow, CancellationToken ct = default)
    {
        if (!applyNow)
        {
            return ExecuteAsync(async () =>
            {
                try
                {
                    var response = await _client.Subscriptions.UpdateSubscription(subscriptionId, new UpdateSubscriptionRequest
                    {
                        Subscription = new UpdateSubscription
                        {
                            ProductHandle = targetPlanHandle,
                            ProductChangeDelayed = true
                        }
                    }, ct);

                    return MapSubscription(response.Subscription)
                        ?? throw new BillingProviderException($"Maxio subscription {subscriptionId} was not found after scheduling the plan change.");
                }
                catch (SdkException<UpdateSubscriptionError> ex)
                {
                    if (ex.Error.TryGetErrorListResponse1(out var errorList))
                    {
                        throw new BillingProviderException($"Maxio rejected the delayed plan change: {string.Join("; ", errorList.Errors)}");
                    }
                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw new BillingProviderException($"Maxio delayed plan change failed ({(int)raw.StatusCode}): {raw.ReadAsString()}");
                    }
                    throw new BillingProviderException("Maxio delayed plan change failed with an unrecognized error.");
                }
            }, "schedule delayed plan change");
        }

        return ExecuteAsync(async () =>
        {
            try
            {
                var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(subscriptionId, new SubscriptionProductMigrationRequest
                {
                    Migration = new SubscriptionProductMigration
                    {
                        ProductHandle = targetPlanHandle,
                        PreservePeriod = true
                    }
                }, ct);

                return MapSubscription(response.Subscription)
                    ?? throw new BillingProviderException($"Maxio subscription {subscriptionId} was not found after the plan change.");
            }
            catch (SdkException<MigrateSubscriptionProductError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    throw new BillingProviderException($"Maxio rejected the plan change: {string.Join("; ", errorList.Errors)}");
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new BillingProviderException($"Maxio plan change failed ({(int)raw.StatusCode}): {raw.ReadAsString()}");
                }
                throw new BillingProviderException("Maxio plan change failed with an unrecognized error.");
            }
        }, "commit plan change");
    }

    public Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            try
            {
                var response = await _client.SubscriptionStatus.PauseSubscription(subscriptionId, body: null, ct);
                return MapSubscription(response.Subscription)
                    ?? throw new BillingProviderException($"Maxio subscription {subscriptionId} was not found after pausing.");
            }
            catch (SdkException<PauseSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    throw new BillingProviderException($"Maxio rejected the pause request: {string.Join("; ", errorList.Errors)}");
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new BillingProviderException($"Maxio pause failed ({(int)raw.StatusCode}): {raw.ReadAsString()}");
                }
                throw new BillingProviderException("Maxio pause failed with an unrecognized error.");
            }
        }, "pause subscription");

    public Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            try
            {
                var response = await _client.SubscriptionStatus.ResumeSubscription(subscriptionId, calendarBillingResumptionCharge: null, ct);
                return MapSubscription(response.Subscription)
                    ?? throw new BillingProviderException($"Maxio subscription {subscriptionId} was not found after resuming.");
            }
            catch (SdkException<ResumeSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    throw new BillingProviderException($"Maxio rejected the resume request: {string.Join("; ", errorList.Errors)}");
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new BillingProviderException($"Maxio resume failed ({(int)raw.StatusCode}): {raw.ReadAsString()}");
                }
                throw new BillingProviderException("Maxio resume failed with an unrecognized error.");
            }
        }, "resume subscription");

    public Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken ct = default)
    {
        var body = new CancellationRequest
        {
            Subscription = new CancellationOptions { CancellationMessage = reason }
        };

        if (endOfPeriod)
        {
            return ExecuteAsync(async () =>
            {
                try
                {
                    await _client.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId, body, ct);
                }
                catch (SdkException<InitiateDelayedCancellationError> ex)
                {
                    if (ex.Error.TryGetErrorListResponse1(out var errorList))
                    {
                        throw new BillingProviderException($"Maxio rejected the end-of-period cancellation: {string.Join("; ", errorList.Errors)}");
                    }
                    if (ex.Error.TryGetNoContent(out var notFound))
                    {
                        throw new BillingProviderException($"Maxio subscription {subscriptionId} was not found ({(int)notFound.StatusCode}).");
                    }
                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw new BillingProviderException($"Maxio end-of-period cancellation failed ({(int)raw.StatusCode}): {raw.ReadAsString()}");
                    }
                    throw new BillingProviderException("Maxio end-of-period cancellation failed with an unrecognized error.");
                }

                return await GetSubscriptionAsync(subscriptionId, ct)
                    ?? throw new BillingProviderException($"Maxio subscription {subscriptionId} was not found after scheduling cancellation.");
            }, "cancel subscription at end of period");
        }

        return ExecuteAsync(async () =>
        {
            try
            {
                var response = await _client.SubscriptionStatus.CancelSubscription(subscriptionId, body, ct);
                return MapSubscription(response.Subscription)
                    ?? throw new BillingProviderException($"Maxio subscription {subscriptionId} was not found after cancelling.");
            }
            catch (SdkException<CancelSubscriptionApiError> ex)
            {
                if (ex.Error.TryGetNoContent(out var notFound))
                {
                    throw new BillingProviderException($"Maxio subscription {subscriptionId} was not found ({(int)notFound.StatusCode}).");
                }
                if (ex.Error.TryGetCancelSubscriptionErrorResponse(out var errorResponse))
                {
                    if (errorResponse.TryGetErrorListResponse1(out var errorList))
                    {
                        throw new BillingProviderException($"Maxio rejected the cancellation: {string.Join("; ", errorList.Errors)}");
                    }
                    if (errorResponse.TryGetSingleErrorResponse1(out var singleError))
                    {
                        throw new BillingProviderException($"Maxio rejected the cancellation: {singleError.Error}");
                    }
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new BillingProviderException($"Maxio cancellation failed ({(int)raw.StatusCode}): {raw.ReadAsString()}");
                }
                throw new BillingProviderException("Maxio cancellation failed with an unrecognized error.");
            }
        }, "cancel subscription immediately");
    }

    public Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            try
            {
                var response = await _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, body: null, ct);
                return MapSubscription(response.Subscription)
                    ?? throw new BillingProviderException($"Maxio subscription {subscriptionId} was not found after reactivating.");
            }
            catch (SdkException<ReactivateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    throw new BillingProviderException($"Maxio rejected the reactivation request: {string.Join("; ", errorList.Errors)}");
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new BillingProviderException($"Maxio reactivation failed ({(int)raw.StatusCode}): {raw.ReadAsString()}");
                }
                throw new BillingProviderException("Maxio reactivation failed with an unrecognized error.");
            }
        }, "reactivate subscription");

    private Task<Component> FindMeteredComponentAsync(CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            try
            {
                var response = await _client.Components.FindComponent(_settings.MeteredComponentHandle, ct);
                return response.Component;
            }
            catch (SdkException<RawError> ex)
            {
                throw new BillingProviderException(
                    $"Maxio metered component '{_settings.MeteredComponentHandle}' could not be resolved ({(int)ex.Error.StatusCode}): {ex.Error.ReadAsString()}");
            }
        }, "resolve metered component");

    private async Task<int> ResolveMeteredComponentIdAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(MeteredComponentIdCacheKey, out int id))
        {
            return id;
        }

        var component = await FindMeteredComponentAsync(ct);
        id = component.Id ?? throw new BillingProviderException($"Maxio metered component '{_settings.MeteredComponentHandle}' has no id.");
        _cache.Set(MeteredComponentIdCacheKey, id, ComponentCacheDuration);
        return id;
    }

    /// <summary>
    /// Every call into the Maxio SDK funnels through here so a raw transport failure (DNS, TCP refusal,
    /// timeout — e.g. an unreachable <see cref="MaxioSettings.BaseUrl"/> override) is normalized into a
    /// <see cref="BillingProviderException"/> exactly like an API-level rejection, never leaking a bare
    /// <see cref="HttpRequestException"/>/<see cref="TaskCanceledException"/> across the ApplicationCore
    /// boundary. A caller-requested cancellation (via <c>ct</c>) is deliberately left alone so it still
    /// surfaces as a normal <see cref="OperationCanceledException"/>.
    /// </summary>
    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationDescription)
    {
        try
        {
            return await operation();
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Could not reach Maxio while trying to {operationDescription}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException($"Maxio timed out while trying to {operationDescription}.", ex);
        }
    }

    private static BillingPlan MapPlan(Product product) =>
        new(
            product.Handle ?? string.Empty,
            product.Name ?? product.Handle ?? string.Empty,
            product.PriceInCents ?? 0,
            product.Interval ?? 1,
            product.IntervalUnit?.Value ?? "month");

    private static BillingSubscription? MapSubscription(Subscription? subscription)
    {
        if (subscription?.Id is null)
        {
            return null;
        }

        return new BillingSubscription(
            subscription.Id.Value,
            subscription.Customer?.Reference ?? string.Empty,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            subscription.Product?.PriceInCents ?? subscription.ProductPriceInCents ?? 0,
            subscription.State?.Value ?? "unknown",
            subscription.CurrentPeriodEndsAt,
            subscription.CancelAtEndOfPeriod ?? false,
            subscription.DelayedCancelAt,
            subscription.NextProductHandle);
    }
}
