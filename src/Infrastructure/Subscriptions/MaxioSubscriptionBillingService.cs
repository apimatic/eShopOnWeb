using System;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;
    private readonly SemaphoreSlim _familyLookupLock = new(1, 1);
    private volatile string? _cachedFamilyId;

    public MaxioSubscriptionBillingService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> options)
    {
        _client = client;
        _productFamilyHandle = options.Value.ProductFamilyHandle;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await ListProductsAsync(cancellationToken);
        return products.Select(MapPlan).ToList();
    }

    private async Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken ct)
    {
        var familyId = await ResolveProductFamilyIdAsync(ct);

        IReadOnlyList<ProductResponse> products;
        try
        {
            products = await _client.ProductFamilies.ListProductsForProductFamily(
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
                perPage: 200,
                ct: ct);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFoundMessage))
            {
                throw new MaxioException(
                    $"Configured product family '{_productFamilyHandle}' was not found in the billing provider: {notFoundMessage}",
                    (int)HttpStatusCode.BadGateway, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, ex);
            }
            throw new MaxioException("Unexpected billing provider error while listing plans.", (int)HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioException("Unable to reach the billing provider.", (int)HttpStatusCode.BadGateway, ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new MaxioException("The billing provider returned a response that could not be processed.", (int)HttpStatusCode.BadGateway, ex);
        }

        return products.Select(p => p.Product).ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        string customerReference,
        string customerEmail,
        string customerFirstName,
        string customerLastName,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        var customer = await FindOrCreateCustomerAsync(customerReference, customerEmail, customerFirstName, customerLastName, cancellationToken);

        // Non-atomic double-click/retry guard: the SDK exposes no idempotency key on subscription
        // creation, so this narrows the race but cannot close it (see maxio-plan.md Blockers).
        var existingSubscriptions = await ListCustomerSubscriptionsInternalAsync(customer.Id!.Value, cancellationToken);
        var existingActive = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            (s.State == SubscriptionState.Active || s.State == SubscriptionState.Trialing));
        if (existingActive != null)
        {
            return MapSubscription(existingActive);
        }

        // These plans require no payment method up front (see task catalog config), but Maxio still
        // attempts to capture the first charge immediately unless told otherwise. Per the SDK's own
        // documented field intent, a future NextBillingAt defers that first charge and captures no
        // payment at creation time at all — so no card is required to enroll. Derive the deferral
        // from the plan's own billing interval so this isn't hard-coded to a monthly cadence.
        var product = await FindProductByHandleAsync(planHandle, cancellationToken);
        var nextBillingAt = ComputeNextBillingAt(product);

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerReference = customerReference,
                        NextBillingAt = nextBillingAt
                    }
                }, ct: cancellationToken);

            var subscription = response.Subscription;
            if (subscription == null)
            {
                throw new MaxioException("Billing provider did not return the created subscription.", (int)HttpStatusCode.BadGateway);
            }
            return MapSubscription(subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var message = errorList.Errors.Count > 0 ? string.Join("; ", errorList.Errors) : "Validation failed.";
                throw new MaxioException($"Unable to create subscription: {message}", (int)HttpStatusCode.UnprocessableEntity, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, ex);
            }
            throw new MaxioException("Unexpected billing provider error while creating subscription.", (int)HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioException("Unable to reach the billing provider.", (int)HttpStatusCode.BadGateway, ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new MaxioException("The billing provider returned a response that could not be processed.", (int)HttpStatusCode.BadGateway, ex);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken = default)
    {
        var customer = await TryReadCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer?.Id == null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsInternalAsync(customer.Id.Value, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<Customer> FindOrCreateCustomerAsync(
        string reference, string email, string firstName, string lastName, CancellationToken ct)
    {
        var existing = await TryReadCustomerByReferenceAsync(reference, ct);
        if (existing != null)
        {
            return existing;
        }

        try
        {
            var response = await _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = reference
                    }
                }, ct: ct);

            var created = response.Customer;
            if (created == null)
            {
                throw new MaxioException("Billing provider did not return the created customer.", (int)HttpStatusCode.BadGateway);
            }
            return created;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // The 422 payload shape for this error is unreliable (see maxio-plan.md trap notes) —
                // re-check instead of trying to parse a reason from it. If a concurrent request won
                // the race and created the same reference first, use that customer.
                var raced = await TryReadCustomerByReferenceAsync(reference, ct);
                if (raced != null)
                {
                    return raced;
                }
                throw new MaxioException($"Unable to create a billing customer for reference '{reference}'.", (int)HttpStatusCode.Conflict, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, ex);
            }
            throw new MaxioException("Unexpected billing provider error while creating customer.", (int)HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioException("Unable to reach the billing provider.", (int)HttpStatusCode.BadGateway, ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new MaxioException("The billing provider returned a response that could not be processed.", (int)HttpStatusCode.BadGateway, ex);
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            throw TranslateRawError(ex.Error, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioException("Unable to reach the billing provider.", (int)HttpStatusCode.BadGateway, ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new MaxioException("The billing provider returned a response that could not be processed.", (int)HttpStatusCode.BadGateway, ex);
        }
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsInternalAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
            return response.Where(r => r.Subscription != null).Select(r => r.Subscription!).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioException("Unable to reach the billing provider.", (int)HttpStatusCode.BadGateway, ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new MaxioException("The billing provider returned a response that could not be processed.", (int)HttpStatusCode.BadGateway, ex);
        }
    }

    private async Task<string> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        if (_cachedFamilyId != null)
        {
            return _cachedFamilyId;
        }

        await _familyLookupLock.WaitAsync(ct);
        try
        {
            if (_cachedFamilyId != null)
            {
                return _cachedFamilyId;
            }

            IReadOnlyList<ProductFamilyResponse> families;
            try
            {
                families = await _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw TranslateRawError(ex.Error, ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new MaxioException("Unable to reach the billing provider.", (int)HttpStatusCode.BadGateway, ex);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new MaxioException("The billing provider returned a response that could not be processed.", (int)HttpStatusCode.BadGateway, ex);
            }

            var family = families
                .Select(f => f.ProductFamily)
                .FirstOrDefault(f => f != null && string.Equals(f.Handle, _productFamilyHandle, StringComparison.OrdinalIgnoreCase));

            if (family?.Id == null)
            {
                throw new MaxioException(
                    $"Configured product family '{_productFamilyHandle}' was not found in the billing provider.",
                    (int)HttpStatusCode.BadGateway);
            }

            _cachedFamilyId = family.Id.Value.ToString(CultureInfo.InvariantCulture);
            return _cachedFamilyId;
        }
        finally
        {
            _familyLookupLock.Release();
        }
    }

    private async Task<Product?> FindProductByHandleAsync(string handle, CancellationToken ct)
    {
        var products = await ListProductsAsync(ct);
        return products.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));
    }

    private static DateTimeOffset ComputeNextBillingAt(Product? product)
    {
        var intervalCount = product?.Interval is > 0 ? product.Interval.Value : 1;
        var now = DateTimeOffset.UtcNow;
        return product?.IntervalUnit == IntervalUnit.Day
            ? now.AddDays(intervalCount)
            : now.AddMonths(intervalCount);
    }

    private static SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Price = (product.PriceInCents ?? 0) / 100m,
        IntervalCount = product.Interval ?? 1,
        IntervalUnit = product.IntervalUnit?.Value ?? string.Empty
    };

    private static CustomerSubscription MapSubscription(Subscription subscription) => new()
    {
        SubscriptionId = subscription.Id ?? 0,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        State = subscription.State?.Value ?? string.Empty,
        Price = (subscription.CurrentBillingAmountInCents ?? 0) / 100m,
        Currency = subscription.Currency,
        NextBillingDate = subscription.NextAssessmentAt
    };

    private static MaxioException TranslateRawError(RawError raw, Exception inner)
    {
        var statusCode = (int)raw.StatusCode;
        // Surface the provider's 4xx as-is (the caller can act on it); anything else is a gateway failure.
        var mappedStatus = statusCode is >= 400 and < 500 ? statusCode : (int)HttpStatusCode.BadGateway;
        return new MaxioException($"Billing provider returned HTTP {statusCode}: {raw.ReadAsString()}", mappedStatus, inner);
    }
}
