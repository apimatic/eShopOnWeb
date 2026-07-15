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
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single Infrastructure seam that talks to Maxio Advanced Billing (via the
/// maxio-sdk-exp1-agents plugin's generated SDK). Nothing else in the solution may
/// reference the Maxio SDK directly — see IBillingClient (ApplicationCore).
///
/// The SDK does not read HttpClient.BaseAddress: it builds each request's absolute URL
/// from MaxioAdvancedBillingClientOptions.Server.Production.{Region}.{BaseUrl|Site}. The
/// injected HttpClient still comes from IHttpClientFactory (typed client, §4.3) and only
/// carries the message-handler pipeline; the actual "which host" decision from
/// MaxioSettings (explicit BaseUrl wins, else Subdomain-derived — §2.3) is applied to
/// those Server options here, the one place retargeting happens.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly SemaphoreSlim _componentResolveLock = new(1, 1);
    private int? _meteredComponentId;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> options)
    {
        _settings = options.Value;

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = _settings.ApiKey, Password = "x" },
            Environment = string.Equals(_settings.Environment, "EU", StringComparison.OrdinalIgnoreCase)
                ? ServerEnvironment.Eu
                : ServerEnvironment.Us
        };

        if (clientOptions.Environment == ServerEnvironment.Eu)
        {
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                clientOptions.Server.Production.Eu.BaseUrl = _settings.BaseUrl;
            }
            else
            {
                clientOptions.Server.Production.Eu.Site = _settings.Subdomain;
            }
        }
        else if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            clientOptions.Server.Production.Us.BaseUrl = _settings.BaseUrl;
        }
        else
        {
            clientOptions.Server.Production.Us.Site = _settings.Subdomain;
        }

        _client = new MaxioAdvancedBillingClient(httpClient, clientOptions);
    }

    public async Task ValidateConfigurationAsync(CancellationToken cancellationToken = default)
    {
        await ResolveMeteredComponentIdAsync(cancellationToken);

        foreach (var handle in new[] { _settings.DefaultProductHandle, _settings.AlternateProductHandle })
        {
            await ReadProductByHandleOrThrowAsync(handle, cancellationToken);
        }

        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(null, null, null, null, null, cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Unable to list Maxio product families. {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable while listing product families.", ex);
        }

        var familyExists = families.Any(f => string.Equals(f.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));
        if (!familyExists)
        {
            throw new BillingProviderException($"Configured product family handle '{_settings.ProductFamilyHandle}' does not resolve against the Maxio sandbox — see UC0 (seed the sandbox) in plan.md.");
        }
    }

    public async Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var plans = new List<BillingPlan>();
        foreach (var handle in new[] { _settings.DefaultProductHandle, _settings.AlternateProductHandle })
        {
            var product = await ReadProductByHandleOrThrowAsync(handle, cancellationToken);
            plans.Add(new BillingPlan(
                product.Handle ?? handle,
                product.Name ?? handle,
                product.PriceInCents ?? 0,
                (int)(product.Interval ?? 1),
                product.IntervalUnit?.Value ?? "month"));
        }

        return plans;
    }

    public async Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, cancellationToken);
            return response.Customer == null ? null : ToBillingCustomer(response.Customer);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Unable to look up the Maxio customer for '{reference}'. {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable while looking up the customer.", ex);
        }
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerAsync(reference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

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
            }, cancellationToken);

            if (response.Customer == null)
            {
                throw new BillingProviderException($"Maxio returned an empty customer payload for reference '{reference}'.");
            }

            return ToBillingCustomer(response.Customer);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var validation))
            {
                throw new BillingProviderException($"Maxio rejected the customer for '{reference}': {DescribeCustomerErrors(validation)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Maxio rejected the customer for '{reference}': {raw.ReadAsString()}", ex);
            }

            throw new BillingProviderException($"Maxio rejected the customer for '{reference}'.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable while creating the customer.", ex);
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(customerId, cancellationToken);
            return responses.Select(r => ToBillingSubscription(r.Subscription)).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Unable to list subscriptions for customer {customerId}. {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable while listing subscriptions.", ex);
        }
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(string customerReference, string productHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerReference = customerReference,
                    ProductHandle = productHandle,
                    // The demo products have "requires payment method" off (UC0), but this site
                    // has Relationship Invoicing enabled with a default collection method of
                    // "automatic" (card-charge). Remittance is RIA's non-card collection method,
                    // so signup here never demands a card or triggers 3DS — see plan.md UC1.
                    PaymentCollectionMethod = CollectionMethod.Remittance
                }
            }, cancellationToken);

            return ToBillingSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Maxio rejected the subscription for product '{productHandle}': {string.Join("; ", errors.Errors)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Maxio rejected the subscription for product '{productHandle}': {raw.ReadAsString()}", ex);
            }

            throw new BillingProviderException($"Maxio rejected the subscription for product '{productHandle}'.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable while creating the subscription.", ex);
        }
    }

    public async Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId, null, cancellationToken);
            return ToBillingSubscription(response.Subscription);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new SubscriptionNotFoundException(subscriptionId);
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Unable to read subscription {subscriptionId}. {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable while reading the subscription.", ex);
        }
    }

    public async Task<UsageResult> RecordUsageAsync(int subscriptionId, double quantity, string? memo, CancellationToken cancellationToken = default)
    {
        var componentId = await ResolveMeteredComponentIdAsync(cancellationToken);

        Usage usage;
        try
        {
            var response = await _client.SubscriptionComponents.CreateUsage(
                SubscriptionIdOrReference.Int(subscriptionId),
                ComponentIdModel.Int(componentId),
                new CreateUsageRequest { Usage = new CreateUsage { Quantity = quantity, Memo = memo } },
                cancellationToken);

            usage = response.Usage;
        }
        catch (SdkException<CreateUsageError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Maxio rejected the usage record for subscription {subscriptionId}: {string.Join("; ", errors.Errors)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Maxio rejected the usage record for subscription {subscriptionId}: {raw.ReadAsString()}", ex);
            }

            throw new BillingProviderException($"Maxio rejected the usage record for subscription {subscriptionId}.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable while recording usage.", ex);
        }

        var quantityRecorded = quantity;
        if (usage.Quantity != null)
        {
            if (usage.Quantity.TryGetInt(out var intQuantity))
            {
                quantityRecorded = intQuantity;
            }
            else if (usage.Quantity.TryGetString(out var stringQuantity) && double.TryParse(stringQuantity, out var parsedQuantity))
            {
                quantityRecorded = parsedQuantity;
            }
        }

        int? balance = null;
        try
        {
            balance = await GetMeteredUsageBalanceAsync(subscriptionId, cancellationToken);
        }
        catch (BillingProviderException)
        {
            // Best-effort read-back per UC2: the usage record above already stands.
        }

        return new UsageResult(usage.Id ?? 0, quantityRecorded, usage.Memo, balance);
    }

    public async Task<int?> GetMeteredUsageBalanceAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var componentId = await ResolveMeteredComponentIdAsync(cancellationToken);

        try
        {
            var response = await _client.SubscriptionComponents.ReadSubscriptionComponent(subscriptionId, componentId, cancellationToken);
            return (int?)response.Component?.UnitBalance;
        }
        catch (SdkException<ReadSubscriptionComponentError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Unable to read the usage balance for subscription {subscriptionId}: {raw.ReadAsString()}", ex);
            }

            throw new BillingProviderException($"Unable to read the usage balance for subscription {subscriptionId}.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable while reading the usage balance.", ex);
        }
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyNow, CancellationToken cancellationToken = default)
    {
        if (!applyNow)
        {
            var subscription = await GetSubscriptionAsync(subscriptionId, cancellationToken);
            var targetProduct = await ReadProductByHandleOrThrowAsync(targetProductHandle, cancellationToken);
            return new PlanChangePreview(0, targetProduct.PriceInCents ?? 0, targetProduct.PriceInCents ?? 0, 0, subscription.CurrentPeriodEndsAt ?? DateTimeOffset.UtcNow);
        }

        try
        {
            var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(subscriptionId,
                new SubscriptionMigrationPreviewRequest
                {
                    Migration = new SubscriptionMigrationPreviewOptions { ProductHandle = targetProductHandle }
                }, cancellationToken);

            var migration = response.Migration;
            return new PlanChangePreview(
                migration.ProratedAdjustmentInCents ?? 0,
                migration.ChargeInCents ?? 0,
                migration.PaymentDueInCents ?? 0,
                migration.CreditAppliedInCents ?? 0,
                DateTimeOffset.UtcNow);
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Maxio rejected the plan-change preview for subscription {subscriptionId}: {string.Join("; ", errors.Errors)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Maxio rejected the plan-change preview for subscription {subscriptionId}: {raw.ReadAsString()}", ex);
            }

            throw new BillingProviderException($"Maxio rejected the plan-change preview for subscription {subscriptionId}.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable while previewing the plan change.", ex);
        }
    }

    public async Task<BillingSubscription> ChangePlanAsync(int subscriptionId, string targetProductHandle, bool applyNow, CancellationToken cancellationToken = default)
    {
        try
        {
            if (applyNow)
            {
                var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(subscriptionId,
                    new SubscriptionProductMigrationRequest
                    {
                        Migration = new SubscriptionProductMigration { ProductHandle = targetProductHandle }
                    }, cancellationToken);
                return ToBillingSubscription(response.Subscription);
            }

            var delayedResponse = await _client.Subscriptions.UpdateSubscription(subscriptionId,
                new UpdateSubscriptionRequest
                {
                    Subscription = new UpdateSubscription { ProductHandle = targetProductHandle, ProductChangeDelayed = true }
                }, cancellationToken);
            return ToBillingSubscription(delayedResponse.Subscription);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Maxio rejected the plan change for subscription {subscriptionId}: {string.Join("; ", errors.Errors)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Maxio rejected the plan change for subscription {subscriptionId}: {raw.ReadAsString()}", ex);
            }

            throw new BillingProviderException($"Maxio rejected the plan change for subscription {subscriptionId}.", ex);
        }
        catch (SdkException<UpdateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Maxio rejected the delayed plan change for subscription {subscriptionId}: {string.Join("; ", errors.Errors)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Maxio rejected the delayed plan change for subscription {subscriptionId}: {raw.ReadAsString()}", ex);
            }

            throw new BillingProviderException($"Maxio rejected the delayed plan change for subscription {subscriptionId}.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable while changing the plan.", ex);
        }
    }

    public async Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.PauseSubscription(subscriptionId, null, cancellationToken);
            return ToBillingSubscription(response.Subscription);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Maxio rejected pausing subscription {subscriptionId}: {string.Join("; ", errors.Errors)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Maxio rejected pausing subscription {subscriptionId}: {raw.ReadAsString()}", ex);
            }

            throw new BillingProviderException($"Maxio rejected pausing subscription {subscriptionId}.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable while pausing the subscription.", ex);
        }
    }

    public async Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ResumeSubscription(subscriptionId, null, cancellationToken);
            return ToBillingSubscription(response.Subscription);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Maxio rejected resuming subscription {subscriptionId}: {string.Join("; ", errors.Errors)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Maxio rejected resuming subscription {subscriptionId}: {raw.ReadAsString()}", ex);
            }

            throw new BillingProviderException($"Maxio rejected resuming subscription {subscriptionId}.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable while resuming the subscription.", ex);
        }
    }

    public async Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default)
    {
        try
        {
            if (endOfPeriod)
            {
                await _client.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId,
                    new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = reason } },
                    cancellationToken);
                return await GetSubscriptionAsync(subscriptionId, cancellationToken);
            }

            var response = await _client.SubscriptionStatus.CancelSubscription(subscriptionId,
                new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = reason } },
                cancellationToken);
            return ToBillingSubscription(response.Subscription);
        }
        catch (SdkException<InitiateDelayedCancellationError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }

            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Maxio rejected the end-of-period cancellation for subscription {subscriptionId}: {string.Join("; ", errors.Errors)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Maxio rejected the end-of-period cancellation for subscription {subscriptionId}: {raw.ReadAsString()}", ex);
            }

            throw new BillingProviderException($"Maxio rejected the end-of-period cancellation for subscription {subscriptionId}.", ex);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }

            if (ex.Error.TryGetCancelSubscriptionErrorResponse(out var union))
            {
                var message = union.TryGetErrorListResponse1(out var list)
                    ? string.Join("; ", list.Errors)
                    : union.TryGetSingleErrorResponse1(out var single)
                        ? single.Error
                        : "cancellation rejected";
                throw new BillingProviderException($"Maxio rejected cancellation of subscription {subscriptionId}: {message}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Maxio rejected cancellation of subscription {subscriptionId}: {raw.ReadAsString()}", ex);
            }

            throw new BillingProviderException($"Maxio rejected cancellation of subscription {subscriptionId}.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable while cancelling the subscription.", ex);
        }
    }

    public async Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, null, cancellationToken);
            return ToBillingSubscription(response.Subscription);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Maxio rejected reactivating subscription {subscriptionId}: {string.Join("; ", errors.Errors)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Maxio rejected reactivating subscription {subscriptionId}: {raw.ReadAsString()}", ex);
            }

            throw new BillingProviderException($"Maxio rejected reactivating subscription {subscriptionId}.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable while reactivating the subscription.", ex);
        }
    }

    private async Task<Product> ReadProductByHandleOrThrowAsync(string handle, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Products.ReadProductByHandle(handle, cancellationToken);
            return response.Product ?? throw new BillingProviderException($"Maxio returned an empty product payload for handle '{handle}'.");
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Configured product handle '{handle}' does not resolve against the Maxio sandbox — see UC0 (seed the sandbox) in plan.md. {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is unreachable while resolving a product handle.", ex);
        }
    }

    private async Task<int> ResolveMeteredComponentIdAsync(CancellationToken cancellationToken)
    {
        if (_meteredComponentId.HasValue)
        {
            return _meteredComponentId.Value;
        }

        await _componentResolveLock.WaitAsync(cancellationToken);
        try
        {
            if (_meteredComponentId.HasValue)
            {
                return _meteredComponentId.Value;
            }

            ComponentResponse response;
            try
            {
                response = await _client.Components.FindComponent(_settings.MeteredComponentHandle, cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                throw new BillingProviderException($"Configured metered component handle '{_settings.MeteredComponentHandle}' does not resolve against the Maxio sandbox — see UC0 (seed the sandbox) in plan.md. {ex.Error.ReadAsString()}", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new BillingProviderException("Maxio is unreachable while resolving the metered component.", ex);
            }

            var component = response.Component ?? throw new BillingProviderException($"Maxio returned an empty component payload for handle '{_settings.MeteredComponentHandle}'.");
            if (component.Kind != ComponentKind.MeteredComponent)
            {
                throw new BillingProviderException($"Component '{_settings.MeteredComponentHandle}' is of kind '{component.Kind}', not metered — see UC0's failure scenarios in plan.md for how to fix the sandbox seed.");
            }

            _meteredComponentId = (int)(component.Id ?? throw new BillingProviderException($"Maxio returned a component with no id for handle '{_settings.MeteredComponentHandle}'."));
            return _meteredComponentId.Value;
        }
        finally
        {
            _componentResolveLock.Release();
        }
    }

    private static string DescribeCustomerErrors(CustomerErrorResponse1 errors)
    {
        var messages = new List<string>();
        if (errors.Errors?.PerPage is { Count: > 0 } perPage)
        {
            messages.AddRange(perPage);
        }

        if (errors.Errors?.PricePoint is { Count: > 0 } pricePoint)
        {
            messages.AddRange(pricePoint);
        }

        return messages.Count > 0 ? string.Join("; ", messages) : "validation failed (no further detail returned by Maxio)";
    }

    private static BillingCustomer ToBillingCustomer(Customer customer) => new(
        (int)(customer.Id ?? 0),
        customer.Reference,
        customer.Email,
        customer.FirstName,
        customer.LastName);

    private static BillingSubscription ToBillingSubscription(Subscription? subscription)
    {
        if (subscription == null)
        {
            throw new BillingProviderException("Maxio returned an empty subscription payload.");
        }

        return new BillingSubscription(
            (int)(subscription.Id ?? 0),
            MapState(subscription.State),
            subscription.Product?.Handle,
            subscription.Product?.Name,
            subscription.Product?.PriceInCents ?? 0,
            (int)(subscription.Customer?.Id ?? 0),
            subscription.Customer?.Reference,
            subscription.NextAssessmentAt,
            subscription.CurrentPeriodEndsAt,
            subscription.DelayedCancelAt);
    }

    private static BillingSubscriptionState MapState(SubscriptionState? state) => state?.Value switch
    {
        "pending" => BillingSubscriptionState.Pending,
        "awaiting_signup" => BillingSubscriptionState.AwaitingSignup,
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
        "failed_to_create" => BillingSubscriptionState.FailedToCreate,
        _ => BillingSubscriptionState.Unknown
    };
}
