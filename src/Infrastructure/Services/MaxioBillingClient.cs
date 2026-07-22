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
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using MaxioComponentKind = MaxioAdvancedBilling.Models.Enums.ComponentKind;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;
using MaxioSubscriptionState = MaxioAdvancedBilling.Models.Enums.SubscriptionState;
using Subscription = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription;
using SubscriptionState = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.SubscriptionState;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The one and only class in eShopOnWeb that talks to Maxio Advanced Billing. It normalizes every
/// provider model onto eShopOnWeb's own domain types, converts the provider's cents to whole
/// currency units, and surfaces every failure as <see cref="BillingProviderException"/> or
/// <see cref="BillingConfigurationException"/>.
/// </summary>
/// <remarks>
/// The outbound target server is resolved here, from <see cref="MaxioSettings.ResolveBaseUrl"/>, so
/// pointing the same build at production, a dev tenant, or a local mock is a configuration change
/// only. The <see cref="HttpClient"/> is supplied by <c>IHttpClientFactory</c> and is also the seam
/// tests use to intercept outbound traffic.
/// </remarks>
public class MaxioBillingClient : IBillingClient, IBillingProvisioningClient
{
    private const string BasicAuthPassword = "x";
    private const int MaxPageSize = 200;

    private readonly Lazy<MaxioAdvancedBillingClient> _client;
    private readonly MaxioSettings _settings;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings.Value ?? new MaxioSettings();

