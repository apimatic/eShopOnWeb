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
using MaxioAdvancedBilling.Core.Configuration;
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

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single integration point with Maxio Advanced Billing (plan §2.2). Everything provider
/// specific — the SDK, its wire models, its exception types and its base URL — stops here; callers
/// only ever see the provider-agnostic types of <see cref="IBillingClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// The outbound target is resolved from <see cref="MaxioSettings.ResolveBaseUrl"/>, so the same
/// build can be pointed at production, a sandbox tenant, or a local mock purely through
/// configuration (plan §2.3).
/// </para>
/// <para>
/// Every operation is bounded by <see cref="MaxioSettings.Timeout"/>. Retries apply to idempotent
/// reads only — a write is never replayed, so a transport failure can never double-bill.
/// </para>
/// </remarks>
public class MaxioBillingClient : IBillingClient
{
    /// <summary>Basic authentication carries the API key as the user name and a fixed placeholder password.</summary>
    private const string ApiKeyPasswordPlaceholder = "x";

    /// <summary>Prefix that tells the provider a component or product is addressed by handle.</summary>
    private const string HandlePrefix = "handle:";

    private const string RedactionMarker = "[redacted]";

    private const int CentsPerUnit = 100;

    private static readonly IReadOnlyList<string> NoMessages = Array.Empty<string>();

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingClient> _logger;
    private readonly Lazy<MaxioAdvancedBillingClient> _provider;

    private int? _productFamilyId;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _settings = settings.Value ?? new MaxioSettings();
        _logger = logger;
        _provider = new Lazy<MaxioAdvancedBillingClient>(CreateProviderClient);
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        var products = await InvokeAsync(nameof(ListPlansAsync), (client, ct) =>
            client.ProductFamilies.ListProductsForProductFamily(
                familyId.ToString(CultureInfo.InvariantCulture),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 200,
                ct: ct), cancellationToken);

