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
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

// The single class in this repository allowed to reference the Maxio Advanced Billing SDK
// (see ApplicationCore.Interfaces.IBillingClient). Registered as a typed HttpClient
// (IHttpClientFactory) so the HttpClient itself is pooled/managed by the framework; the
// MaxioAdvancedBillingClient it wraps is safe to reuse for the lifetime of this instance.
//
// Target-server resolution (explicit Maxio:BaseUrl wins, else Subdomain + Environment region)
// happens entirely through MaxioAdvancedBillingClientOptions.Server — this SDK resolves the
// outbound host itself and does not use HttpClient.BaseAddress.
public class MaxioBillingClient : IBillingClient
{
    private static volatile bool _meteredComponentValidated;
    private static volatile int _meteredComponentId;
    private static readonly SemaphoreSlim ValidationLock = new(1, 1);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> options)
    {
        _settings = options.Value;

        var isEu = string.Equals(_settings.Environment, "EU", StringComparison.OrdinalIgnoreCase);
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
            Retry = RetryOptions.Default(),
            BasicAuth = new BasicAuthCredentials { Username = _settings.ApiKey, Password = "x" },
        };

        if (isEu)
        {
            clientOptions.Server.Production.Eu.Site = _settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                clientOptions.Server.Production.Eu.BaseUrl = _settings.BaseUrl;
            }
        }
        else
        {
            clientOptions.Server.Production.Us.Site = _settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                clientOptions.Server.Production.Us.BaseUrl = _settings.BaseUrl;
            }
        }

        _client = new MaxioAdvancedBillingClient(httpClient, clientOptions);
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct = default)
    {
        try
        {
            // ListProductsForProductFamily's `productFamilyId` accepts either the numeric id or
            // the handle prefixed with "handle:" (see Api/ProductFamilies.cs's XML doc-comment on
            // this parameter). Prefer the handle: it's stable identifying data for the family,
            // whereas the numeric id mirrored into configuration can drift out of sync with the
            // provider's real id for that handle (verified against the live sandbox: the
            // configured Maxio:ProductFamilyId was stale and produced an empty-body 404 that the
            // SDK's error mapping — which expects a JSON string body on 404 — could not parse).
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: $"handle:{_settings.ProductFamilyHandle}",
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                ct: ct);

            return products
                .Where(p => p.Product != null)
                .Select(p => MapPlan(p.Product!))
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFoundMessage))
            {
                throw new BillingProviderException(notFoundMessage, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException(DescribeRaw(raw), ex);
            }
            throw new BillingProviderException("Failed to list subscription plans.", ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw new BillingProviderException("Could not reach the billing provider.", ex);
        }
    }

    public async Task EnsureMeteredComponentIsValidAsync(CancellationToken ct = default)
    {
        if (_meteredComponentValidated)
        {
            return;
        }

        await ValidationLock.WaitAsync(ct);
        try
        {
            if (_meteredComponentValidated)
            {
                return;
            }

            Component component;
            try
            {
                var response = await _client.Components.FindComponent(handle: _settings.MeteredComponentHandle, ct: ct);
                component = response.Component ?? throw new BillingProviderException(
                    $"Metered component '{_settings.MeteredComponentHandle}' was not found on the billing provider. Re-run UC0 seeding.");
            }
            catch (SdkException<RawError> ex)
            {
                throw new BillingProviderException(
                    $"Could not validate metered component '{_settings.MeteredComponentHandle}': {DescribeRaw(ex.Error)}", ex);
            }
            catch (Exception ex) when (IsConnectionFailure(ex, ct))
            {
                throw new BillingProviderException("Could not reach the billing provider.", ex);
            }

            if (component.Kind != ComponentKind.MeteredComponent)
            {
                throw new BillingProviderException(
                    $"Component '{_settings.MeteredComponentHandle}' exists but is not of Metered kind (found: {component.Kind?.Value}). Re-seed it as Metered (see UC0).");
            }

            // Resolve the live numeric id from the handle rather than trusting configuration —
            // Maxio assigns ids at creation and a reseeded sandbox invalidates any id on file.
            _meteredComponentId = component.Id ?? 0;
            _meteredComponentValidated = true;
        }
        finally
        {
            ValidationLock.Release();
        }
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string customerReference,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: customerReference, ct: ct);
            return response.Customer != null ? MapCustomer(response.Customer) : null;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            throw new BillingProviderException(DescribeRaw(ex.Error), ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw new BillingProviderException("Could not reach the billing provider.", ex);
        }
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(string customerReference, string email, string firstName,
        string lastName, CancellationToken ct = default)
    {
        var existing = await FindCustomerByReferenceAsync(customerReference, ct);
        if (existing != null)
        {
            return existing;
        }

        try
        {
            var response = await _client.Customers.CreateCustomer(body: new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = customerReference,
                }
            }, ct: ct);

            return MapCustomer(response.Customer!);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // The reference must be unique on this site — a 422 here most likely means a
            // concurrent request already created the customer; recover instead of failing.
            var recovered = await FindCustomerByReferenceAsync(customerReference, ct);
            if (recovered != null)
            {
                return recovered;
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out var typed))
            {
                throw new BillingProviderException(DescribeCustomerErrors(typed), ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException(DescribeRaw(raw), ex);
            }
            throw new BillingProviderException("Failed to create billing customer.", ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw new BillingProviderException("Could not reach the billing provider.", ex);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(int billingCustomerId,
        CancellationToken ct = default)
    {
        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId: billingCustomerId, ct: ct);
            return subscriptions
                .Where(s => s.Subscription != null)
                .Select(s => MapSubscription(s.Subscription!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException(DescribeRaw(ex.Error), ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw new BillingProviderException("Could not reach the billing provider.", ex);
        }
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(int billingCustomerId, string planHandle,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body: new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = billingCustomerId,
                    // The seeded products have no trial, so the full balance is due at creation.
                    // "Requires payment method = off" (§1.3/UC0) means signup must not demand a
                    // card/3-DS — collect via invoice/remittance rather than auto-charging a
                    // payment profile that doesn't exist.
                    PaymentCollectionMethod = CollectionMethod.Invoice,
                }
            }, ct: ct);

            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var typed))
            {
                throw new BillingProviderException(DescribeErrorList(typed), ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException(DescribeRaw(raw), ex);
            }
            throw new BillingProviderException("Failed to create subscription.", ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw new BillingProviderException("Could not reach the billing provider.", ex);
        }
    }

    public async Task<CustomerSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId: subscriptionId, include: null, ct: ct);
            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }
            throw new BillingProviderException(DescribeRaw(ex.Error), ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw new BillingProviderException("Could not reach the billing provider.", ex);
        }
    }

    public async Task<UsageRecordResult> RecordUsageAsync(int subscriptionId, double quantity, string? memo,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionComponents.CreateUsage(
                subscriptionIdOrReference: SubscriptionIdOrReference.Int(subscriptionId),
                componentId: ComponentIdModel.String($"handle:{_settings.MeteredComponentHandle}"),
                body: new CreateUsageRequest
                {
                    Usage = new CreateUsage
                    {
                        Quantity = quantity,
                        Memo = memo,
                    }
                },
                ct: ct);

            var usage = response.Usage!;
            return new UsageRecordResult(usage.Id ?? 0, ReadQuantity(usage.Quantity), usage.CreatedAt ?? DateTimeOffset.UtcNow, null);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var typed))
            {
                throw new BillingProviderException(DescribeErrorList(typed), ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException(DescribeRaw(raw), ex);
            }
            throw new BillingProviderException("Failed to record usage.", ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw new BillingProviderException("Could not reach the billing provider.", ex);
        }
    }

    public async Task<int?> TryGetMeteredComponentBalanceAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            // ReadSubscriptionComponent takes a plain numeric component id (no handle option),
            // so resolve it live from the handle rather than trusting configuration.
            await EnsureMeteredComponentIsValidAsync(ct);

            var response = await _client.SubscriptionComponents.ReadSubscriptionComponent(
                subscriptionId: subscriptionId, componentId: _meteredComponentId, ct: ct);
            return response.Component?.UnitBalance;
        }
        catch
        {
            // Best-effort read-back only: the usage record itself already succeeded by the
            // time this is called (see UC2's "read-back fails" failure scenario).
            return null;
        }
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(
                subscriptionId: subscriptionId,
                body: new SubscriptionMigrationPreviewRequest
                {
                    Migration = new SubscriptionMigrationPreviewOptions
                    {
                        ProductHandle = targetPlanHandle,
                    }
                },
                ct: ct);

            var migration = response.Migration!;
            return new PlanChangePreview(
                targetPlanHandle,
                migration.ProratedAdjustmentInCents ?? 0,
                migration.ChargeInCents ?? 0,
                migration.PaymentDueInCents ?? 0,
                migration.CreditAppliedInCents ?? 0);
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var typed))
            {
                throw new BillingProviderException(DescribeErrorList(typed), ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException(DescribeRaw(raw), ex);
            }
            throw new BillingProviderException("Failed to preview plan change.", ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw new BillingProviderException("Could not reach the billing provider.", ex);
        }
    }

    public async Task<CustomerSubscription> ApplyPlanChangeNowAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(
                subscriptionId: subscriptionId,
                body: new SubscriptionProductMigrationRequest
                {
                    Migration = new SubscriptionProductMigration
                    {
                        ProductHandle = targetPlanHandle,
                    }
                },
                ct: ct);

            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var typed))
            {
                throw new BillingProviderException(DescribeErrorList(typed), ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException(DescribeRaw(raw), ex);
            }
            throw new BillingProviderException("Failed to change plan.", ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw new BillingProviderException("Could not reach the billing provider.", ex);
        }
    }

    public async Task<CustomerSubscription> SchedulePlanChangeAtRenewalAsync(int subscriptionId,
        string targetPlanHandle, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Subscriptions.UpdateSubscription(
                subscriptionId: subscriptionId,
                body: new UpdateSubscriptionRequest
                {
                    Subscription = new UpdateSubscription
                    {
                        ProductHandle = targetPlanHandle,
                        ProductChangeDelayed = true,
                    }
                },
                ct: ct);

            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<UpdateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var typed))
            {
                throw new BillingProviderException(DescribeErrorList(typed), ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException(DescribeRaw(raw), ex);
            }
            throw new BillingProviderException("Failed to schedule plan change.", ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw new BillingProviderException("Could not reach the billing provider.", ex);
        }
    }

    public async Task<CustomerSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.PauseSubscription(
                subscriptionId: subscriptionId, body: new PauseRequest(), ct: ct);
            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var typed))
            {
                throw new BillingProviderException(DescribeErrorList(typed), ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException(DescribeRaw(raw), ex);
            }
            throw new BillingProviderException("Failed to pause subscription.", ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw new BillingProviderException("Could not reach the billing provider.", ex);
        }
    }

    public async Task<CustomerSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ResumeSubscription(
                subscriptionId: subscriptionId, calendarBillingResumptionCharge: null, ct: ct);
            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var typed))
            {
                throw new BillingProviderException(DescribeErrorList(typed), ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException(DescribeRaw(raw), ex);
            }
            throw new BillingProviderException("Failed to resume subscription.", ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw new BillingProviderException("Could not reach the billing provider.", ex);
        }
    }

    public async Task<CustomerSubscription> CancelSubscriptionAsync(int subscriptionId, string? reason,
        bool endOfPeriod, CancellationToken ct = default)
    {
        if (endOfPeriod)
        {
            try
            {
                await _client.SubscriptionStatus.InitiateDelayedCancellation(
                    subscriptionId: subscriptionId,
                    body: new CancellationRequest
                    {
                        Subscription = new CancellationOptions { CancellationMessage = reason }
                    },
                    ct: ct);
            }
            catch (SdkException<InitiateDelayedCancellationError> ex)
            {
                if (ex.Error.TryGetNoContent(out _))
                {
                    throw new SubscriptionNotFoundException(subscriptionId);
                }
                if (ex.Error.TryGetErrorListResponse1(out var typed))
                {
                    throw new BillingProviderException(DescribeErrorList(typed), ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new BillingProviderException(DescribeRaw(raw), ex);
                }
                throw new BillingProviderException("Failed to schedule cancellation.", ex);
            }
            catch (Exception ex) when (IsConnectionFailure(ex, ct))
            {
                throw new BillingProviderException("Could not reach the billing provider.", ex);
            }

            // The delayed-cancellation endpoint returns only a confirmation message, not the
            // subscription itself — re-read it so the caller sees the up-to-date state.
            return await GetSubscriptionAsync(subscriptionId, ct);
        }

        try
        {
            var response = await _client.SubscriptionStatus.CancelSubscription(
                subscriptionId: subscriptionId,
                body: new CancellationRequest
                {
                    Subscription = new CancellationOptions { CancellationMessage = reason }
                },
                ct: ct);

            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }
            if (ex.Error.TryGetCancelSubscriptionErrorResponse(out var union))
            {
                if (union.TryGetErrorListResponse1(out var list))
                {
                    throw new BillingProviderException(DescribeErrorList(list), ex);
                }
                if (union.TryGetSingleErrorResponse1(out var single))
                {
                    throw new BillingProviderException(
                        single.Error ?? "The billing provider rejected the cancellation.", ex);
                }
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException(DescribeRaw(raw), ex);
            }
            throw new BillingProviderException("Failed to cancel subscription.", ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw new BillingProviderException("Could not reach the billing provider.", ex);
        }
    }

    public async Task<CustomerSubscription> ReactivateSubscriptionAsync(int subscriptionId,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ReactivateSubscription(
                subscriptionId: subscriptionId, body: new ReactivateSubscriptionRequest(), ct: ct);
            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var typed))
            {
                throw new BillingProviderException(DescribeErrorList(typed), ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException(DescribeRaw(raw), ex);
            }
            throw new BillingProviderException("Failed to reactivate subscription.", ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw new BillingProviderException("Could not reach the billing provider.", ex);
        }
    }

    private static bool IsConnectionFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested);

    private static string DescribeRaw(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            return string.IsNullOrWhiteSpace(body) ? $"Billing provider returned HTTP {(int)raw.StatusCode}." : body;
        }
        catch
        {
            return $"Billing provider returned HTTP {(int)raw.StatusCode}.";
        }
    }

    private static string DescribeErrorList(ErrorListResponse1 errors) =>
        errors.Errors is { Count: > 0 } list
            ? string.Join("; ", list)
            : "The billing provider rejected the request.";

    private static string DescribeCustomerErrors(CustomerErrorResponse1 typed)
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
        return messages.Count > 0 ? string.Join("; ", messages) : "The billing provider rejected the customer details.";
    }

    private static double ReadQuantity(Quantity1? quantity)
    {
        if (quantity == null)
        {
            return 0;
        }
        if (quantity.TryGetInt(out var intValue))
        {
            return intValue;
        }
        if (quantity.TryGetString(out var stringValue) &&
            double.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return 0;
    }

    private static SubscriptionPlan MapPlan(Product product) =>
        new(product.Handle ?? string.Empty,
            product.Name ?? string.Empty,
            product.PriceInCents ?? 0,
            product.Interval ?? 1,
            product.IntervalUnit?.Value ?? "month",
            product.RequireCreditCard ?? false);

    private static BillingCustomer MapCustomer(Customer customer) =>
        new(customer.Id ?? 0, customer.Reference ?? string.Empty, customer.Email ?? string.Empty);

    private static CustomerSubscription MapSubscription(Subscription subscription) =>
        new(subscription.Id ?? 0,
            subscription.Customer?.Reference ?? string.Empty,
            subscription.State?.Value ?? string.Empty,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            subscription.Product?.PriceInCents ?? 0,
            subscription.CurrentPeriodStartedAt,
            subscription.CurrentPeriodEndsAt,
            subscription.CancelAtEndOfPeriod ?? false,
            subscription.NextProductHandle,
            subscription.BalanceInCents ?? 0);
}