        // Configuration is validated on first use rather than during construction, so an environment
        // where Maxio is not configured still starts and serves the rest of eShopOnWeb normally.
        _client = new Lazy<MaxioAdvancedBillingClient>(
            () => new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(_settings)));
    }

    /// <summary>
    /// Translates <see cref="MaxioSettings"/> into SDK client options. The SDK composes absolute
    /// request URLs from its own server options rather than from <see cref="HttpClient.BaseAddress"/>,
    /// so the resolved base URL is applied here — an explicit <c>Maxio:BaseUrl</c> is used verbatim,
    /// and only in its absence is the host derived from the subdomain and region.
    /// </summary>
    private static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new BillingConfigurationException(
                "'Maxio:ApiKey' is not configured. Supply it through user-secrets or the environment.");
        }

        if (!settings.HasExplicitBaseUrl && string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            throw new BillingConfigurationException(
                "Maxio has no target server: set either 'Maxio:BaseUrl' or 'Maxio:Subdomain'.");
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = settings.IsEuropeanRegion ? ServerEnvironment.Eu : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = BasicAuthPassword
            }
        };

        // Only the nested options for the selected environment are read, so configure that one.
        // An explicit base URL is used verbatim; otherwise the host is derived from the subdomain.
        var explicitBaseUrl = settings.HasExplicitBaseUrl ? settings.ResolveBaseUrl() : null;
        var site = settings.Subdomain.Trim();

        if (settings.IsEuropeanRegion)
        {
            if (explicitBaseUrl is not null)
            {
                options.Server.Production.Eu.BaseUrl = explicitBaseUrl;
            }
            else
            {
                options.Server.Production.Eu.Site = site;
            }
        }
        else
        {
            if (explicitBaseUrl is not null)
            {
                options.Server.Production.Us.BaseUrl = explicitBaseUrl;
            }
            else
            {
                options.Server.Production.Us.Site = site;
            }
        }

        return options;
    }

    /// <summary>
    /// The target server this client is pointed at, or an empty string when none is configured.
    /// Exposed for diagnostics.
    /// </summary>
    public string TargetBaseUrl => _settings.TryResolveBaseUrl(out var baseUrl) ? baseUrl : string.Empty;

    #region Plans

    public async Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(
        CancellationToken cancellationToken = default)
    {
        var products = await ListProductsAsync(ResolveConfiguredFamilyReference(), false, cancellationToken);
        return products.Select(MapPlan).ToList();
    }

    public async Task<BillingPlan?> FindPlanByHandleAsync(string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        var response = await ReadOrNullAsync(
            ct => _client.Value.Products.ReadProductByHandle(planHandle, ct), cancellationToken);

        return response?.Product is null ? null : MapPlan(response.Product);
    }

    private async Task<IReadOnlyCollection<Product>> ListProductsAsync(string familyReference,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var products = new List<Product>();
        var page = 1;

        while (true)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await _client.Value.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyReference,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: includeArchived,
                    include: null,
                    page: page,
                    perPage: MaxPageSize,
                    ct: cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFound))
                {
                    throw new BillingConfigurationException(
                        $"Maxio product family '{familyReference}' did not resolve ({notFound}). " +
                        "Re-seed the sandbox or correct 'Maxio:ProductFamilyHandle'.", ex);
                }

                throw Translate(null, ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
            }
            catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
            {
                throw ToBoundaryException(ex);
            }

            products.AddRange(batch.Select(p => p.Product));

            if (batch.Count < MaxPageSize)
            {
                return products;
            }

            page++;
        }
    }

    #endregion

    #region Components

    public async Task<BillingComponent?> FindComponentByHandleAsync(string componentHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(componentHandle))
        {
            return null;
        }

        var response = await ReadOrNullAsync(
            ct => _client.Value.Components.FindComponent(componentHandle, ct), cancellationToken);

        return response is null ? null : MapComponent(response.Component);
    }

    #endregion

    #region Customers

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string customerReference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return null;
        }

        var response = await ReadOrNullAsync(
            ct => _client.Value.Customers.ReadCustomerByReference(customerReference, ct), cancellationToken);

        // Guard against a 2xx that carries no usable customer rather than the expected 404.
        if (response?.Customer is null || response.Customer.Id is null or 0)
        {
            return null;
        }

        return MapCustomer(response.Customer);
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(string customerReference,
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(customerReference));
        }

        var existing = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = customerReference
            }
        };

        CustomerResponse response;
        try
        {
            response = await _client.Value.Customers.CreateCustomer(body, cancellationToken);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A concurrent subscribe may have created the customer between the lookup and here.
            var raced = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw TranslateCustomerError(ex);
        }
        catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
        {
            throw ToBoundaryException(ex);
        }

        return MapCustomer(response.Customer);
    }

    #endregion

    #region Subscriptions

    public async Task<IReadOnlyCollection<Subscription>> ListSubscriptionsAsync(BillingCustomer customer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        var responses = await ReadAsync(
            ct => _client.Value.Customers.ListCustomerSubscriptions(customer.Id, ct), cancellationToken);

        return responses
            .Select(r => r.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!, customer.Reference))
            .ToList();
    }

    public async Task<Subscription> GetSubscriptionAsync(int providerSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        var response = await ReadAsync(
            ct => _client.Value.Subscriptions.ReadSubscription(providerSubscriptionId, include: null, ct: ct),
            cancellationToken);

        return MapSubscription(RequireSubscription(response, providerSubscriptionId), null);
    }

    public async Task<Subscription> CreateSubscriptionAsync(BillingCustomer customer,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = ResolveCollectionMethod(_settings.PaymentCollectionMethod)
            }
        };

        SubscriptionResponse response;
        try
        {
            response = await _client.Value.Subscriptions.CreateSubscription(body, cancellationToken);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw Translate(
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null,
                ex);
        }
        catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
        {
            throw ToBoundaryException(ex);
        }

        return MapSubscription(RequireSubscription(response, 0), customer.Reference);
    }

    #endregion

    #region Usage

    public async Task<UsageRecord> RecordUsageAsync(int providerSubscriptionId,
        BillingComponent component,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity),
                "Reported usage must be a positive quantity.");
        }

        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"Component '{component.Handle}' is {component.Kind}, not metered, so it cannot accept usage. " +
                "Archive it and recreate it as a metered component.");
        }

        var body = new CreateUsageRequest
        {
            Usage = new CreateUsage
            {
                Quantity = (double)quantity,
                Memo = memo
            }
        };

        UsageResponse response;
        try
        {
            response = await _client.Value.SubscriptionComponents.CreateUsage(
                SubscriptionIdOrReference.Int(providerSubscriptionId),
                ComponentIdModel.Int(component.Id),
                body,
                cancellationToken);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            throw Translate(
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null,
                ex);
        }
        catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
        {
            throw ToBoundaryException(ex);
        }

        var usage = response.Usage;
        return new UsageRecord(
            usage.Id ?? 0,
            usage.SubscriptionId ?? providerSubscriptionId,
            usage.ComponentId ?? component.Id,
            usage.ComponentHandle ?? component.Handle,
            ReadQuantity(usage.Quantity) ?? quantity,
            usage.Memo,
            usage.CreatedAt);
    }

    public async Task<int?> GetPeriodToDateUnitsAsync(int providerSubscriptionId,
        BillingComponent component,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);

        SubscriptionComponentResponse response;
        try
        {
            response = await _client.Value.SubscriptionComponents.ReadSubscriptionComponent(
                providerSubscriptionId, component.Id, cancellationToken);
        }
        catch (SdkException<ReadSubscriptionComponentError> ex)
        {
            if (ex.Error.TryGetNoContent(out var missing) && missing.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            throw Translate(null, ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
        }
        catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
        {
            throw ToBoundaryException(ex);
        }

        return response.Component?.UnitBalance;
    }

    #endregion

    #region Plan change

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(Subscription subscription,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (string.IsNullOrWhiteSpace(targetPlanHandle))
        {
            throw new ArgumentException("A target plan handle is required.", nameof(targetPlanHandle));
        }

        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            // Deferred changes are never prorated: the current period is untouched and the customer
            // simply pays the target plan's price from the next period onwards.
            var targetPlan = await FindPlanByHandleAsync(targetPlanHandle, cancellationToken)
                ?? throw new BillingConfigurationException(
                    $"Maxio plan '{targetPlanHandle}' did not resolve. Re-seed the sandbox or correct the configuration.");

            return new PlanChangePreview(subscription.Plan.Handle, targetPlanHandle, timing,
                proratedAdjustment: 0m, charge: targetPlan.Price, paymentDue: 0m, creditApplied: 0m);
        }

        var body = new SubscriptionMigrationPreviewRequest
        {
            Migration = new SubscriptionMigrationPreviewOptions
            {
                ProductHandle = targetPlanHandle,
                IncludeTrial = false,
                IncludeInitialCharge = false,
                IncludeCoupons = true,
                PreservePeriod = false
            }
        };

        SubscriptionMigrationPreviewResponse response;
        try
        {
            response = await _client.Value.SubscriptionProducts.PreviewSubscriptionProductMigration(
                subscription.ProviderSubscriptionId, body, cancellationToken);
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            throw Translate(
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null,
                ex);
        }
        catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
        {
            throw ToBoundaryException(ex);
        }

        var migration = response.Migration;
        return new PlanChangePreview(
            subscription.Plan.Handle,
            targetPlanHandle,
            timing,
            FromCents(migration.ProratedAdjustmentInCents),
            FromCents(migration.ChargeInCents),
            FromCents(migration.PaymentDueInCents),
            FromCents(migration.CreditAppliedInCents));
    }

    public async Task<Subscription> ChangePlanAsync(Subscription subscription,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (string.IsNullOrWhiteSpace(targetPlanHandle))
        {
            throw new ArgumentException("A target plan handle is required.", nameof(targetPlanHandle));
        }

        SubscriptionResponse response;

        if (timing == PlanChangeTiming.AtNextRenewal)
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
                response = await _client.Value.Subscriptions.UpdateSubscription(
                    subscription.ProviderSubscriptionId, body, cancellationToken);
            }
            catch (SdkException<UpdateSubscriptionError> ex)
            {
                throw Translate(
                    ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null,
                    ex);
            }
            catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
            {
                throw ToBoundaryException(ex);
            }
        }
        else
        {
            // The same option values the preview was computed with, so preview and commit agree.
            var body = new SubscriptionProductMigrationRequest
            {
                Migration = new SubscriptionProductMigration
                {
                    ProductHandle = targetPlanHandle,
                    IncludeTrial = false,
                    IncludeInitialCharge = false,
                    IncludeCoupons = true,
                    PreservePeriod = false
                }
            };

            try
            {
                response = await _client.Value.SubscriptionProducts.MigrateSubscriptionProduct(
                    subscription.ProviderSubscriptionId, body, cancellationToken);
            }
            catch (SdkException<MigrateSubscriptionProductError> ex)
            {
                throw Translate(
                    ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null,
                    ex);
            }
            catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
            {
                throw ToBoundaryException(ex);
            }
        }

        return MapSubscription(
            RequireSubscription(response, subscription.ProviderSubscriptionId),
            subscription.CustomerReference);
    }

    #endregion

    #region Lifecycle

    public async Task<Subscription> PauseSubscriptionAsync(int providerSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        SubscriptionResponse response;
        try
        {
            // No body: hold indefinitely until the customer resumes.
            response = await _client.Value.SubscriptionStatus.PauseSubscription(
                providerSubscriptionId, null, cancellationToken);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            throw Translate(
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null,
                ex);
        }
        catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
        {
            throw ToBoundaryException(ex);
        }

        return MapSubscription(RequireSubscription(response, providerSubscriptionId), null);
    }

    public async Task<Subscription> ResumeSubscriptionAsync(int providerSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        SubscriptionResponse response;
        try
        {
            response = await _client.Value.SubscriptionStatus.ResumeSubscription(
                providerSubscriptionId, null, cancellationToken);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            throw Translate(
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null,
                ex);
        }
        catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
        {
            throw ToBoundaryException(ex);
        }

        return MapSubscription(RequireSubscription(response, providerSubscriptionId), null);
    }

    public async Task<Subscription> CancelSubscriptionAsync(int providerSubscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var body = new CancellationRequest
        {
            Subscription = new CancellationOptions
            {
                CancellationMessage = reason
            }
        };

        if (timing == CancellationTiming.EndOfPeriod)
        {
            try
            {
                // The delayed-cancel response carries only a message, so the resulting state has to
                // be read back from the subscription itself.
                await _client.Value.SubscriptionStatus.InitiateDelayedCancellation(
                    providerSubscriptionId, body, cancellationToken);
            }
            catch (SdkException<InitiateDelayedCancellationError> ex)
            {
                if (ex.Error.TryGetNoContent(out var missing))
                {
                    throw new BillingProviderException((int)missing.StatusCode,
                        $"Subscription {providerSubscriptionId} was not found.", ex);
                }

                throw Translate(
                    ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null,
                    ex);
            }
            catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
            {
                throw ToBoundaryException(ex);
            }

            return await GetSubscriptionAsync(providerSubscriptionId, cancellationToken);
        }

        SubscriptionResponse response;
        try
        {
            response = await _client.Value.SubscriptionStatus.CancelSubscription(
                providerSubscriptionId, body, cancellationToken);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            throw TranslateCancelError(ex);
        }
        catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
        {
            throw ToBoundaryException(ex);
        }

        return MapSubscription(RequireSubscription(response, providerSubscriptionId), null);
    }

    public async Task<Subscription> ReactivateSubscriptionAsync(int providerSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        SubscriptionResponse response;
        try
        {
            response = await _client.Value.SubscriptionStatus.ReactivateSubscription(
                providerSubscriptionId, new ReactivateSubscriptionRequest(), cancellationToken);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            throw Translate(
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null,
                ex);
        }
        catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
        {
            throw ToBoundaryException(ex);
        }

        return MapSubscription(RequireSubscription(response, providerSubscriptionId), null);
    }

    #endregion

    #region Provisioning (UC0)

    public async Task<BillingProductFamily?> FindProductFamilyByHandleAsync(string handle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return null;
        }

        // Maxio exposes no read-family-by-handle operation, so list and match client-side.
        var families = await ReadAsync(ct => _client.Value.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: ct), cancellationToken);

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null &&
                string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase));

        return match is null ? null : MapProductFamily(match);
    }

    public async Task<BillingProductFamily> CreateProductFamilyAsync(string handle,
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var body = new CreateProductFamilyRequest
        {
            ProductFamily = new CreateProductFamily
            {
                Name = name,
                Handle = handle,
                Description = description
            }
        };

        ProductFamilyResponse response;
        try
        {
            response = await _client.Value.ProductFamilies.CreateProductFamily(body, cancellationToken);
        }
        catch (SdkException<CreateProductFamilyError> ex)
        {
            throw Translate(
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null,
                ex);
        }
        catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
        {
            throw ToBoundaryException(ex);
        }

        if (response.ProductFamily is null)
        {
            throw new BillingProviderException(0,
                "Maxio accepted the product family but returned no product family.");
        }

        return MapProductFamily(response.ProductFamily);
    }

    public async Task<IReadOnlyCollection<BillingPlan>> ListPlansForFamilyAsync(BillingProductFamily family,
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(family);

        var products = await ListProductsAsync(FamilyReference(family), includeArchived, cancellationToken);
        return products.Select(MapPlan).ToList();
    }

    public async Task<BillingPlan> CreatePlanAsync(BillingProductFamily family,
        string handle,
        string name,
        string description,
        decimal price,
        int interval,
        string intervalUnit,
        bool requiresPaymentMethod,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(family);

        var body = new CreateOrUpdateProductRequest
        {
            Product = new CreateOrUpdateProduct
            {
                Name = name,
                Handle = handle,
                Description = description,
                PriceInCents = ToCents(price),
                Interval = interval,
                IntervalUnit = ParseIntervalUnit(intervalUnit),
                RequireCreditCard = requiresPaymentMethod
            }
        };

        ProductResponse response;
        try
        {
            response = await _client.Value.Products.CreateProduct(
                FamilyReference(family), body, cancellationToken);
        }
        catch (SdkException<CreateProductError> ex)
        {
            throw Translate(
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null,
                ex);
        }
        catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
        {
            throw ToBoundaryException(ex);
        }

        return MapPlan(response.Product);
    }

    public async Task<BillingPlan> ArchivePlanAsync(int planId, CancellationToken cancellationToken = default)
    {
        ProductResponse response;
        try
        {
            response = await _client.Value.Products.ArchiveProduct(planId, cancellationToken);
        }
        catch (SdkException<ArchiveProductError> ex)
        {
            throw Translate(
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null,
                ex);
        }
        catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
        {
            throw ToBoundaryException(ex);
        }

        return MapPlan(response.Product);
    }

    public async Task<IReadOnlyCollection<BillingComponent>> ListComponentsForFamilyAsync(
        BillingProductFamily family,
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(family);

        var components = new List<BillingComponent>();
        var page = 1;

        while (true)
        {
            var batch = await ReadAsync(ct => _client.Value.Components.ListComponentsForProductFamily(
                productFamilyId: family.Id,
                includeArchived: includeArchived,
                filter: null,
                dateField: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                page: page,
                perPage: MaxPageSize,
                ct: ct), cancellationToken);

            components.AddRange(batch.Select(c => MapComponent(c.Component)));

            if (batch.Count < MaxPageSize)
            {
                return components;
            }

            page++;
        }
    }

    public async Task<BillingComponent> CreateMeteredComponentAsync(BillingProductFamily family,
        string handle,
        string name,
        string unitName,
        decimal unitPrice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(family);

        var body = new CreateMeteredComponent
        {
            MeteredComponent = new MeteredComponent
            {
                Name = name,
                Handle = handle,
                UnitName = unitName,
                PricingScheme = PricingScheme.PerUnit,
                // unit_price is decimal currency units, not cents; the string form avoids float drift.
                UnitPrice = UnitPrice1.String(unitPrice.ToString(CultureInfo.InvariantCulture)),
                Taxable = false
            }
        };

        ComponentResponse response;
        try
        {
            response = await _client.Value.Components.CreateMeteredComponent(
                FamilyReference(family), body, cancellationToken);
        }
        catch (SdkException<CreateMeteredComponentError> ex)
        {
            if (ex.Error.TryGetNoContent(out var missing))
            {
                throw new BillingConfigurationException(
                    $"Maxio product family '{family.Handle}' was not found ({(int)missing.StatusCode}).", ex);
            }

            throw Translate(
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null,
                ex);
        }
        catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
        {
            throw ToBoundaryException(ex);
        }

        return MapComponent(response.Component);
    }

    public async Task<BillingComponent> ArchiveComponentAsync(BillingProductFamily family,
        int componentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(family);

        Component component;
        try
        {
            // Unlike every other component operation this one returns the component directly.
            component = await _client.Value.Components.ArchiveComponent(
                family.Id, componentId.ToString(CultureInfo.InvariantCulture), cancellationToken);
        }
        catch (SdkException<ArchiveComponentError> ex)
        {
            throw Translate(
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null,
                ex);
        }
        catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
        {
            throw ToBoundaryException(ex);
        }

        return MapComponent(component);
    }

    #endregion

    #region Mapping

    private static BillingProductFamily MapProductFamily(ProductFamily family) =>
        new(family.Id ?? 0, family.Handle ?? string.Empty, family.Name ?? string.Empty);

    private static BillingPlan MapPlan(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Handle))
        {
            throw new BillingProviderException(0,
                $"Maxio returned product {product.Id} without a handle, which this integration addresses plans by.");
        }

        return new BillingPlan(
            product.Id ?? 0,
            product.Handle,
            product.Name ?? product.Handle,
            product.Description,
            FromCents(product.PriceInCents),
            product.Interval ?? 1,
            product.IntervalUnit?.Value ?? IntervalUnit.Month.Value,
            product.RequireCreditCard ?? false);
    }

    private static BillingComponent MapComponent(Component component) =>
        new(component.Id ?? 0,
            component.Handle ?? string.Empty,
            component.Name ?? string.Empty,
            MapComponentKind(component.Kind),
            ResolveUnitPrice(component),
            component.ProductFamilyHandle);

    private static BillingCustomer MapCustomer(Customer customer) =>
        new(customer.Id ?? 0,
            customer.Reference ?? string.Empty,
            customer.Email ?? string.Empty,
            customer.FirstName ?? string.Empty,
            customer.LastName ?? string.Empty);

    private static Subscription MapSubscription(MaxioSubscription subscription, string? knownCustomerReference)
    {
        if (subscription.Product is null || string.IsNullOrWhiteSpace(subscription.Product.Handle))
        {
            throw new BillingProviderException(0,
                $"Maxio returned subscription {subscription.Id} without a resolvable product.");
        }

        var reference = subscription.Customer?.Reference
            ?? knownCustomerReference
            ?? string.Empty;

        return new Subscription(
            subscription.Id ?? 0,
            subscription.Customer?.Id ?? 0,
            reference,
            MapPlan(subscription.Product),
            MapSubscriptionState(subscription.State),
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt,
            subscription.CancelAtEndOfPeriod ?? false,
            subscription.NextProductHandle);
    }

    /// <summary>
    /// Maxio models a held subscription as <c>on_hold</c>, but also declares a <c>paused</c> state.
    /// Both map onto the domain's single paused state.
    /// </summary>
    private static SubscriptionState MapSubscriptionState(MaxioSubscriptionState? state)
    {
        if (state is null)
        {
            return SubscriptionState.Unknown;
        }

        if (state == MaxioSubscriptionState.Active) return SubscriptionState.Active;
        if (state == MaxioSubscriptionState.Trialing) return SubscriptionState.Trialing;
        if (state == MaxioSubscriptionState.Pending) return SubscriptionState.Pending;
        if (state == MaxioSubscriptionState.Assessing) return SubscriptionState.Pending;
        if (state == MaxioSubscriptionState.AwaitingSignup) return SubscriptionState.Pending;
        if (state == MaxioSubscriptionState.PastDue) return SubscriptionState.PastDue;
        if (state == MaxioSubscriptionState.SoftFailure) return SubscriptionState.PastDue;
        if (state == MaxioSubscriptionState.Suspended) return SubscriptionState.Suspended;
        if (state == MaxioSubscriptionState.OnHold) return SubscriptionState.Paused;
        if (state == MaxioSubscriptionState.Paused) return SubscriptionState.Paused;
        if (state == MaxioSubscriptionState.Canceled) return SubscriptionState.Canceled;
        if (state == MaxioSubscriptionState.Expired) return SubscriptionState.Expired;
        if (state == MaxioSubscriptionState.Unpaid) return SubscriptionState.Unpaid;
        if (state == MaxioSubscriptionState.TrialEnded) return SubscriptionState.TrialEnded;
        if (state == MaxioSubscriptionState.FailedToCreate) return SubscriptionState.Failed;

        return SubscriptionState.Unknown;
    }

    private static BillingComponentKind MapComponentKind(MaxioComponentKind? kind)
    {
        if (kind is null)
        {
            return BillingComponentKind.Unknown;
        }

        if (kind == MaxioComponentKind.MeteredComponent) return BillingComponentKind.Metered;
        if (kind == MaxioComponentKind.QuantityBasedComponent) return BillingComponentKind.QuantityBased;
        if (kind == MaxioComponentKind.OnOffComponent) return BillingComponentKind.OnOff;
        if (kind == MaxioComponentKind.PrepaidUsageComponent) return BillingComponentKind.PrepaidUsage;
        if (kind == MaxioComponentKind.EventBasedComponent) return BillingComponentKind.EventBased;

        return BillingComponentKind.Unknown;
    }

    /// <summary>
    /// Prefers the provider's cents mirror of the unit price and falls back to parsing the decimal
    /// currency string, so the value handed to the domain is always whole currency units.
    /// </summary>
    private static decimal? ResolveUnitPrice(Component component)
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

        return null;
    }

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

    private static decimal FromCents(long? cents) => (cents ?? 0L) / 100m;

    private static long ToCents(decimal amount) => (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    private static IntervalUnit ParseIntervalUnit(string intervalUnit) =>
        intervalUnit?.Trim().ToLowerInvariant() switch
        {
            "month" => IntervalUnit.Month,
            "day" => IntervalUnit.Day,
            _ => throw new BillingConfigurationException(
                $"Unsupported billing interval unit '{intervalUnit}'. Maxio supports 'month' and 'day'.")
        };

    private static CollectionMethod? ResolveCollectionMethod(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "automatic" => CollectionMethod.Automatic,
            "remittance" => CollectionMethod.Remittance,
            "prepaid" => CollectionMethod.Prepaid,
            "invoice" => CollectionMethod.Invoice,
            _ => throw new BillingConfigurationException(
                $"'Maxio:PaymentCollectionMethod' has unsupported value '{value}'. " +
                "Expected automatic, remittance, prepaid, or invoice.")
        };

    /// <summary>
    /// Addresses the configured product family by its stable handle where one is configured, because
    /// Maxio reassigns numeric ids whenever the catalogue is re-created.
    /// </summary>
    private string ResolveConfiguredFamilyReference()
    {
        if (!string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            return $"handle:{_settings.ProductFamilyHandle.Trim()}";
        }

        if (_settings.ProductFamilyId > 0)
        {
            return _settings.ProductFamilyId.ToString(CultureInfo.InvariantCulture);
        }

        throw new BillingConfigurationException(
            "Neither 'Maxio:ProductFamilyHandle' nor 'Maxio:ProductFamilyId' is configured.");
    }

    private static string FamilyReference(BillingProductFamily family) =>
        family.Id > 0
            ? family.Id.ToString(CultureInfo.InvariantCulture)
            : $"handle:{family.Handle}";

    private static MaxioSubscription RequireSubscription(SubscriptionResponse response, int subscriptionId)
    {
        if (response.Subscription is null)
        {
            throw new BillingProviderException(0,
                subscriptionId > 0
                    ? $"Maxio returned no subscription for {subscriptionId}."
                    : "Maxio accepted the request but returned no subscription.");
        }

        return response.Subscription;
    }

    #endregion

    #region Error boundary

    /// <summary>
    /// Runs a call whose only failure shape is a raw provider error, translating it and any
    /// transport failure into <see cref="BillingProviderException"/>.
    /// </summary>
    private static async Task<T> ReadAsync<T>(Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        try
        {
            return await call(cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException((int)ex.Error.StatusCode, ReadRawMessage(ex.Error), ex);
        }
        catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
        {
            throw ToBoundaryException(ex);
        }
    }

    /// <summary>As <see cref="ReadAsync{T}"/>, but a 404 means "does not exist" rather than a failure.</summary>
    private static async Task<T?> ReadOrNullAsync<T>(Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await call(cancellationToken);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException((int)ex.Error.StatusCode, ReadRawMessage(ex.Error), ex);
        }
        catch (Exception ex) when (IsUnreadableProviderResponse(ex, cancellationToken))
        {
            throw ToBoundaryException(ex);
        }
    }

    /// <summary>
    /// Builds the domain exception from whichever error shape the provider supplied. The typed
    /// payload is preferred; the raw error is only ever the fallback.
    /// </summary>
    private static BillingProviderException Translate(ErrorListResponse1? errors, RawError? raw, Exception inner)
    {
        if (errors is not null)
        {
            return new BillingProviderException(422, string.Join("; ", errors.Errors), inner);
        }

        if (raw is not null)
        {
            return new BillingProviderException((int)raw.StatusCode, ReadRawMessage(raw), inner);
        }

        return new BillingProviderException(0, "Maxio returned an error with no readable payload.", inner);
    }

    /// <summary>
    /// Customer validation failures come back in a shape whose fields do not describe customer
    /// validation, so anything readable is taken best-effort before falling back to the raw body.
    /// </summary>
    private static BillingProviderException TranslateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var typed))
        {
            var messages = new List<string>();
            if (typed.Errors?.PerPage is { Count: > 0 } perPage)
            {
                messages.AddRange(perPage);
            }

            if (typed.Errors?.PricePoint is { Count: > 0 } pricePoint)
            {
                messages.AddRange(pricePoint);
            }

            if (messages.Count > 0)
            {
                return new BillingProviderException(422, string.Join("; ", messages), ex);
            }
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return new BillingProviderException((int)raw.StatusCode, ReadRawMessage(raw), ex);
        }

        return new BillingProviderException(422, "Maxio rejected the customer details.", ex);
    }

    private static BillingProviderException TranslateCancelError(SdkException<CancelSubscriptionApiError> ex)
    {
        if (ex.Error.TryGetNoContent(out var missing))
        {
            return new BillingProviderException((int)missing.StatusCode, "The subscription was not found.", ex);
        }

        if (ex.Error.TryGetCancelSubscriptionErrorResponse(out var typed))
        {
            if (typed.TryGetErrorListResponse1(out var list))
            {
                return new BillingProviderException(422, string.Join("; ", list.Errors), ex);
            }

            if (typed.TryGetSingleErrorResponse1(out var single))
            {
                return new BillingProviderException(422, single.Error, ex);
            }
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return new BillingProviderException((int)raw.StatusCode, ReadRawMessage(raw), ex);
        }

        return new BillingProviderException(0, "Maxio refused the cancellation without a readable reason.", ex);
    }

    /// <summary>Reads a raw error body as text; JSON parsing is deliberately avoided because non-JSON bodies throw.</summary>
    private static string ReadRawMessage(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            return string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)raw.StatusCode}" : body;
        }
        catch (Exception)
        {
            return $"HTTP {(int)raw.StatusCode} (response body could not be read)";
        }
    }

    /// <summary>
    /// True for failures the SDK surfaces as plain exceptions rather than <see cref="SdkException{T}"/>:
    /// a connection-level problem, or a body that does not match the shape the SDK expects — its
    /// deserializer throws in that case, including while reading an error payload. A caller-requested
    /// cancellation is never swallowed.
    /// </summary>
    private static bool IsUnreadableProviderResponse(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or TaskCanceledException or JsonException
        && !cancellationToken.IsCancellationRequested;

    private static BillingProviderException ToBoundaryException(Exception inner) => inner is JsonException
        ? new BillingProviderException(0,
            $"Maxio returned a response this integration could not read: {inner.Message}", inner)
        : new BillingProviderException(0, $"Maxio could not be reached: {inner.Message}", inner);

    #endregion
}
