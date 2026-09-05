using System;
using System.Collections.Generic;
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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioSubscriptionService : ISubscriptionService
{
    // Subscription states that mean "not a live enrollment" for duplicate-prevention purposes.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _productFamilyHandle = options.Value.ProductFamilyHandle;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken ct = default)
    {
        try
        {
            var familyId = await ResolveProductFamilyIdAsync(ct);

            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 20,
                ct: ct);

            return products
                .Select(p => p.Product)
                .Where(p => p is not null && !string.IsNullOrEmpty(p.Handle))
                .Select(p => MapPlan(p!))
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var message))
            {
                throw new SubscriptionProviderException($"Maxio rejected the plan list request: {message}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new SubscriptionProviderException($"Maxio plan list request failed with status {(int)raw.StatusCode}.", ex);
            }
            throw new SubscriptionProviderException("Maxio plan list request failed.", ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw new SubscriptionProviderException($"Maxio product family lookup failed with status {(int)ex.Error.StatusCode}.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SubscriptionProviderException("Maxio was unreachable.", ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionProviderException("Maxio returned a response that could not be processed.", ex);
        }
    }

    public async Task<SubscriptionDetails> SubscribeAsync(SubscriptionEnrollmentRequest request, CancellationToken ct = default)
    {
        var customer = await FindOrCreateCustomerAsync(request.CustomerReference, request.Email, request.FirstName, request.LastName, ct);
        if (customer.Id is null)
        {
            throw new SubscriptionProviderException("Maxio returned a customer with no id.");
        }

        var existingLive = await FindLiveSubscriptionAsync(customer.Id.Value, request.PlanHandle, ct);
        if (existingLive is not null)
        {
            _logger.LogInformation(
                "Customer {CustomerReference} already has a live subscription {SubscriptionId} to plan {PlanHandle}; returning it instead of creating a duplicate.",
                request.CustomerReference, existingLive.Id, request.PlanHandle);
            return MapSubscription(existingLive);
        }

        var subscriptionReference = $"eshop:{request.CustomerReference}:{request.PlanHandle}";

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = request.PlanHandle,
                        CustomerId = customer.Id,
                        Reference = subscriptionReference,
                        // Both seeded plans have no payment method on file (RequireCreditCard = false),
                        // but Maxio still attempts an immediate charge for a priced product unless billing
                        // is explicitly deferred - NextBillingAt is the documented way to make subscription
                        // creation charge-free (see maxio-plan.md §5).
                        NextBillingAt = ComputeNextBillingAt(request.PlanInterval, request.PlanIntervalUnit)
                    }
                },
                ct: ct);

            var created = response.Subscription;
            if (created is null)
            {
                throw new SubscriptionProviderException("Maxio returned no subscription after creation.");
            }
            return MapSubscription(created);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // A concurrent duplicate POST may have created the subscription first - recheck before
            // treating this as a hard failure (see maxio-plan.md §5, subscription dedupe is not
            // server-guaranteed, only application-checked).
            var recovered = await FindLiveSubscriptionAsync(customer.Id.Value, request.PlanHandle, ct);
            if (recovered is not null)
            {
                _logger.LogWarning(
                    "CreateSubscription for customer {CustomerReference} / plan {PlanHandle} failed but a live subscription {SubscriptionId} now exists - treating as a resolved race, not an error.",
                    request.CustomerReference, request.PlanHandle, recovered.Id);
                return MapSubscription(recovered);
            }

            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                var detail = errors.Errors is null ? string.Empty : string.Join("; ", errors.Errors);
                _logger.LogError(ex, "Maxio rejected subscription creation for customer {CustomerReference} / plan {PlanHandle}: {Detail}", request.CustomerReference, request.PlanHandle, detail);
                throw new SubscriptionProviderException($"Maxio rejected the subscription request: {detail}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new SubscriptionProviderException($"Maxio subscription creation failed with status {(int)raw.StatusCode}.", ex);
            }
            throw new SubscriptionProviderException("Maxio subscription creation failed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SubscriptionProviderException("Maxio was unreachable.", ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionProviderException("Maxio returned a response that could not be processed.", ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken ct = default)
    {
        var customer = await TryReadCustomerByReferenceAsync(customerReference, ct);
        if (customer?.Id is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await ListSubscriptionsAsync(customer.Id.Value, ct);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<string> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        var families = await _client.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: ct);

        var family = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null && string.Equals(f.Handle, _productFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family?.Id is null)
        {
            throw new SubscriptionProviderException($"Maxio product family '{_productFamilyHandle}' was not found on this site.");
        }

        return family.Id.Value.ToString();
    }

    private async Task<Customer> FindOrCreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken ct)
    {
        var existing = await TryReadCustomerByReferenceAsync(reference, ct);
        if (existing is not null)
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
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = reference
                    }
                },
                ct: ct);

            var customer = response.Customer;
            if (customer is null)
            {
                throw new SubscriptionProviderException("Maxio returned no customer after creation.");
            }
            return customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // The typed 422 body doesn't expose the conflicting field (see maxio-plan.md §2.2 row 4) -
                // a concurrent request may have created the customer first, so re-fetch by reference
                // rather than trying to parse the error for a "reference taken" signal.
                var recovered = await TryReadCustomerByReferenceAsync(reference, ct);
                if (recovered is not null)
                {
                    return recovered;
                }
                throw new SubscriptionProviderException("Maxio rejected customer creation and no existing customer could be found for this user.", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new SubscriptionProviderException($"Maxio customer creation failed with status {(int)raw.StatusCode}.", ex);
            }
            throw new SubscriptionProviderException("Maxio customer creation failed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SubscriptionProviderException("Maxio was unreachable.", ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionProviderException("Maxio returned a response that could not be processed.", ex);
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            throw new SubscriptionProviderException($"Maxio customer lookup failed with status {(int)ex.Error.StatusCode}.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SubscriptionProviderException("Maxio was unreachable.", ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionProviderException("Maxio returned a response that could not be processed.", ex);
        }
    }

    private async Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct);
            return response
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new SubscriptionProviderException($"Maxio subscription list request failed with status {(int)ex.Error.StatusCode}.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SubscriptionProviderException("Maxio was unreachable.", ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionProviderException("Maxio returned a response that could not be processed.", ex);
        }
    }

    private async Task<Subscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        var subscriptions = await ListSubscriptionsAsync(customerId, ct);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            !TerminalStates.Contains(s.State?.Value ?? string.Empty));
    }

    private static DateTimeOffset ComputeNextBillingAt(int interval, string intervalUnit) =>
        string.Equals(intervalUnit, "day", StringComparison.OrdinalIgnoreCase)
            ? DateTimeOffset.UtcNow.AddDays(interval)
            : DateTimeOffset.UtcNow.AddMonths(interval);

    private static SubscriptionPlan MapPlan(Product p) => new(
        Handle: p.Handle!,
        Name: p.Name ?? p.Handle!,
        PriceAmount: (p.PriceInCents ?? 0) / 100m,
        Interval: p.Interval ?? 1,
        IntervalUnit: p.IntervalUnit?.Value ?? "month",
        RequiresPaymentMethod: p.RequireCreditCard ?? false);

    private static SubscriptionDetails MapSubscription(Subscription s) => new(
        Id: s.Id ?? 0,
        Reference: s.Reference,
        PlanHandle: s.Product?.Handle ?? string.Empty,
        PlanName: s.Product?.Name ?? string.Empty,
        PriceAmount: (s.Product?.PriceInCents ?? 0) / 100m,
        Interval: s.Product?.Interval ?? 0,
        IntervalUnit: s.Product?.IntervalUnit?.Value ?? string.Empty,
        State: s.State?.Value ?? "unknown",
        NextBillingDate: s.NextAssessmentAt);
}
