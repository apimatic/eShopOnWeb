using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> ListSubscriptionPlansAsync(CancellationToken ct)
    {
        try
        {
            var plans = new List<SubscriptionPlanDto>();
            var response = await _client.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 20,
                ct: ct);

            foreach (var item in response)
            {
                var product = item.Product;
                if (product != null)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id ?? 0,
                        Handle = product.Handle ?? "",
                        Name = product.Name ?? "",
                        Description = product.Description ?? "",
                        Price = product.PriceInCents.HasValue ? product.PriceInCents.Value / 100m : 0m,
                        Interval = product.Interval ?? 1,
                        IntervalUnit = product.IntervalUnit?.Value ?? "month"
                    });
                }
            }

            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list subscription plans. Status: {StatusCode}", (int)ex.Error.StatusCode);
            throw new InvalidOperationException("Failed to retrieve subscription plans from billing provider", ex);
        }
    }

    public async Task<int> EnsureCustomerExistsAsync(string userId, string email, string firstName, string lastName, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: userId, ct: ct);
            var customer = response.Customer;
            return customer?.Id ?? 0;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Customer not found for reference {UserId}, creating new customer", userId);
            return await CreateCustomerAsync(userId, email, firstName, lastName, ct);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to lookup customer. Status: {StatusCode}", (int)ex.Error.StatusCode);
            throw new InvalidOperationException("Failed to verify customer with billing provider", ex);
        }
    }

    private async Task<int> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken ct)
    {
        try
        {
            var body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = reference
                }
            };

            var response = await _client.Customers.CreateCustomer(body: body, ct: ct);
            var customer = response.Customer;
            return customer?.Id ?? throw new InvalidOperationException("Customer created but no ID returned");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorResponse))
            {
                _logger.LogError("Failed to create customer. Validation errors: {Errors}", errorResponse.Errors?.ToString() ?? "unknown");
                throw new InvalidOperationException($"Customer validation failed", ex);
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                _logger.LogError("Failed to create customer. Status: {StatusCode}, Body: {Body}", (int)raw.StatusCode, raw.ReadAsString());
                throw new InvalidOperationException("Failed to create customer with billing provider", ex);
            }
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to create customer. Status: {StatusCode}", (int)ex.Error.StatusCode);
            throw new InvalidOperationException("Failed to create customer with billing provider", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct)
    {
        try
        {
            var body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(body: body, ct: ct);
            var subscription = response.Subscription;

            return MapSubscriptionToDto(subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorResponse))
            {
                var errors = errorResponse.Errors ?? new List<string>();
                _logger.LogError("Failed to create subscription. Errors: {Errors}", string.Join(", ", errors));
                throw new InvalidOperationException($"Subscription creation failed: {string.Join(", ", errors)}", ex);
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                _logger.LogError("Failed to create subscription. Status: {StatusCode}, Body: {Body}", (int)raw.StatusCode, raw.ReadAsString());
                throw new InvalidOperationException("Failed to create subscription", ex);
            }
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to create subscription. Status: {StatusCode}", (int)ex.Error.StatusCode);
            throw new InvalidOperationException("Failed to create subscription with billing provider", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize subscription response");
            throw new InvalidOperationException("Invalid response from billing provider", ex);
        }
    }

    public async Task<List<SubscriptionDto>> ListUserSubscriptionsAsync(int maxioCustomerId, CancellationToken ct)
    {
        try
        {
            var subscriptions = new List<SubscriptionDto>();
            var response = await _client.Customers.ListCustomerSubscriptions(customerId: maxioCustomerId, ct: ct);

            foreach (var item in response)
            {
                var subscription = item.Subscription;
                if (subscription != null)
                {
                    subscriptions.Add(MapSubscriptionToDto(subscription));
                }
            }

            return subscriptions;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list subscriptions. Status: {StatusCode}", (int)ex.Error.StatusCode);
            throw new InvalidOperationException("Failed to retrieve subscriptions from billing provider", ex);
        }
    }

    private static SubscriptionDto MapSubscriptionToDto(Subscription? subscription)
    {
        if (subscription == null)
            throw new InvalidOperationException("Subscription data missing");

        return new SubscriptionDto
        {
            Id = subscription.Id ?? 0,
            State = subscription.State?.Value ?? "unknown",
            ProductHandle = subscription.Product?.Handle ?? "",
            ProductName = subscription.Product?.Name ?? "",
            Price = subscription.ProductPriceInCents.HasValue ? subscription.ProductPriceInCents.Value / 100m : 0m,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt ?? DateTimeOffset.UtcNow,
            NextAssessmentAt = subscription.NextAssessmentAt ?? DateTimeOffset.UtcNow.AddMonths(1)
        };
    }
}
