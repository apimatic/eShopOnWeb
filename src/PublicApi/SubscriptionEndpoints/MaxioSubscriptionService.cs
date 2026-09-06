using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client)
    {
        _client = client;
    }

    public async Task<List<ProductDto>> GetSubscriptionPlansAsync(CancellationToken ct)
    {
        try
        {
            var products = await _client.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: ct);

            return products
                .Select(pr => new ProductDto
                {
                    Id = pr.Product?.Id ?? 0,
                    Name = pr.Product?.Name ?? string.Empty,
                    Handle = pr.Product?.Handle ?? string.Empty,
                    Description = pr.Product?.Description ?? string.Empty,
                    PriceInCents = pr.Product?.PriceInCents ?? 0,
                    Interval = pr.Product?.Interval ?? 0,
                    IntervalUnit = pr.Product?.IntervalUnit ?? string.Empty,
                })
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to fetch subscription plans: {ex.Error.ReadAsString()}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to parse subscription plans response", ex);
        }
    }

    public async Task<(bool Created, SubscriptionDto Subscription)> CreateOrGetSubscriptionAsync(
        string userReference,
        int productId,
        string? productHandle,
        CancellationToken ct)
    {
        try
        {
            // First, try to get or create a customer with the user reference
            Customer? customer = null;
            try
            {
                var customerResponse = await _client.Customers.ReadCustomerByReference(userReference, ct: ct);
                customer = customerResponse.Customer;
            }
            catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
            {
                // Customer doesn't exist, we'll create one via subscription creation
                customer = null;
            }

            // Try to create subscription
            var subscriptionRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductId = productId > 0 ? productId : (int?)null,
                    ProductHandle = !string.IsNullOrEmpty(productHandle) ? productHandle : null,
                    CustomerId = customer?.Id,
                    CustomerReference = userReference,
                }
            };

            try
            {
                var response = await _client.Subscriptions.CreateSubscription(subscriptionRequest, ct: ct);
                return (true, MapToSubscriptionDto(response.Subscription));
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                // Check if it's a duplicate (customer + subscription reference already exists)
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    var isDuplicate = errorList.Errors?.Any(e =>
                        e.Contains("customer") && (e.Contains("reference") || e.Contains("unique"))) ?? false;

                    if (isDuplicate && customer?.Id.HasValue == true)
                    {
                        // Subscription already exists for this user, fetch it
                        var subscriptions = await _client.Customers.ListCustomerSubscriptions(
                            customerId: customer.Id.Value,
                            ct: ct);

                        var existing = subscriptions.FirstOrDefault(sr => sr.Subscription != null);

                        if (existing?.Subscription != null)
                        {
                            return (false, MapToSubscriptionDto(existing.Subscription));
                        }
                    }

                    throw new InvalidOperationException(
                        $"Failed to create subscription: {string.Join(", ", errorList.Errors ?? new List<string>())}", ex);
                }

                if (ex.Error.TryGetRawError(out var rawError))
                {
                    throw new InvalidOperationException(
                        $"Failed to create subscription: {rawError.ReadAsString()}", ex);
                }

                throw;
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException(
                $"Failed to create subscription: {ex.Error.ReadAsString()}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to parse subscription response", ex);
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userReference, CancellationToken ct)
    {
        try
        {
            // Get customer by reference
            var customerResponse = await _client.Customers.ReadCustomerByReference(userReference, ct: ct);
            var customer = customerResponse.Customer;

            if (customer == null || !customer.Id.HasValue)
            {
                return new List<SubscriptionDto>();
            }

            // Get subscriptions for this customer
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(
                customerId: customer.Id.Value,
                ct: ct);

            return subscriptions
                .Where(sr => sr.Subscription != null)
                .Select(sr => MapToSubscriptionDto(sr.Subscription!))
                .ToList();
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            // Customer doesn't exist
            return new List<SubscriptionDto>();
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException(
                $"Failed to fetch subscriptions: {ex.Error.ReadAsString()}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to parse subscriptions response", ex);
        }
    }

    private static SubscriptionDto MapToSubscriptionDto(Subscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id ?? 0,
            State = subscription.State?.ToString() ?? "Unknown",
            BalanceInCents = subscription.BalanceInCents ?? 0,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ProductPriceInCents = subscription.ProductPriceInCents ?? 0,
        };
    }
}
