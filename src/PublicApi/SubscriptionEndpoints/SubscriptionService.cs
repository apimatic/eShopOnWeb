using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Api;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct);
    Task<SubscriptionDto> EnrollAsync(string userId, string planHandle, CancellationToken ct);
    Task<IReadOnlyList<SubscriptionDto>> ListUserSubscriptionsAsync(string userId, CancellationToken ct);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<SubscriptionService> _logger;
    private const string ProductFamilyHandle = "eshop-subscribe";

    public SubscriptionService(MaxioAdvancedBillingClient client, ILogger<SubscriptionService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct)
    {
        try
        {
            var response = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: ProductFamilyHandle,
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

            var plans = new List<SubscriptionPlanDto>();
            if (response != null)
            {
                foreach (var productResponse in response)
                {
                    if (productResponse?.Product != null)
                    {
                        plans.Add(new SubscriptionPlanDto
                        {
                            Id = productResponse.Product.Id ?? 0,
                            Name = productResponse.Product.Name ?? string.Empty,
                            Handle = productResponse.Product.Handle ?? string.Empty,
                            PriceInCents = productResponse.Product.PriceInCents ?? 0,
                            Description = productResponse.Product.Description ?? string.Empty,
                            Interval = productResponse.Product.Interval ?? 1,
                            IntervalUnit = productResponse.Product.IntervalUnit?.ToString() ?? "month"
                        });
                    }
                }
            }
            return plans;
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            _logger.LogError("Failed to list plans: {Error}", ex.Message);
            if (ex.Error.TryGetRawError(out RawError raw))
            {
                _logger.LogError("API error: HTTP {StatusCode}", (int)raw.StatusCode);
            }
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing plans");
            throw;
        }
    }

    public async Task<SubscriptionDto> EnrollAsync(string userId, string planHandle, CancellationToken ct)
    {
        try
        {
            // Step 1: Check if customer already exists
            CustomerResponse? existingCustomer = null;
            try
            {
                existingCustomer = await _client.Customers.ReadCustomerByReference(reference: userId, ct: ct);
            }
            catch (SdkException<RawError>)
            {
                // Customer doesn't exist, will create inline
            }

            // Step 2: Check if subscription already exists (idempotency)
            if (existingCustomer?.Customer != null && existingCustomer.Customer.Id.HasValue)
            {
                var existingSubs = await _client.Customers.ListCustomerSubscriptions(
                    customerId: (int)existingCustomer.Customer.Id,
                    ct: ct);

                if (existingSubs != null && existingSubs.Any())
                {
                    var activeSub = existingSubs
                        .FirstOrDefault(s => s.Subscription?.State == SubscriptionState.Active);
                    if (activeSub?.Subscription != null)
                    {
                        _logger.LogInformation("User {UserId} already has active subscription {SubId}",
                            userId, activeSub.Subscription.Id);
                        return MapToSubscriptionDto(activeSub.Subscription);
                    }
                }
            }

            // Step 3: Create subscription
            var subscriptionData = new CreateSubscription
            {
                ProductHandle = planHandle,
                Reference = userId
            };

            // Set customer ID if existing, otherwise set customer attributes for inline creation
            if (existingCustomer?.Customer?.Id.HasValue == true)
            {
                subscriptionData = subscriptionData with { CustomerId = (int)existingCustomer.Customer.Id };
            }
            else
            {
                subscriptionData = subscriptionData with
                {
                    CustomerAttributes = new CustomerAttributes
                    {
                        Reference = userId,
                        Email = userId
                    }
                };
            }

            var createRequest = new CreateSubscriptionRequest
            {
                Subscription = subscriptionData
            };

            var response = await _client.Subscriptions.CreateSubscription(body: createRequest, ct: ct);

            if (response?.Subscription != null)
            {
                _logger.LogInformation("User {UserId} enrolled in plan {PlanHandle}, subscription {SubId}",
                    userId, planHandle, response.Subscription.Id);
                return MapToSubscriptionDto(response.Subscription);
            }

            throw new InvalidOperationException("Failed to create subscription: empty response");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            _logger.LogError("Failed to enroll user {UserId}: {Error}", userId, ex.Message);
            if (ex.Error.TryGetErrorListResponse1(out var validationError))
            {
                _logger.LogError("Validation error: {Details}", validationError.Errors);
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                _logger.LogError("API error: HTTP {StatusCode}", (int)raw.StatusCode);
            }
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error enrolling user {UserId}", userId);
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListUserSubscriptionsAsync(string userId, CancellationToken ct)
    {
        try
        {
            // Look up customer by reference
            CustomerResponse? customer = null;
            try
            {
                customer = await _client.Customers.ReadCustomerByReference(reference: userId, ct: ct);
            }
            catch (SdkException<RawError>)
            {
                // Customer not found
                return new List<SubscriptionDto>();
            }

            if (customer?.Customer?.Id == null)
            {
                return new List<SubscriptionDto>();
            }

            // Get subscriptions for this customer
            var response = await _client.Customers.ListCustomerSubscriptions(
                customerId: (int)customer.Customer.Id,
                ct: ct);

            var subscriptions = new List<SubscriptionDto>();
            if (response != null)
            {
                foreach (var subResponse in response)
                {
                    if (subResponse?.Subscription != null)
                    {
                        subscriptions.Add(MapToSubscriptionDto(subResponse.Subscription));
                    }
                }
            }
            return subscriptions;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Failed to list subscriptions for user {UserId}: HTTP {StatusCode}",
                userId, (int)ex.Error.StatusCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing subscriptions for user {UserId}", userId);
            throw;
        }
    }

    private static SubscriptionDto MapToSubscriptionDto(Subscription sub)
    {
        return new SubscriptionDto
        {
            Id = sub.Id ?? 0,
            CustomerId = (int?)sub.Customer?.Id ?? 0,
            ProductId = (int?)sub.Product?.Id ?? 0,
            State = sub.State?.ToString() ?? "unknown",
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            CreatedAt = sub.CreatedAt,
            Reference = sub.Reference
        };
    }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? Reference { get; set; }
}
