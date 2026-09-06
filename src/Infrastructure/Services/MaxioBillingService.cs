using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Api;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class MaxioBillingService : IMaxioBillingService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;

    public MaxioBillingService(MaxioAdvancedBillingClient client, MaxioConfiguration config)
    {
        _client = client;
        _productFamilyHandle = config.ProductFamilyHandle;
    }

    public async Task<SubscriptionPlanDto[]> ListSubscriptionPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: $"handle:{_productFamilyHandle}",
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: ct);

            var plans = new List<SubscriptionPlanDto>();
            foreach (var productResponse in response)
            {
                if (productResponse.Product != null)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = productResponse.Product.Id ?? 0,
                        Handle = productResponse.Product.Handle ?? string.Empty,
                        Name = productResponse.Product.Name ?? string.Empty,
                        PriceInCents = (decimal)(productResponse.Product.PriceInCents ?? 0),
                        TrialDays = productResponse.Product.TrialInterval
                    });
                }
            }

            return plans.ToArray();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var errorMsg))
            {
                throw new InvalidOperationException($"Failed to list products: {errorMsg}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new InvalidOperationException($"Failed to list products: HTTP {(int)raw.StatusCode}", ex);
            }

            throw;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to parse products response", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string userId,
        string firstName,
        string lastName,
        string email,
        string productHandle,
        CancellationToken ct = default)
    {
        // Ensure customer exists (idempotent via reference)
        int customerId;
        try
        {
            var customerResponse = await _client.Customers.ReadCustomerByReference(
                reference: userId,
                ct: ct);
            customerId = customerResponse.Customer?.Id ?? throw new InvalidOperationException("Customer ID not found");
        }
        catch (SdkException<RawError> ex)
        {
            // Customer not found, create one
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                var newCustomerResponse = await CreateCustomerAsync(userId, firstName, lastName, email, ct);
                customerId = newCustomerResponse.Customer?.Id ?? throw new InvalidOperationException("Failed to get new customer ID");
            }
            else
            {
                throw new InvalidOperationException($"Failed to look up customer: HTTP {(int)ex.Error.StatusCode}", ex);
            }
        }

        // Create subscription
        try
        {
            var subscriptionRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    Reference = userId
                }
            };

            var subscriptionResponse = await _client.Subscriptions.CreateSubscription(
                body: subscriptionRequest,
                ct: ct);

            if (subscriptionResponse.Subscription == null)
            {
                throw new InvalidOperationException("Subscription creation returned null subscription");
            }

            return MapToSubscriptionDto(subscriptionResponse.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors422))
            {
                var errorMsg = string.Join("; ", errors422.Errors ?? Array.Empty<string>());
                throw new InvalidOperationException($"Failed to create subscription: {errorMsg}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new InvalidOperationException($"Failed to create subscription: HTTP {(int)raw.StatusCode}", ex);
            }

            throw;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to parse subscription response", ex);
        }
    }

    public async Task<SubscriptionDto[]> GetUserSubscriptionsAsync(
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            // Look up customer by reference
            var customerResponse = await _client.Customers.ReadCustomerByReference(
                reference: userId,
                ct: ct);

            var customerId = customerResponse.Customer?.Id ?? throw new InvalidOperationException("Customer ID not found");

            // Get subscriptions for customer
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(
                customerId: customerId,
                ct: ct);

            var result = new List<SubscriptionDto>();
            foreach (var subResponse in subscriptions)
            {
                if (subResponse.Subscription != null)
                {
                    result.Add(MapToSubscriptionDto(subResponse.Subscription));
                }
            }

            return result.ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                // Customer not found, return empty list
                return Array.Empty<SubscriptionDto>();
            }

            throw new InvalidOperationException($"Failed to get subscriptions: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to parse subscriptions response", ex);
        }
    }

    private async Task<CustomerResponse> CreateCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email,
        CancellationToken ct)
    {
        try
        {
            var createCustomerRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = reference
                }
            };

            var response = await _client.Customers.CreateCustomer(
                body: createCustomerRequest,
                ct: ct);

            return response;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var errors422))
            {
                throw new InvalidOperationException($"Failed to create customer: validation error", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new InvalidOperationException($"Failed to create customer: HTTP {(int)raw.StatusCode}", ex);
            }

            throw;
        }
    }

    private static SubscriptionDto MapToSubscriptionDto(Subscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id ?? 0,
            CustomerId = subscription.Customer?.Id ?? 0,
            ProductId = subscription.Product?.Id ?? 0,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            State = subscription.State?.Value ?? "unknown",
            NextBillingAt = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            Reference = subscription.Reference
        };
    }
}
