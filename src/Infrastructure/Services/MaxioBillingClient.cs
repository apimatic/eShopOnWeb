using System;
using System.Collections.Generic;
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
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;
using MaxioSubscriptionState = MaxioAdvancedBilling.Models.Enums.SubscriptionState;
using Subscription = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription;
using SubscriptionState = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.SubscriptionState;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single integration point with Maxio Advanced Billing (§2.2/§4.2). Wraps one long-lived
/// <see cref="MaxioAdvancedBillingClient"/> (constructed once per typed-HttpClient instance, per
/// <c>AddHttpClient&lt;IBillingClient, MaxioBillingClient&gt;</c> in the composition root) and
/// normalizes every SDK error into <see cref="BillingProviderException"/> /
/// <see cref="BillingConfigurationException"/> so no Maxio SDK type ever leaks past this class.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingClient> _logger;
    private readonly SemaphoreSlim _catalogValidationLock = new(1, 1);
    private volatile bool _catalogValidated;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> options, IAppLogger<MaxioBillingClient> logger)
    {
        _settings = options.Value;
        _logger = logger;
        _client = new MaxioAdvancedBillingClient(httpClient, BuildOptions(_settings));
    }

    public async Task EnsureCatalogConfiguredAsync(CancellationToken ct = default)
    {
        if (_catalogValidated)
        {
            return;
        }

        await _catalogValidationLock.WaitAsync(ct);
        try
        {
            if (_catalogValidated)
            {
                return;
            }

            await ReadProductFamilyInternalAsync(ct);

            var defaultProduct = await ReadProductInternalAsync(_settings.DefaultProductId, ct);
            if (defaultProduct.RequireCreditCard == true)
            {
                throw new BillingConfigurationException(
                    $"Configured plan '{_settings.DefaultProductHandle}' requires a payment method; the demo expects no card capture. Fix the seed (UC0).");
            }

            var alternateProduct = await ReadProductInternalAsync(_settings.AlternateProductId, ct);
            if (alternateProduct.RequireCreditCard == true)
            {
                throw new BillingConfigurationException(
                    $"Configured plan '{_settings.AlternateProductHandle}' requires a payment method; the demo expects no card capture. Fix the seed (UC0).");
            }

            var component = await ReadComponentInternalAsync(ct);
            if (component.Kind is null || component.Kind.Value != ComponentKind.MeteredComponent)
            {
                throw new BillingConfigurationException(
                    $"Configured component '{_settings.MeteredComponentHandle}' is of kind '{component.Kind}', not metered. Fix the seed (UC0).");
            }

            _catalogValidated = true;
        }
        finally
        {
            _catalogValidationLock.Release();
        }
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

            return products
                .Where(p => p.Product is not null)
                .Select(p => MapPlan(p.Product!))
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            var detail = ex.Error.TryGetString(out var notFoundMessage) ? notFoundMessage : DescribeFallback(ex.Error);
            throw new BillingProviderException($"Maxio list plans failed: {detail}", ex);
        }
    }

    public async Task<BillingPlan?> FindPlanByHandleAsync(string productHandle, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Products.ReadProductByHandle(productHandle, ct);
            return response.Product is null ? null : MapPlan(response.Product);
        }
        catch (SdkException<RawError> ex) when (IsNotFound(ex.Error))
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapRaw("find plan by handle", ex);
        }
    }

    public async Task<int> GetOrCreateCustomerAsync(string customerReference, string email, string firstName, string lastName, CancellationToken ct = default)
    {
        var existing = await FindCustomerByReferenceAsync(customerReference, ct);
        if (existing is not null)
        {
            return existing.Value;
        }

        try
        {
            var request = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = customerReference
                }
            };

            var response = await _client.Customers.CreateCustomer(request, ct);
            var customer = response.Customer ?? throw new BillingProviderException("Maxio create customer succeeded but returned no customer body.");
            return customer.Id ?? throw new BillingProviderException("Maxio create customer succeeded but returned no customer id.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // The typed 422 body's fields (per_page/price_point) are a confirmed generation mismatch
            // for this operation (maxio-plan.md §2.2) — always surface the raw fallback text instead.
            throw new BillingProviderException($"Maxio create customer failed: {DescribeFallback(ex.Error)}", ex);
        }
    }

    public async Task<int?> FindCustomerByReferenceAsync(string customerReference, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(customerReference, ct);
            return response.Customer?.Id;
        }
        catch (SdkException<RawError> ex) when (IsNotFound(ex.Error))
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapRaw("read customer by reference", ex);
        }
    }

    public async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int providerCustomerId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(providerCustomerId, ct);
            return response
                .Where(s => s.Subscription is not null)
                .Select(s => MapSubscription(s.Subscription!, providerCustomerId))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapRaw("list customer subscriptions", ex);
        }
    }

    public async Task<Subscription> CreateSubscriptionAsync(int providerCustomerId, string productHandle, CancellationToken ct = default)
    {
        try
        {
            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = providerCustomerId,
                    // The product's own RequireCreditCard=false only waives card-capture UI validation;
                    // CreateSubscription still defaults to automatic (auto-charge) collection, which is
                    // rejected with no payment profile on file. Invoice collection bills outside the
                    // gateway, matching "no card capture" (confirmed empirically against the sandbox).
                    PaymentCollectionMethod = CollectionMethod.Invoice
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(request, ct);
            var subscription = response.Subscription ?? throw new BillingProviderException("Maxio create subscription succeeded but returned no subscription body.");
            return MapSubscription(subscription, providerCustomerId);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? DescribeErrorList(errors) : DescribeFallback(ex.Error);
            throw new BillingProviderException($"Maxio create subscription failed: {detail}", ex);
        }
    }

    public async Task<Subscription> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct);
            if (response.Subscription is null)
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }

            return MapSubscription(response.Subscription, knownCustomerId: null);
        }
        catch (SdkException<RawError> ex) when (IsNotFound(ex.Error))
        {
            throw new SubscriptionNotFoundException(subscriptionId);
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapRaw("read subscription", ex);
        }
    }

    public async Task<UsageRecord> RecordUsageAsync(int subscriptionId, double quantity, string? memo, CancellationToken ct = default)
    {
        UsageResponse response;
        try
        {
            var request = new CreateUsageRequest
            {
                Usage = new CreateUsage
                {
                    Quantity = quantity,
                    Memo = memo
                }
            };

            response = await _client.SubscriptionComponents.CreateUsage(
                SubscriptionIdOrReference.Int(subscriptionId),
                ComponentIdModel.Int(_settings.MeteredComponentId),
                request,
                ct);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? DescribeErrorList(errors) : DescribeFallback(ex.Error);
            throw new BillingProviderException($"Maxio record usage failed: {detail}", ex);
        }

        var recordedAt = response.Usage?.CreatedAt ?? DateTimeOffset.UtcNow;

        int? periodToDateTotal = null;
        try
        {
            var componentResponse = await _client.SubscriptionComponents.ReadSubscriptionComponent(subscriptionId, _settings.MeteredComponentId, ct);
            periodToDateTotal = componentResponse.Component?.UnitBalance;
        }
        catch (Exception ex)
        {
            // Read-back failure must not fail the whole operation (UC2): the usage stands, total unavailable.
            _logger.LogWarning("Failed to read back period-to-date usage for subscription {0}: {1}", subscriptionId, ex.Message);
        }

        return new UsageRecord(quantity, memo, recordedAt, periodToDateTotal);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default)
    {
        try
        {
            var request = new SubscriptionMigrationPreviewRequest
            {
                Migration = new SubscriptionMigrationPreviewOptions
                {
                    ProductHandle = targetProductHandle,
                    PreservePeriod = true
                }
            };

            var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(subscriptionId, request, ct);
            var migration = response.Migration ?? throw new BillingProviderException("Maxio preview plan change succeeded but returned no migration body.");

            return new PlanChangePreview(
                subscriptionId,
                currentProductHandle: string.Empty,
                targetProductHandle,
                proratedAdjustmentInCents: (int)(migration.ProratedAdjustmentInCents ?? 0),
                chargeInCents: (int)(migration.ChargeInCents ?? 0),
                paymentDueInCents: (int)(migration.PaymentDueInCents ?? 0),
                creditAppliedInCents: (int)(migration.CreditAppliedInCents ?? 0));
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? DescribeErrorList(errors) : DescribeFallback(ex.Error);
            throw new BillingProviderException($"Maxio preview plan change failed: {detail}", ex);
        }
    }

    public async Task<Subscription> CommitPlanChangeNowAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default)
    {
        try
        {
            var request = new SubscriptionProductMigrationRequest
            {
                Migration = new SubscriptionProductMigration
                {
                    ProductHandle = targetProductHandle,
                    PreservePeriod = true
                }
            };

            var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(subscriptionId, request, ct);
            var subscription = response.Subscription ?? throw new BillingProviderException("Maxio migrate subscription product succeeded but returned no subscription body.");
            return MapSubscription(subscription, knownCustomerId: null);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? DescribeErrorList(errors) : DescribeFallback(ex.Error);
            throw new BillingProviderException($"Maxio commit plan change failed: {detail}", ex);
        }
    }

    public async Task<Subscription> SchedulePlanChangeAtRenewalAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default)
    {
        try
        {
            var request = new UpdateSubscriptionRequest
            {
                Subscription = new UpdateSubscription
                {
                    ProductHandle = targetProductHandle,
                    ProductChangeDelayed = true
                }
            };

            var response = await _client.Subscriptions.UpdateSubscription(subscriptionId, request, ct);
            var subscription = response.Subscription ?? throw new BillingProviderException("Maxio update subscription succeeded but returned no subscription body.");
            return MapSubscription(subscription, knownCustomerId: null);
        }
        catch (SdkException<UpdateSubscriptionError> ex)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? DescribeErrorList(errors) : DescribeFallback(ex.Error);
            throw new BillingProviderException($"Maxio schedule plan change failed: {detail}", ex);
        }
    }

    public async Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.PauseSubscription(subscriptionId, body: null, ct);
            var subscription = response.Subscription ?? throw new BillingProviderException("Maxio pause subscription succeeded but returned no subscription body.");
            return MapSubscription(subscription, knownCustomerId: null);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? DescribeErrorList(errors) : DescribeFallback(ex.Error);
            throw new BillingProviderException($"Maxio pause subscription failed: {detail}", ex);
        }
    }

    public async Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ResumeSubscription(subscriptionId, calendarBillingResumptionCharge: null, ct);
            var subscription = response.Subscription ?? throw new BillingProviderException("Maxio resume subscription succeeded but returned no subscription body.");
            return MapSubscription(subscription, knownCustomerId: null);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? DescribeErrorList(errors) : DescribeFallback(ex.Error);
            throw new BillingProviderException($"Maxio resume subscription failed: {detail}", ex);
        }
    }

    public async Task<Subscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken ct = default)
    {
        var request = new CancellationRequest
        {
            Subscription = new CancellationOptions
            {
                CancelAtEndOfPeriod = endOfPeriod,
                CancellationMessage = reason
            }
        };

        if (endOfPeriod)
        {
            try
            {
                await _client.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId, request, ct);
            }
            catch (SdkException<InitiateDelayedCancellationError> ex)
            {
                var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? DescribeErrorList(errors) : DescribeFallback(ex.Error);
                throw new BillingProviderException($"Maxio delayed cancel subscription failed: {detail}", ex);
            }

            return await GetSubscriptionAsync(subscriptionId, ct);
        }

        try
        {
            var response = await _client.SubscriptionStatus.CancelSubscription(subscriptionId, request, ct);
            var subscription = response.Subscription ?? throw new BillingProviderException("Maxio cancel subscription succeeded but returned no subscription body.");
            return MapSubscription(subscription, knownCustomerId: null);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            string detail;
            if (ex.Error.TryGetNoContent(out var notFound))
            {
                detail = DescribeRawError(notFound);
            }
            else if (ex.Error.TryGetCancelSubscriptionErrorResponse(out var union) && union.TryGetErrorListResponse1(out var errors))
            {
                detail = DescribeErrorList(errors);
            }
            else
            {
                detail = DescribeFallback(ex.Error);
            }

            throw new BillingProviderException($"Maxio cancel subscription failed: {detail}", ex);
        }
    }

    public async Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var request = new ReactivateSubscriptionRequest
            {
                Resume = Resume.Bool(true)
            };

            var response = await _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, request, ct);
            var subscription = response.Subscription ?? throw new BillingProviderException("Maxio reactivate subscription succeeded but returned no subscription body.");
            return MapSubscription(subscription, knownCustomerId: null);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? DescribeErrorList(errors) : DescribeFallback(ex.Error);
            throw new BillingProviderException($"Maxio reactivate subscription failed: {detail}", ex);
        }
    }

    private async Task ReadProductFamilyInternalAsync(CancellationToken ct)
    {
        try
        {
            await _client.ProductFamilies.ReadProductFamily(_settings.ProductFamilyId, ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingConfigurationException(
                $"Configured product family id {_settings.ProductFamilyId} ('{_settings.ProductFamilyHandle}') does not resolve: {DescribeRawError(ex.Error)}. Re-run UC0 seeding.", ex);
        }
    }

    private async Task<Product> ReadProductInternalAsync(int productId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Products.ReadProduct(productId, ct);
            return response.Product ?? throw new BillingConfigurationException($"Configured product id {productId} did not return a product body.");
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingConfigurationException(
                $"Configured product id {productId} does not resolve: {DescribeRawError(ex.Error)}. Re-run UC0 seeding.", ex);
        }
    }

    private async Task<Component> ReadComponentInternalAsync(CancellationToken ct)
    {
        try
        {
            var response = await _client.Components.ReadComponent(_settings.ProductFamilyId, _settings.MeteredComponentId.ToString(), ct);
            return response.Component ?? throw new BillingConfigurationException($"Configured metered component id {_settings.MeteredComponentId} did not return a component body.");
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingConfigurationException(
                $"Configured metered component id {_settings.MeteredComponentId} ('{_settings.MeteredComponentHandle}') does not resolve: {DescribeRawError(ex.Error)}. Re-run UC0 seeding.", ex);
        }
    }

    private static MaxioAdvancedBillingClientOptions BuildOptions(MaxioSettings settings)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            }
        };

        var isEu = string.Equals(settings.Environment, "EU", StringComparison.OrdinalIgnoreCase);
        options.Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us;

        var hasExplicitBaseUrl = !string.IsNullOrWhiteSpace(settings.BaseUrl);
        if (isEu)
        {
            if (hasExplicitBaseUrl)
            {
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                options.Server.Production.Eu.Site = settings.Subdomain;
            }
        }
        else
        {
            if (hasExplicitBaseUrl)
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain;
            }
        }

        return options;
    }

    private static BillingPlan MapPlan(Product product) => new(
        productId: product.Id ?? 0,
        handle: product.Handle ?? string.Empty,
        name: product.Name ?? string.Empty,
        priceInCents: (int)(product.PriceInCents ?? 0),
        intervalUnit: product.IntervalUnit ?? string.Empty,
        requiresPaymentMethod: product.RequireCreditCard ?? false);

    private static Subscription MapSubscription(MaxioSubscription raw, int? knownCustomerId)
    {
        var providerCustomerId = knownCustomerId ?? raw.Customer?.Id;
        var product = raw.Product;

        return new Subscription(
            id: raw.Id ?? 0,
            providerCustomerId: providerCustomerId,
            productHandle: product?.Handle ?? string.Empty,
            productName: product?.Name ?? string.Empty,
            priceInCents: (int)(product?.PriceInCents ?? 0),
            state: MapState(raw.State),
            currentPeriodEndsAt: raw.CurrentPeriodEndsAt);
    }

    private static SubscriptionState MapState(MaxioSubscriptionState? state)
    {
        if (state is null)
        {
            throw new BillingProviderException("Subscription state was not returned by the billing provider.");
        }

        var value = state.Value;
        if (value == MaxioSubscriptionState.Pending) return SubscriptionState.Pending;
        if (value == MaxioSubscriptionState.FailedToCreate) return SubscriptionState.FailedToCreate;
        if (value == MaxioSubscriptionState.Trialing) return SubscriptionState.Trialing;
        if (value == MaxioSubscriptionState.Assessing) return SubscriptionState.Assessing;
        if (value == MaxioSubscriptionState.Active) return SubscriptionState.Active;
        if (value == MaxioSubscriptionState.SoftFailure) return SubscriptionState.SoftFailure;
        if (value == MaxioSubscriptionState.PastDue) return SubscriptionState.PastDue;
        if (value == MaxioSubscriptionState.Suspended) return SubscriptionState.Suspended;
        if (value == MaxioSubscriptionState.Canceled) return SubscriptionState.Canceled;
        if (value == MaxioSubscriptionState.Expired) return SubscriptionState.Expired;
        if (value == MaxioSubscriptionState.Paused) return SubscriptionState.Paused;
        if (value == MaxioSubscriptionState.Unpaid) return SubscriptionState.Unpaid;
        if (value == MaxioSubscriptionState.TrialEnded) return SubscriptionState.TrialEnded;
        if (value == MaxioSubscriptionState.OnHold) return SubscriptionState.OnHold;
        if (value == MaxioSubscriptionState.AwaitingSignup) return SubscriptionState.AwaitingSignup;

        throw new BillingProviderException($"Unrecognized subscription state '{value}' returned by the billing provider.");
    }

    private static bool IsNotFound(RawError error) => (int)error.StatusCode == 404;

    private static BillingProviderException WrapRaw(string action, SdkException<RawError> ex) =>
        new($"Maxio {action} failed: {DescribeRawError(ex.Error)}", ex);

    private static string DescribeRawError(RawError raw) => $"HTTP {(int)raw.StatusCode}: {SafeReadAsString(raw)}";

    private static string SafeReadAsString(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch (Exception ex)
        {
            return $"(unreadable error body: {ex.Message})";
        }
    }

    private static string DescribeFallback(ApiError error) =>
        error.TryGetRawError(out var raw) ? DescribeRawError(raw) : "(no error detail returned)";

    private static string DescribeErrorList(ErrorListResponse1 errors) =>
        errors.Errors is { Count: > 0 } list ? string.Join("; ", list) : "(no error detail returned)";
}