        return products
            .Select(response => MapPlan(response.Product))
            .Where(plan => !plan.IsArchived)
            .ToList();
    }

    public async Task<BillingPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        var identifier = planHandle.Trim();

        try
        {
            // A plan is normally addressed by its stable handle, but a caller holding the provider's
            // numeric identifier is accepted too.
            var response = int.TryParse(identifier, NumberStyles.Integer, CultureInfo.InvariantCulture, out var planId)
                ? await InvokeAsync(nameof(FindPlanAsync), (client, ct) =>
                    client.Products.ReadProduct(planId, ct: ct), cancellationToken)
                : await InvokeAsync(nameof(FindPlanAsync), (client, ct) =>
                    client.Products.ReadProductByHandle(identifier, ct: ct), cancellationToken);

            return MapPlan(response.Product);
        }
        catch (BillingEntityNotFoundException)
        {
            return await FindPlanInCatalogAsync(identifier, cancellationToken);
        }
    }

    /// <summary>
    /// Falls back to the product family's catalog when the provider has no direct lookup for the
    /// identifier, so a plan is still resolved by handle or by identifier.
    /// </summary>
    private async Task<BillingPlan?> FindPlanInCatalogAsync(string identifier, CancellationToken cancellationToken)
    {
        try
        {
            var plans = await ListPlansAsync(cancellationToken);

            return plans.FirstOrDefault(plan =>
                string.Equals(plan.Handle, identifier, StringComparison.OrdinalIgnoreCase)
                || plan.Id.ToString(CultureInfo.InvariantCulture) == identifier);
        }
        catch (BillingProviderException)
        {
            return null;
        }
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
            var response = await InvokeAsync(nameof(FindCustomerByReferenceAsync), (client, ct) =>
                client.Customers.ReadCustomerByReference(reference, ct: ct), cancellationToken);

            return MapCustomer(response.Customer);
        }
        catch (BillingEntityNotFoundException)
        {
            return null;
        }
    }

    public async Task<BillingCustomer> CreateCustomerAsync(string reference, string email, string firstName,
        string lastName, CancellationToken cancellationToken = default)
    {
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var response = await InvokeAsync(nameof(CreateCustomerAsync), (client, ct) =>
            client.Customers.CreateCustomer(request, ct: ct), cancellationToken);

        return MapCustomer(response.Customer);
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                CustomerId = customerId,
                ProductHandle = AsPlanHandle(planHandle),
                ProductId = AsPlanId(planHandle),
                PaymentCollectionMethod = ResolvePaymentCollectionMethod()
            }
        };

        var response = await InvokeAsync(nameof(CreateSubscriptionAsync), (client, ct) =>
            client.Subscriptions.CreateSubscription(request, ct: ct), cancellationToken);

        return RequireSubscription(response, nameof(CreateSubscriptionAsync));
    }

    public async Task<BillingSubscription?> GetSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await InvokeAsync(nameof(GetSubscriptionAsync), (client, ct) =>
                client.Subscriptions.ReadSubscription(subscriptionId, null, ct: ct), cancellationToken);

            return response.Subscription is null ? null : MapSubscription(response.Subscription);
        }
        catch (BillingEntityNotFoundException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsForCustomerAsync(int customerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var responses = await InvokeAsync(nameof(ListSubscriptionsForCustomerAsync), (client, ct) =>
                client.Customers.ListCustomerSubscriptions(customerId, ct: ct), cancellationToken);

            return responses
                .Where(response => response.Subscription is not null)
                .Select(response => MapSubscription(response.Subscription!))
                .ToList();
        }
        catch (BillingEntityNotFoundException)
        {
            return Array.Empty<BillingSubscription>();
        }
    }

    public async Task<BillingComponent> GetUsageComponentAsync(CancellationToken cancellationToken = default)
    {
        var handle = _settings.MeteredComponentHandle?.Trim();
        if (string.IsNullOrEmpty(handle))
        {
            throw new BillingConfigurationException(
                $"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.MeteredComponentHandle)}' is not configured, so usage cannot be metered.");
        }

        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        var component = await ResolveComponentAsync(familyId, handle, cancellationToken)
            ?? throw new BillingConfigurationException(
                $"The configured metered component '{handle}' does not exist on product family {familyId}. Re-run the billing provider seed.");

        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"The configured component '{handle}' is of kind '{component.Kind}', not metered, so usage cannot be reported against it. Re-create it as a metered component.");
        }

        return component;
    }

    public async Task<UsageRecord> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo,
        CancellationToken cancellationToken = default)
    {
        var handle = _settings.MeteredComponentHandle?.Trim();
        if (string.IsNullOrEmpty(handle))
        {
            throw new BillingConfigurationException(
                $"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.MeteredComponentHandle)}' is not configured, so usage cannot be metered.");
        }

        var request = new CreateUsageRequest
        {
            Usage = new CreateUsage
            {
                Quantity = (double)quantity,
                Memo = memo
            }
        };

        var response = await InvokeAsync(nameof(RecordUsageAsync), (client, ct) =>
            client.SubscriptionComponents.CreateUsage(
                SubscriptionIdOrReference.Int(subscriptionId),
                ComponentIdModel.String(HandlePrefix + handle),
                request,
                ct: ct), cancellationToken);

        return MapUsage(response.Usage);
    }

    public async Task<decimal?> GetPeriodToDateUsageAsync(int subscriptionId, int componentId,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(nameof(GetPeriodToDateUsageAsync), (client, ct) =>
            client.SubscriptionComponents.ReadSubscriptionComponent(subscriptionId, componentId, ct: ct),
            cancellationToken);

        return response.Component?.UnitBalance;
    }

    public async Task<PlanMigrationQuote> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        var request = new SubscriptionMigrationPreviewRequest
        {
            Migration = new SubscriptionMigrationPreviewOptions
            {
                ProductHandle = AsPlanHandle(targetPlanHandle),
                ProductId = AsPlanId(targetPlanHandle)
            }
        };

        var response = await InvokeAsync(nameof(PreviewPlanChangeAsync), (client, ct) =>
            client.SubscriptionProducts.PreviewSubscriptionProductMigration(subscriptionId, request, ct: ct),
            cancellationToken);

        var migration = response.Migration;

        var charge = ToMajorUnits(migration.ChargeInCents);
        var creditApplied = ToMajorUnits(migration.CreditAppliedInCents);

        // The provider does not always quote a payment due; when it is absent it is what remains of
        // the charge once the credit is applied.
        var paymentDue = migration.PaymentDueInCents.HasValue
            ? ToMajorUnits(migration.PaymentDueInCents)
            : Math.Max(0m, charge - creditApplied);

        return new PlanMigrationQuote(
            ToMajorUnits(migration.ProratedAdjustmentInCents),
            charge,
            paymentDue,
            creditApplied);
    }

    public async Task<BillingSubscription> MigratePlanAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        var request = new SubscriptionProductMigrationRequest
        {
            Migration = new SubscriptionProductMigration
            {
                ProductHandle = AsPlanHandle(targetPlanHandle),
                ProductId = AsPlanId(targetPlanHandle)
            }
        };

        var response = await InvokeAsync(nameof(MigratePlanAsync), (client, ct) =>
            client.SubscriptionProducts.MigrateSubscriptionProduct(subscriptionId, request, ct: ct), cancellationToken);

        return RequireSubscription(response, nameof(MigratePlanAsync));
    }

    public async Task<BillingSubscription> SchedulePlanChangeAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateSubscriptionRequest
        {
            Subscription = new UpdateSubscription
            {
                ProductHandle = AsPlanHandle(targetPlanHandle),
                ProductId = AsPlanId(targetPlanHandle),
                ProductChangeDelayed = true
            }
        };

        var response = await InvokeAsync(nameof(SchedulePlanChangeAsync), (client, ct) =>
            client.Subscriptions.UpdateSubscription(subscriptionId, request, ct: ct), cancellationToken);

        return RequireSubscription(response, nameof(SchedulePlanChangeAsync));
    }

    public async Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(nameof(PauseSubscriptionAsync), (client, ct) =>
            client.SubscriptionStatus.PauseSubscription(subscriptionId, null, ct: ct), cancellationToken);

        return RequireSubscription(response, nameof(PauseSubscriptionAsync));
    }

    public async Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(nameof(ResumeSubscriptionAsync), (client, ct) =>
            client.SubscriptionStatus.ResumeSubscription(subscriptionId, null, ct: ct), cancellationToken);

        return RequireSubscription(response, nameof(ResumeSubscriptionAsync));
    }

    public async Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, string? reason,
        CancellationToken cancellationToken = default)
    {
        var request = BuildCancellationRequest(reason, cancelAtEndOfPeriod: false);

        var response = await InvokeAsync(nameof(CancelSubscriptionAsync), (client, ct) =>
            client.SubscriptionStatus.CancelSubscription(subscriptionId, request, ct: ct), cancellationToken);

        return RequireSubscription(response, nameof(CancelSubscriptionAsync));
    }

    public async Task<BillingSubscription> ScheduleCancellationAsync(int subscriptionId, string? reason,
        CancellationToken cancellationToken = default)
    {
        var request = BuildCancellationRequest(reason, cancelAtEndOfPeriod: true);

        var acknowledgement = await InvokeAsync(nameof(ScheduleCancellationAsync), (client, ct) =>
            client.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId, request, ct: ct), cancellationToken);

        _logger.LogInformation("Subscription {SubscriptionId} scheduled for end-of-period cancellation: {Message}",
            subscriptionId, acknowledgement.Message ?? "accepted");

        // The provider acknowledges a delayed cancellation with a message only, so the authoritative
        // state has to be read back.
        return await GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new BillingEntityNotFoundException(
                $"Subscription {subscriptionId} disappeared while scheduling its cancellation.",
                nameof(ScheduleCancellationAsync), (int)HttpStatusCode.NotFound);
    }

    public async Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(nameof(ReactivateSubscriptionAsync), (client, ct) =>
            client.SubscriptionStatus.ReactivateSubscription(subscriptionId, null, ct: ct), cancellationToken);

        return RequireSubscription(response, nameof(ReactivateSubscriptionAsync));
    }

    /// <summary>
    /// Applies the configured payment collection method to new subscriptions. Leaving it unset lets
    /// the provider decide, which is what a site that always captures a card wants.
    /// </summary>
    /// <exception cref="BillingConfigurationException">The configured value is not one the provider knows.</exception>
    private CollectionMethod? ResolvePaymentCollectionMethod()
    {
        var configured = _settings.PaymentCollectionMethod?.Trim();
        if (string.IsNullOrEmpty(configured))
        {
            return null;
        }

        if (!CollectionMethod.TryGetKnownValue(configured.ToLowerInvariant(), out var known))
        {
            throw new BillingConfigurationException(
                $"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.PaymentCollectionMethod)}' is set to an unrecognised value.");
        }

        return known;
    }

    /// <summary>
    /// A plan is normally addressed by its stable handle. When the caller supplies the provider's
    /// numeric identifier instead, that is sent as the identifier and no handle is claimed.
    /// </summary>
    private static string? AsPlanHandle(string planIdentifier)
        => IsPlanId(planIdentifier, out _) ? null : planIdentifier;

    private static int? AsPlanId(string planIdentifier)
        => IsPlanId(planIdentifier, out var planId) ? planId : null;

    private static bool IsPlanId(string planIdentifier, out int planId)
        => int.TryParse(planIdentifier, NumberStyles.Integer, CultureInfo.InvariantCulture, out planId);

    private static CancellationRequest BuildCancellationRequest(string? reason, bool cancelAtEndOfPeriod)
        => new()
        {
            Subscription = new CancellationOptions
            {
                CancellationMessage = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                CancelAtEndOfPeriod = cancelAtEndOfPeriod ? true : null
            }
        };

    /// <summary>
    /// Resolves a component of the product family by handle. The family's component list is the
    /// authoritative source; the direct lookups are tried first because they cost one call instead
    /// of a page walk.
    /// </summary>
    private async Task<BillingComponent?> ResolveComponentAsync(int familyId, string handle,
        CancellationToken cancellationToken)
    {
        if (_settings.MeteredComponentId > 0)
        {
            var byId = await TryReadComponentAsync(familyId,
                _settings.MeteredComponentId.ToString(CultureInfo.InvariantCulture), cancellationToken);

            if (byId is not null)
            {
                return byId;
            }
        }

        var byHandle = await TryReadComponentAsync(familyId, HandlePrefix + handle, cancellationToken);
        if (byHandle is not null)
        {
            return byHandle;
        }

        var byLookup = await TryFindComponentAsync(handle, cancellationToken);
        if (byLookup is not null)
        {
            return byLookup;
        }

        var components = await InvokeAsync(nameof(GetUsageComponentAsync), (client, ct) =>
            client.Components.ListComponentsForProductFamily(
                familyId,
                includeArchived: false,
                filter: null,
                dateField: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                page: 1,
                perPage: 200,
                ct: ct), cancellationToken);

        return components
            .Select(response => MapComponent(response.Component))
            .FirstOrDefault(component => string.Equals(component.Handle, handle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<BillingComponent?> TryReadComponentAsync(int familyId, string componentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await InvokeAsync(nameof(GetUsageComponentAsync), (client, ct) =>
                client.Components.ReadComponent(familyId, componentId, ct: ct), cancellationToken);

            return MapComponent(response.Component);
        }
        catch (BillingEntityNotFoundException)
        {
            return null;
        }
    }

    private async Task<BillingComponent?> TryFindComponentAsync(string handle, CancellationToken cancellationToken)
    {
        try
        {
            var response = await InvokeAsync(nameof(GetUsageComponentAsync), (client, ct) =>
                client.Components.FindComponent(handle, ct: ct), cancellationToken);

            return MapComponent(response.Component);
        }
        catch (BillingEntityNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the configured product family. Handles are the stable identifier; the numeric id is
    /// only a fallback for a deployment that configures no handle (plan §1.3).
    /// </summary>
    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_productFamilyId.HasValue)
        {
            return _productFamilyId.Value;
        }

        var handle = _settings.ProductFamilyHandle?.Trim();

        if (string.IsNullOrEmpty(handle))
        {
            if (_settings.ProductFamilyId > 0)
            {
                _productFamilyId = _settings.ProductFamilyId;
                return _productFamilyId.Value;
            }

            throw new BillingConfigurationException(
                $"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)}' is not configured, so the billing catalog cannot be located.");
        }

        var families = await InvokeAsync(nameof(ResolveProductFamilyIdAsync), (client, ct) =>
            client.ProductFamilies.ListProductFamilies(null, null, null, null, null, ct: ct), cancellationToken);

        var match = families
            .Select(response => response.ProductFamily)
            .FirstOrDefault(family => family is not null
                && string.Equals(family.Handle, handle, StringComparison.OrdinalIgnoreCase)
                && family.Id.HasValue);

        if (match?.Id is null)
        {
            throw new BillingConfigurationException(
                $"No product family with handle '{handle}' exists at the billing provider. Re-run the billing provider seed.");
        }

        _productFamilyId = match.Id.Value;
        return _productFamilyId.Value;
    }

    private MaxioAdvancedBillingClient CreateProviderClient()
    {
        var apiKey = _settings.ApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new BillingConfigurationException(
                $"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.ApiKey)}' is not configured. Supply it through user-secrets or an environment variable.");
        }

        var baseUrl = _settings.ResolveBaseUrl();

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = _settings.IsEuropeanRegion ? ServerEnvironment.Eu : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = apiKey,
                Password = ApiKeyPasswordPlaceholder
            },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = _settings.RetryCount,
                Timeout = _settings.Timeout
            }
        };

        var site = _settings.Subdomain?.Trim();

        // An explicit base URL carries no placeholders and is therefore used verbatim; the site is
        // still supplied so a placeholder-bearing override keeps working. The US and EU server option
        // objects are distinct types, so only the selected region is configured.
        if (_settings.IsEuropeanRegion)
        {
            options.Server.Production.Eu.BaseUrl = baseUrl;
            if (!string.IsNullOrEmpty(site))
            {
                options.Server.Production.Eu.Site = site;
            }
        }
        else
        {
            options.Server.Production.Us.BaseUrl = baseUrl;
            if (!string.IsNullOrEmpty(site))
            {
                options.Server.Production.Us.Site = site;
            }
        }

        return new MaxioAdvancedBillingClient(_httpClient, options);
    }

    /// <summary>
    /// Runs one provider call under the configured timeout and converts every provider failure into
    /// the billing exception family, so no SDK type ever escapes this class.
    /// </summary>
    private async Task<TResult> InvokeAsync<TResult>(string operation,
        Func<MaxioAdvancedBillingClient, CancellationToken, Task<TResult>> call,
        CancellationToken cancellationToken)
    {
        var client = _provider.Value;

        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(_settings.Timeout);

        try
        {
            return await call(client, attempt.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(operation, Read(ex.Error), ex);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw Translate(operation, Read(ex.Error), ex);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            throw Translate(operation, Read(ex.Error), ex);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw Translate(operation, Read(ex.Error), ex);
        }
        catch (SdkException<UpdateSubscriptionError> ex)
        {
            throw Translate(operation, Read(ex.Error), ex);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            throw Translate(operation, Read(ex.Error), ex);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            throw Translate(operation, Read(ex.Error), ex);
        }
        catch (SdkException<ReadSubscriptionComponentError> ex)
        {
            throw Translate(operation, Read(ex.Error), ex);
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            throw Translate(operation, Read(ex.Error), ex);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            throw Translate(operation, Read(ex.Error), ex);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            throw Translate(operation, Read(ex.Error), ex);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            throw Translate(operation, Read(ex.Error), ex);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            throw Translate(operation, Read(ex.Error), ex);
        }
        catch (SdkException<InitiateDelayedCancellationError> ex)
        {
            throw Translate(operation, Read(ex.Error), ex);
        }
        catch (SdkException<CancelDelayedCancellationError> ex)
        {
            throw Translate(operation, Read(ex.Error), ex);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            throw Translate(operation, Read(ex.Error), ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away. That is not a provider failure.
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new BillingProviderUnavailableException(
                $"The billing provider did not answer '{operation}' within {_settings.Timeout.TotalSeconds:0} seconds.",
                operation, innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Billing operation {Operation} could not reach the provider: {Reason}",
                operation, Redact(ex.Message));

            throw new BillingProviderUnavailableException(
                $"The billing provider could not be reached for '{operation}'.", operation, innerException: ex);
        }
        catch (JsonException ex)
        {
            // A malformed or unexpected payload is a provider fault. The parser's message names
            // internal types and payload offsets, so it is logged and never surfaced.
            _logger.LogWarning("Billing operation {Operation} returned an unreadable payload: {Reason}",
                operation, Redact(ex.Message));

            throw new BillingProviderUnavailableException(
                $"The billing provider returned a response that could not be understood for '{operation}'.",
                operation, innerException: ex);
        }
        catch (Exception ex) when (ex is not BillingProviderException and not BillingConfigurationException)
        {
            // Last line of defence: nothing from the provider stack may reach a caller unmapped.
            _logger.LogWarning("Billing operation {Operation} failed unexpectedly: {Reason}",
                operation, Redact(ex.Message));

            throw new BillingProviderUnavailableException(
                $"The billing provider could not complete '{operation}'.", operation, innerException: ex);
        }
    }

    private BillingProviderException Translate(string operation, ProviderFailure failure, Exception inner)
    {
        var messages = failure.Messages.Count == 0
            ? NoMessages
            : failure.Messages.Where(m => !string.IsNullOrWhiteSpace(m)).Select(Redact).ToList();

        var detail = messages.Count == 0 ? string.Empty : " " + string.Join(" ", messages);
        var status = failure.StatusCode;

        _logger.LogWarning("Billing operation {Operation} failed with provider status {StatusCode}.{Detail}",
            operation, status?.ToString(CultureInfo.InvariantCulture) ?? "none", detail);

        if (status == (int)HttpStatusCode.NotFound)
        {
            return new BillingEntityNotFoundException(
                $"The billing provider has no record matching '{operation}'.", operation, status, messages, inner);
        }

        if (status is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden)
        {
            // Never echo anything credential-shaped back to the caller.
            return new BillingProviderUnavailableException(
                $"The billing provider refused the credentials configured for '{operation}'.", operation, status,
                NoMessages, inner);
        }

        if (status >= 400 && status < 500)
        {
            return new BillingRequestRejectedException(
                $"The billing provider rejected '{operation}'.{detail}", operation, status, messages, inner);
        }

        return new BillingProviderUnavailableException(
            status.HasValue
                ? $"The billing provider could not complete '{operation}' (status {status.Value})."
                : $"The billing provider could not complete '{operation}'.",
            operation, status, messages, inner);
    }

    /// <summary>
    /// Removes the API key from any text that is about to be logged or surfaced. Provider payloads
    /// are not expected to echo credentials, but the guarantee is cheap and absolute.
    /// </summary>
    private string Redact(string text)
    {
        var apiKey = _settings.ApiKey;

        return string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(text)
            ? text
            : text.Replace(apiKey, RedactionMarker, StringComparison.Ordinal);
    }

    private static ProviderFailure Read(RawError raw) => new((int?)raw.StatusCode, NoMessages);

    private static ProviderFailure Read(ListProductsForProductFamilyError error)
    {
        if (error.TryGetString(out _))
        {
            return new ProviderFailure((int)HttpStatusCode.NotFound, NoMessages);
        }

        return FromRaw(error.TryGetRawError(out var raw) ? raw : null);
    }

    private static ProviderFailure Read(CreateCustomerError error)
    {
        if (error.TryGetCustomerErrorResponse1(out var customerError))
        {
            return new ProviderFailure((int)HttpStatusCode.UnprocessableEntity, Flatten(customerError));
        }

        return FromRaw(error.TryGetRawError(out var raw) ? raw : null);
    }

    private static ProviderFailure Read(CreateSubscriptionError error)
        => ReadErrorList(error.TryGetErrorListResponse1(out var list) ? list : null,
            error.TryGetRawError(out var raw) ? raw : null);

    private static ProviderFailure Read(UpdateSubscriptionError error)
        => ReadErrorList(error.TryGetErrorListResponse1(out var list) ? list : null,
            error.TryGetRawError(out var raw) ? raw : null);

    private static ProviderFailure Read(CreateUsageError error)
        => ReadErrorList(error.TryGetErrorListResponse1(out var list) ? list : null,
            error.TryGetRawError(out var raw) ? raw : null);

    private static ProviderFailure Read(PreviewSubscriptionProductMigrationError error)
        => ReadErrorList(error.TryGetErrorListResponse1(out var list) ? list : null,
            error.TryGetRawError(out var raw) ? raw : null);

    private static ProviderFailure Read(MigrateSubscriptionProductError error)
        => ReadErrorList(error.TryGetErrorListResponse1(out var list) ? list : null,
            error.TryGetRawError(out var raw) ? raw : null);

    private static ProviderFailure Read(PauseSubscriptionError error)
        => ReadErrorList(error.TryGetErrorListResponse1(out var list) ? list : null,
            error.TryGetRawError(out var raw) ? raw : null);

    private static ProviderFailure Read(ResumeSubscriptionError error)
        => ReadErrorList(error.TryGetErrorListResponse1(out var list) ? list : null,
            error.TryGetRawError(out var raw) ? raw : null);

    private static ProviderFailure Read(ReactivateSubscriptionError error)
        => ReadErrorList(error.TryGetErrorListResponse1(out var list) ? list : null,
            error.TryGetRawError(out var raw) ? raw : null);

    private static ProviderFailure Read(FindSubscriptionError error)
    {
        if (error.TryGetNoContent(out _))
        {
            return new ProviderFailure((int)HttpStatusCode.NotFound, NoMessages);
        }

        return FromRaw(error.TryGetRawError(out var raw) ? raw : null);
    }

    private static ProviderFailure Read(ReadSubscriptionComponentError error)
    {
        if (error.TryGetNoContent(out _))
        {
            return new ProviderFailure((int)HttpStatusCode.NotFound, NoMessages);
        }

        return FromRaw(error.TryGetRawError(out var raw) ? raw : null);
    }

    private static ProviderFailure Read(CancelDelayedCancellationError error)
    {
        if (error.TryGetNoContent(out _))
        {
            return new ProviderFailure((int)HttpStatusCode.NotFound, NoMessages);
        }

        return FromRaw(error.TryGetRawError(out var raw) ? raw : null);
    }

    private static ProviderFailure Read(InitiateDelayedCancellationError error)
    {
        if (error.TryGetNoContent(out _))
        {
            return new ProviderFailure((int)HttpStatusCode.NotFound, NoMessages);
        }

        return ReadErrorList(error.TryGetErrorListResponse1(out var list) ? list : null,
            error.TryGetRawError(out var raw) ? raw : null);
    }

    private static ProviderFailure Read(CancelSubscriptionApiError error)
    {
        if (error.TryGetNoContent(out _))
        {
            return new ProviderFailure((int)HttpStatusCode.NotFound, NoMessages);
        }

        if (error.TryGetCancelSubscriptionErrorResponse(out var response))
        {
            if (response.TryGetErrorListResponse1(out var list))
            {
                return new ProviderFailure((int)HttpStatusCode.UnprocessableEntity, list.Errors ?? NoMessages);
            }

            if (response.TryGetSingleErrorResponse1(out var single))
            {
                return new ProviderFailure((int)HttpStatusCode.UnprocessableEntity, new[] { single.Error });
            }

            return new ProviderFailure((int)HttpStatusCode.UnprocessableEntity, NoMessages);
        }

        return FromRaw(error.TryGetRawError(out var raw) ? raw : null);
    }

    private static ProviderFailure ReadErrorList(ErrorListResponse1? list, RawError? raw)
    {
        if (list is not null)
        {
            return new ProviderFailure((int)HttpStatusCode.UnprocessableEntity, list.Errors ?? NoMessages);
        }

        return FromRaw(raw);
    }

    private static ProviderFailure FromRaw(RawError? raw)
        => new(raw is null ? null : (int)raw.StatusCode, NoMessages);

    private static IReadOnlyList<string> Flatten(CustomerErrorResponse1 error)
    {
        var errors = error.Errors;
        if (errors is null)
        {
            return NoMessages;
        }

        var messages = new List<string>();
        if (errors.PerPage is not null)
        {
            messages.AddRange(errors.PerPage);
        }

        if (errors.PricePoint is not null)
        {
            messages.AddRange(errors.PricePoint);
        }

        return messages;
    }

    private static BillingSubscription RequireSubscription(SubscriptionResponse response, string operation)
    {
        if (response.Subscription is null)
        {
            throw new BillingProviderUnavailableException(
                $"The billing provider returned no subscription for '{operation}'.", operation);
        }

        return MapSubscription(response.Subscription);
    }

    private static BillingPlan MapPlan(Product product) => new(
        product.Id ?? 0,
        product.Handle ?? string.Empty,
        product.Name ?? string.Empty,
        ToMajorUnits(product.PriceInCents),
        product.Interval ?? 0,
        product.IntervalUnit?.Value ?? string.Empty,
        product.RequireCreditCard ?? false,
        product.ArchivedAt.HasValue);

    private static BillingCustomer MapCustomer(Customer customer) => new(
        customer.Id ?? 0,
        customer.Reference ?? string.Empty,
        customer.Email ?? string.Empty,
        customer.FirstName ?? string.Empty,
        customer.LastName ?? string.Empty);

    private static BillingComponent MapComponent(Component component)
    {
        var kind = component.Kind?.Value ?? string.Empty;

        return new BillingComponent(
            component.Id ?? 0,
            component.Handle ?? string.Empty,
            component.Name ?? string.Empty,
            kind,
            component.Kind == ComponentKind.MeteredComponent,
            ResolveUnitPrice(component),
            component.PricingScheme?.Value,
            component.UnitName);
    }

    /// <summary>
    /// The provider reports a component's price either as an integer number of cents or as a decimal
    /// string in major units. The cents field is authoritative when present.
    /// </summary>
    private static decimal ResolveUnitPrice(Component component)
    {
        if (component.PricePerUnitInCents.HasValue)
        {
            return ToMajorUnits(component.PricePerUnitInCents);
        }

        if (!string.IsNullOrWhiteSpace(component.UnitPrice)
            && decimal.TryParse(component.UnitPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0m;
    }

    private static BillingSubscription MapSubscription(Subscription subscription)
    {
        var product = subscription.Product;

        return new BillingSubscription(
            subscription.Id ?? 0,
            MapState(subscription.State),
            subscription.State?.Value ?? string.Empty,
            subscription.Customer?.Id,
            subscription.Customer?.Reference,
            product?.Id,
            product?.Handle,
            product?.Name,
            product?.PriceInCents is null
                ? ToMajorUnits(subscription.ProductPriceInCents)
                : ToMajorUnits(product.PriceInCents),
            ToMajorUnits(subscription.BalanceInCents),
            subscription.Currency,
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt,
            subscription.CancelAtEndOfPeriod ?? false,
            subscription.ScheduledCancellationAt ?? subscription.DelayedCancelAt,
            subscription.NextProductHandle);
    }

    /// <summary>
    /// Maps the provider's state onto the domain enum. An unrecognised state deliberately becomes
    /// <see cref="BillingSubscriptionState.Unknown"/> rather than being guessed at, so a state this
    /// build does not know about is never mistaken for a terminated subscription.
    /// </summary>
    private static BillingSubscriptionState MapState(SubscriptionState? state) => state?.Value switch
    {
        "pending" => BillingSubscriptionState.Pending,
        "trialing" => BillingSubscriptionState.Trialing,
        "assessing" => BillingSubscriptionState.Assessing,
        "active" => BillingSubscriptionState.Active,
        "soft_failure" => BillingSubscriptionState.SoftFailure,
        "past_due" => BillingSubscriptionState.PastDue,
        "suspended" => BillingSubscriptionState.Suspended,
        "canceled" => BillingSubscriptionState.Canceled,
        "expired" => BillingSubscriptionState.Expired,
        "paused" => BillingSubscriptionState.Paused,
        "unpaid" => BillingSubscriptionState.Unpaid,
        "trial_ended" => BillingSubscriptionState.TrialEnded,
        "on_hold" => BillingSubscriptionState.OnHold,
        "awaiting_signup" => BillingSubscriptionState.AwaitingSignup,
        "failed_to_create" => BillingSubscriptionState.FailedToCreate,
        _ => BillingSubscriptionState.Unknown
    };

    private static UsageRecord MapUsage(Usage usage) => new(
        usage.Id ?? 0,
        ReadQuantity(usage.Quantity),
        usage.Memo,
        usage.CreatedAt,
        usage.ComponentId,
        usage.ComponentHandle);

    /// <summary>The provider returns a usage quantity either as a number or as a decimal string.</summary>
    private static decimal ReadQuantity(Quantity1? quantity)
    {
        if (quantity is null)
        {
            return 0m;
        }

        if (quantity.TryGetInt(out var whole))
        {
            return whole;
        }

        if (quantity.TryGetString(out var text)
            && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0m;
    }

    private static decimal ToMajorUnits(long? cents) => cents.HasValue ? cents.Value / (decimal)CentsPerUnit : 0m;

    private readonly record struct ProviderFailure(int? StatusCode, IReadOnlyList<string> Messages);
}
