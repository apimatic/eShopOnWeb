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
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing via the
/// AsadAli.AdvancedBilling.Sdk client. All interaction with the billing SDK is confined here;
/// every SDK failure is translated to a <see cref="BillingException"/> so callers never see
/// SDK types leak across the boundary.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    // Subscription states in which an existing subscription is NOT reusable — a new subscribe
    // should create a fresh one rather than returning these. Anything else (active, trialing,
    // past_due, on_hold, …) is treated as live for the idempotent double-submit guard.
    private static readonly HashSet<string> NonReusableStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        IReadOnlyList<ProductResponse> products;
        try
        {
            // Positional args: (productFamilyId, dateField, filter, startDate, endDate,
            //                   startDatetime, endDatetime, includeArchived, include, page, perPage, ct)
            products = await _client.ProductFamilies.ListProductsForProductFamily(
                familyId.ToString(CultureInfo.InvariantCulture),
                null, null, null, null, null, null, false, null, 1, 200, cancellationToken);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
            {
                throw BillingException.Upstream(
                    $"Maxio could not list products for family '{_settings.ProductFamilyHandle}': {notFound}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw BillingException.Upstream(
                    $"Maxio returned HTTP {(int)raw.StatusCode} listing plans.", ex);
            }

            throw BillingException.Upstream("Maxio returned an error listing plans.", ex);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw Unavailable("listing plans", ex);
        }

        return products
            .Where(p => p.Product is not null)
            .Select(p => MapPlan(p.Product!))
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw BillingException.Validation("A subscribe request is required.");
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            throw BillingException.Validation("A plan handle is required to subscribe.");
        }

        if (string.IsNullOrWhiteSpace(request.CustomerReference))
        {
            throw BillingException.Validation("A customer reference (the caller's identity) is required to subscribe.");
        }

        // 1. Ensure the billing customer exists (idempotent on the reference).
        var customer = await EnsureCustomerAsync(request, cancellationToken);
        var customerId = customer.Id
            ?? throw BillingException.Upstream("Maxio returned a customer without an id.");

        // 2. Idempotency guard: if this customer already has a live subscription to the plan,
        //    return it instead of creating a duplicate (covers double-clicks / retries).
        var existing = await ListSubscriptionsAsync(customerId, cancellationToken);
        var already = existing.FirstOrDefault(s =>
            string.Equals(s.PlanHandle, request.PlanHandle, StringComparison.OrdinalIgnoreCase)
            && IsReusable(s.State));
        if (already is not null)
        {
            _logger.LogInformation(
                $"Reusing existing subscription {already.Id} for customer reference '{request.CustomerReference}' on plan '{request.PlanHandle}'.");
            return already;
        }

        // 3. Create the subscription against the existing customer id (no payment method required).
        SubscriptionResponse response;
        try
        {
            response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        CustomerId = customerId,
                        ProductHandle = request.PlanHandle,
                        // Invoice/remittance collection: these plans require no payment method, so the
                        // balance is invoiced rather than auto-charged (automatic collection would fail
                        // with "no payment method on file" for the plan's up-front balance).
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                },
                cancellationToken);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var detail = errorList?.Errors is { Count: > 0 }
                    ? string.Join("; ", errorList.Errors)
                    : "the subscription request was rejected";
                throw BillingException.Validation($"Maxio rejected the subscription: {detail}");
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw BillingException.Upstream(
                    $"Maxio returned HTTP {(int)raw.StatusCode} creating the subscription.", ex);
            }

            throw BillingException.Upstream("Maxio failed to create the subscription.", ex);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw Unavailable("creating the subscription", ex);
        }

        if (response.Subscription is null)
        {
            throw BillingException.Upstream("Maxio returned an empty subscription after create.");
        }

        _logger.LogInformation(
            $"Created subscription {response.Subscription.Id} for customer reference '{request.CustomerReference}' on plan '{request.PlanHandle}'.");

        return MapSubscription(response.Subscription, request.CustomerReference);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            throw BillingException.Validation("A customer reference is required.");
        }

        var customer = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer?.Id is null)
        {
            // No billing customer yet — the user simply has no subscriptions.
            return Array.Empty<CustomerSubscription>();
        }

        return await ListSubscriptionsAsync(customer.Id.Value, cancellationToken);
    }

    // --- Internals --------------------------------------------------------------------------

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        var handle = _settings.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw BillingException.Upstream("Maxio product family handle is not configured (Maxio:ProductFamilyHandle).");
        }

        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            // Positional args: (dateField, startDate, endDate, startDatetime, endDatetime, ct)
            families = await _client.ProductFamilies.ListProductFamilies(null, null, null, null, null, cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw BillingException.Upstream(
                $"Maxio returned HTTP {(int)ex.Error.StatusCode} listing product families.", ex);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw Unavailable("resolving the product family", ex);
        }

        var family = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null && string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (family?.Id is null)
        {
            throw BillingException.Upstream($"No Maxio product family found with handle '{handle}'.");
        }

        return family.Id.Value;
    }

    private async Task<Customer> EnsureCustomerAsync(SubscribeRequest request, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(request.CustomerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? "eShop" : request.FirstName!,
                        LastName = string.IsNullOrWhiteSpace(request.LastName) ? "Customer" : request.LastName!,
                        Email = request.Email,
                        Reference = request.CustomerReference
                    }
                },
                cancellationToken);

            if (created.Customer is null)
            {
                throw BillingException.Upstream("Maxio returned an empty customer after create.");
            }

            _logger.LogInformation($"Created Maxio customer {created.Customer.Id} for reference '{request.CustomerReference}'.");
            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A 422 here is almost always a unique-reference collision from a concurrent double-submit:
            // the reference was taken between our lookup and this create. Re-resolve it idempotently.
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var afterRace = await FindCustomerByReferenceAsync(request.CustomerReference, cancellationToken);
                if (afterRace is not null)
                {
                    return afterRace;
                }

                // The reference is free but the create still failed validation (e.g. duplicate email
                // on a different reference). Surface as a caller error.
                throw BillingException.Validation(
                    "Maxio rejected the customer details. The email may already be associated with a different account.");
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw BillingException.Upstream(
                    $"Maxio returned HTTP {(int)raw.StatusCode} creating the customer.", ex);
            }

            throw BillingException.Upstream("Maxio failed to create the customer.", ex);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw Unavailable("creating the customer", ex);
        }
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, cancellationToken);
            // A 2xx with an absent customer is treated as "not found" (defensive; a miss normally 404s).
            return response?.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw BillingException.Upstream(
                $"Maxio returned HTTP {(int)ex.Error.StatusCode} looking up the customer.", ex);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw Unavailable("looking up the customer", ex);
        }
    }

    private async Task<List<CustomerSubscription>> ListSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        IReadOnlyList<SubscriptionResponse> list;
        try
        {
            list = await _client.Customers.ListCustomerSubscriptions(customerId, cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw BillingException.Upstream(
                $"Maxio returned HTTP {(int)ex.Error.StatusCode} listing subscriptions.", ex);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw Unavailable("listing subscriptions", ex);
        }

        return list
            .Where(r => r.Subscription is not null)
            .Select(r => MapSubscription(r.Subscription!))
            .ToList();
    }

    private SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        ProductId = product.Id,
        Name = product.Name ?? string.Empty,
        PriceInCents = product.PriceInCents ?? 0,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value,
        // Currency is intentionally null: Maxio does not expose currency on the product/plan model.
        Currency = null,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? _settings.ProductFamilyHandle
    };

    private static CustomerSubscription MapSubscription(Subscription subscription, string? customerReference = null) => new()
    {
        Id = subscription.Id ?? 0,
        State = subscription.State?.Value,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
        Currency = subscription.Currency,
        // Maxio does not return a dedicated next_billing_at; the current period end is the next bill date.
        NextBillingAt = subscription.CurrentPeriodEndsAt,
        CustomerReference = subscription.Customer?.Reference ?? customerReference
    };

    private static bool IsReusable(string? state) => !NonReusableStates.Contains(state ?? string.Empty);

    private static bool IsTransportOrParseFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException;

    private BillingException Unavailable(string action, Exception inner)
    {
        _logger.LogWarning($"Maxio billing provider unavailable while {action}: {inner.Message}");
        return BillingException.Upstream($"The billing provider is currently unavailable while {action}.", inner);
    }
}
