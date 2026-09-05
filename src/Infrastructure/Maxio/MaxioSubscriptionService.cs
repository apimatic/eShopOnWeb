using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly MaxioSubscribeGate _gate;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        MaxioSettings settings,
        MaxioSubscribeGate gate,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _gate = gate;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        IReadOnlyList<ProductResponse> products;
        try
        {
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
                ct: cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var message))
            {
                throw Wrap($"Product family '{_settings.ProductFamilyHandle}' was not found in Maxio.", ex, message);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Wrap("Maxio rejected the subscription plan list request.", ex, DescribeRawError(raw));
            }
            throw Wrap("Maxio rejected the subscription plan list request.", ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Wrap("Could not reach Maxio to list subscription plans.", ex);
        }

        return products
            .Select(p => p.Product)
            .Select(p => new SubscriptionPlan(
                Handle: p.Handle ?? string.Empty,
                Name: p.Name ?? string.Empty,
                PriceInCents: p.PriceInCents,
                Interval: p.Interval,
                IntervalUnit: p.IntervalUnit?.Value))
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(MaxioCustomerProfile customer, string planHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var plan = await ReadPlanAsync(planHandle, cancellationToken).ConfigureAwait(false);

        using var lease = await _gate.AcquireAsync(customer.Reference, cancellationToken).ConfigureAwait(false);

        var maxioCustomer = await EnsureCustomerAsync(customer, cancellationToken).ConfigureAwait(false);
        var customerId = maxioCustomer.Id
            ?? throw Wrap($"Maxio returned customer '{customer.Reference}' with no id.", null);

        var existing = await ListCustomerSubscriptionsRawAsync(customerId, cancellationToken).ConfigureAwait(false);
        var existingLive = existing.FirstOrDefault(s => IsLiveSubscriptionForPlan(s, planHandle));
        if (existingLive != null)
        {
            return ToCustomerSubscription(existingLive);
        }

        SubscriptionResponse response;
        try
        {
            response = await _client.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerReference = customer.Reference,
                        // The seeded plans require no payment method, but Maxio still defaults new
                        // subscriptions to automatic card collection, which fails immediately for a
                        // no-trial plan with no payment profile on file. Invoice billing accepts the
                        // signup without a card and lets the balance be collected out of band.
                        PaymentCollectionMethod = CollectionMethod.Invoice
                    }
                },
                ct: cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw Wrap($"Maxio rejected the subscription for plan '{plan.Handle}'.", ex, string.Join("; ", errorList.Errors));
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Wrap($"Maxio rejected the subscription for plan '{plan.Handle}'.", ex, DescribeRawError(raw));
            }
            throw Wrap($"Maxio rejected the subscription for plan '{plan.Handle}'.", ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            // The write may already have reached Maxio despite the transport failure — reconcile
            // instead of assuming nothing happened (see dotnet-configuration-resilience).
            var reconciled = await ListCustomerSubscriptionsRawAsync(customerId, cancellationToken).ConfigureAwait(false);
            var match = reconciled.FirstOrDefault(s => IsLiveSubscriptionForPlan(s, planHandle));
            if (match != null)
            {
                return ToCustomerSubscription(match);
            }

            throw Wrap($"Could not reach Maxio to create the subscription for plan '{plan.Handle}'.", ex);
        }

        var subscription = response.Subscription
            ?? throw Wrap($"Maxio accepted the subscription for plan '{plan.Handle}' but returned no subscription details.", null);

        return ToCustomerSubscription(subscription);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var customer = await TryReadCustomerByReferenceAsync(customerReference, cancellationToken).ConfigureAwait(false);
        if (customer?.Id == null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsRawAsync(customer.Id.Value, cancellationToken).ConfigureAwait(false);
        return subscriptions.Select(ToCustomerSubscription).ToList();
    }

    private async Task<Product> ReadPlanAsync(string planHandle, CancellationToken ct)
    {
        ProductResponse response;
        try
        {
            response = await _client.Products.ReadProductByHandle(apiHandle: planHandle, ct: ct).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            // ReadProductByHandle has no typed not-found response; per the contract sheet, treat any
            // non-2xx here as "plan not found" rather than a generic provider failure.
            _logger.LogWarning(ex, "Maxio rejected the plan lookup for handle '{PlanHandle}': {Detail}", planHandle, DescribeRawError(ex.Error));
            throw new SubscriptionPlanNotFoundException(planHandle);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            throw Wrap($"Could not reach Maxio to look up plan '{planHandle}'.", ex);
        }

        var product = response.Product;
        if (product.RequireCreditCard == true)
        {
            throw Wrap($"Plan '{planHandle}' requires a payment method, which this integration does not collect.", null);
        }

        return product;
    }

    private async Task<Customer> EnsureCustomerAsync(MaxioCustomerProfile profile, CancellationToken ct)
    {
        var existing = await TryReadCustomerByReferenceAsync(profile.Reference, ct).ConfigureAwait(false);
        if (existing != null)
        {
            return existing;
        }

        try
        {
            var response = await _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = profile.FirstName,
                        LastName = profile.LastName,
                        Email = profile.Email,
                        Reference = profile.Reference
                    }
                },
                ct: ct).ConfigureAwait(false);

            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A concurrent double-click or a transport retry can race a duplicate 'reference' past us
            // here — Maxio enforces reference uniqueness server-side, so re-read before treating this
            // as a real failure.
            var reconciled = await TryReadCustomerByReferenceAsync(profile.Reference, ct).ConfigureAwait(false);
            if (reconciled != null)
            {
                return reconciled;
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out var typed))
            {
                throw Wrap($"Maxio rejected customer creation for '{profile.Reference}'.", ex, typed.ToString());
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Wrap($"Maxio rejected customer creation for '{profile.Reference}'.", ex, DescribeRawError(raw));
            }
            throw Wrap($"Maxio rejected customer creation for '{profile.Reference}'.", ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            var reconciled = await TryReadCustomerByReferenceAsync(profile.Reference, ct).ConfigureAwait(false);
            if (reconciled != null)
            {
                return reconciled;
            }

            throw Wrap($"Could not reach Maxio to create customer '{profile.Reference}'.", ex);
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: reference, ct: ct).ConfigureAwait(false);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Wrap($"Maxio rejected the customer lookup for '{reference}'.", ex, DescribeRawError(ex.Error));
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            throw Wrap($"Could not reach Maxio to look up customer '{reference}'.", ex);
        }
    }

    private async Task<List<Subscription>> ListCustomerSubscriptionsRawAsync(int customerId, CancellationToken ct)
    {
        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            throw Wrap($"Maxio rejected the subscription list request for customer {customerId}.", ex, DescribeRawError(ex.Error));
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            throw Wrap($"Could not reach Maxio to list subscriptions for customer {customerId}.", ex);
        }

        return responses
            .Select(r => r.Subscription)
            .Where(s => s != null)
            .Select(s => s!)
            .ToList();
    }

    private static bool IsLiveSubscriptionForPlan(Subscription subscription, string planHandle)
    {
        if (!string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var state = subscription.State;
        return state != SubscriptionState.Canceled
            && state != SubscriptionState.Expired
            && state != SubscriptionState.FailedToCreate;
    }

    private static CustomerSubscription ToCustomerSubscription(Subscription subscription)
    {
        return new CustomerSubscription(
            PlanHandle: subscription.Product?.Handle ?? string.Empty,
            PlanName: subscription.Product?.Name ?? string.Empty,
            PriceInCents: subscription.ProductPriceInCents,
            State: subscription.State?.Value ?? "unknown",
            NextBillingDate: subscription.NextAssessmentAt);
    }

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw new MaxioProviderException(
                "Maxio billing is not configured. Set 'Maxio:ApiKey', 'Maxio:Subdomain' and 'Maxio:ProductFamilyHandle' (for example via user-secrets or environment variables).");
        }
    }

    private static bool IsTransportFailure(Exception ex, CancellationToken callerToken)
    {
        if (ex is HttpRequestException)
        {
            return true;
        }

        // The SDK's own per-attempt timeout also throws TaskCanceledException; only treat it as a
        // provider-side failure when the caller did not request cancellation themselves, so a client
        // disconnect (HttpContext.RequestAborted) still propagates as a genuine cancellation.
        return ex is TaskCanceledException && !callerToken.IsCancellationRequested;
    }

    private static string DescribeRawError(RawError raw)
    {
        try
        {
            return $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}";
        }
        catch
        {
            return $"HTTP {(int)raw.StatusCode}: (unreadable response body)";
        }
    }

    private MaxioProviderException Wrap(string safeMessage, Exception? ex, string? diagnosticDetail = null)
    {
        if (diagnosticDetail != null)
        {
            _logger.LogWarning(ex, "{SafeMessage} Detail: {Detail}", safeMessage, diagnosticDetail);
        }
        else if (ex != null)
        {
            _logger.LogWarning(ex, "{SafeMessage}", safeMessage);
        }

        return ex == null ? new MaxioProviderException(safeMessage) : new MaxioProviderException(safeMessage, ex);
    }
}
