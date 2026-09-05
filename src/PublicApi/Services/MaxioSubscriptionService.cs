using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services;

public sealed class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, string productFamilyHandle)
    {
        _client = client;
        _productFamilyHandle = productFamilyHandle;
    }

    public async Task<IEnumerable<SubscriptionPlanDto>> ListSubscriptionPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 20,
                ct: ct);

            var plans = new List<SubscriptionPlanDto>();
            foreach (var productResponse in response)
            {
                var product = productResponse.Product;
                if (product?.ProductFamily?.Handle == _productFamilyHandle && product.Id.HasValue)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = (int)product.Id.Value,
                        Handle = product.Handle ?? string.Empty,
                        Name = product.Name ?? string.Empty,
                        Description = product.Description ?? string.Empty,
                        PriceInCents = product.PriceInCents ?? 0,
                        IntervalUnit = product.IntervalUnit?.Value ?? string.Empty
                    });
                }
            }

            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            throw new Exception($"Failed to list subscription plans: HTTP {ex.Error.StatusCode}", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string userId, string userEmail, string userFirstName, string userLastName,
        string productHandle, CancellationToken ct = default)
    {
        try
        {
            // Step 1: Try to look up or create customer
            var customerId = await LookupOrCreateCustomerAsync(userId, userEmail, userFirstName, userLastName, ct);

            // Step 2: Check if subscription already exists with this reference (idempotency)
            var subscriptionReference = $"{userId}:{productHandle}";
            try
            {
                var existing = await _client.Subscriptions.FindSubscription(subscriptionReference, ct: ct);
                if (existing?.Subscription != null)
                {
                    return MapSubscriptionToDto(existing.Subscription);
                }
            }
            catch (SdkException<FindSubscriptionError> ex)
            {
                // 404 means subscription not found, which is expected
                if (ex.Error.TryGetNoContent(out _))
                {
                    // Not found, proceed to create
                }
                else
                {
                    throw;
                }
            }

            // Step 3: Create the subscription
            var createRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle,
                    Reference = subscriptionReference
                }
            };

            var subscriptionResponse = await _client.Subscriptions.CreateSubscription(createRequest, ct: ct);
            return MapSubscriptionToDto(subscriptionResponse.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                var messages = string.Join(", ", errors.Errors ?? new List<string>());
                throw new Exception($"Failed to create subscription: {messages}", ex);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw new Exception($"Failed to create subscription: HTTP {raw.StatusCode}", ex);
            }
            throw;
        }
    }

    public async Task<IEnumerable<SubscriptionDto>> ListCustomerSubscriptionsAsync(
        string userId, CancellationToken ct = default)
    {
        try
        {
            var customerId = await LookupOrCreateCustomerAsync(userId, string.Empty, string.Empty, string.Empty, ct);
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);

            return subscriptions
                .Where(s => s.Subscription != null)
                .Select(s => MapSubscriptionToDto(s.Subscription))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new Exception($"Failed to list customer subscriptions: HTTP {ex.Error.StatusCode}", ex);
        }
    }

    private async Task<int> LookupOrCreateCustomerAsync(
        string reference, string email, string firstName, string lastName, CancellationToken ct)
    {
        try
        {
            // Try to look up existing customer
            var existing = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            if (existing?.Customer?.Id.HasValue == true)
            {
                return (int)existing.Customer.Id.Value;
            }
        }
        catch (SdkException<RawError> ex)
        {
            // 404 means customer not found, which is expected for new users
            if (ex.Error.StatusCode != HttpStatusCode.NotFound)
            {
                throw;
            }
        }

        // Create new customer if not found and we have the required info
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            // If called without customer details (e.g. during list), generate minimal ones
            var request = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = string.IsNullOrWhiteSpace(firstName) ? "Customer" : firstName,
                    LastName = string.IsNullOrWhiteSpace(lastName) ? reference : lastName,
                    Email = string.IsNullOrWhiteSpace(email) ? $"{reference}@eshop.local" : email,
                    Reference = reference
                }
            };

            var response = await _client.Customers.CreateCustomer(request, ct: ct);
            if (response.Customer.Id.HasValue)
            {
                return (int)response.Customer.Id.Value;
            }
            throw new Exception("Failed to create customer: no ID returned");
        }

        var createReq = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        try
        {
            var resp = await _client.Customers.CreateCustomer(createReq, ct: ct);
            if (resp.Customer.Id.HasValue)
            {
                return (int)resp.Customer.Id.Value;
            }
            throw new Exception("Failed to create customer: no ID returned");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var errResponse))
            {
                // Handle 422 validation error - likely duplicate reference
                // Try lookup again - race condition where customer was created by another request
                try
                {
                    var existing = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
                    if (existing?.Customer?.Id.HasValue == true)
                    {
                        return (int)existing.Customer.Id.Value;
                    }
                }
                catch
                {
                    // Lookup also failed, so re-throw the original error
                }
                throw new Exception("Failed to create customer: validation error (possibly duplicate reference)", ex);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw new Exception($"Failed to create customer: HTTP {raw.StatusCode}", ex);
            }
            throw;
        }
    }

    private static SubscriptionDto MapSubscriptionToDto(Subscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id.HasValue ? (int)subscription.Id.Value : 0,
            State = subscription.State?.Value ?? string.Empty,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ProductPriceInCents = subscription.ProductPriceInCents ?? 0
        };
    }
}

public sealed record SubscriptionPlanDto
{
    public int Id { get; set; }
    public required string Handle { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public long PriceInCents { get; set; }
    public required string IntervalUnit { get; set; }
}

public sealed record SubscriptionDto
{
    public int Id { get; set; }
    public required string State { get; set; }
    public required string ProductHandle { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public long ProductPriceInCents { get; set; }
}
