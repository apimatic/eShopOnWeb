using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioSubscriptionService
{
    Task<(int CustomerId, string Reference)> GetOrCreateCustomerAsync(string userId, string userEmail, CancellationToken ct);
    Task<List<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct);
    Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, int productId, string userId, CancellationToken ct);
    Task<List<UserSubscriptionDto>> ListUserSubscriptionsAsync(int customerId, CancellationToken ct);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly IConfiguration _configuration;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        ILogger<MaxioSubscriptionService> logger,
        IConfiguration configuration)
    {
        _client = client;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<(int CustomerId, string Reference)> GetOrCreateCustomerAsync(
        string userId, string userEmail, CancellationToken ct)
    {
        try
        {
            var customer = await _client.Customers.ReadCustomerByReference(userId, ct);
            if (customer?.Customer != null)
            {
                _logger.LogInformation("Found existing Maxio customer: {CustomerId}", customer.Customer.Id);
                return (customer.Customer.Id ?? 0, customer.Customer.Reference ?? userId);
            }
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Customer not found, creating new one for userId: {UserId}", userId);
                return await CreateCustomerAsync(userId, userEmail, ct);
            }
            _logger.LogError(ex, "Error looking up customer: {Status}", ex.Error.StatusCode);
            throw;
        }

        return await CreateCustomerAsync(userId, userEmail, ct);
    }

    private async Task<(int CustomerId, string Reference)> CreateCustomerAsync(
        string userId, string userEmail, CancellationToken ct)
    {
        var createRequest = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                Reference = userId,
                Email = userEmail,
                FirstName = "",
                LastName = ""
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(createRequest, ct);
            if (response?.Customer != null)
            {
                _logger.LogInformation("Created Maxio customer: {CustomerId}", response.Customer.Id);
                return (response.Customer.Id ?? 0, response.Customer.Reference ?? userId);
            }
            throw new InvalidOperationException("Failed to create customer: Empty response");
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Customer creation failed: {Status}", ex.Error.StatusCode);
            throw new InvalidOperationException($"Failed to create customer: {ex.Error.ReadAsString()}", ex);
        }
    }

    public async Task<List<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct)
    {
        try
        {
            var products = await _client.Products.ListProducts(
                null, null, null, null, null, null, null, null, 1, 100, ct);

            var plans = new List<SubscriptionPlanDto>();
            foreach (var item in products)
            {
                var product = item.Product;
                if (product?.Id.HasValue == true)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id.Value,
                        Name = product.Name,
                        Handle = product.Handle,
                        PriceInCents = product.PriceInCents,
                        Interval = product.Interval,
                        IntervalUnit = product.IntervalUnit?.Value
                    });
                }
            }

            _logger.LogInformation("Listed {Count} subscription plans", plans.Count);
            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error listing subscription plans: {Status}", ex.Error.StatusCode);
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        int customerId, int productId, string userId, CancellationToken ct)
    {
        var reference = $"{userId}:{productId}:{DateTime.UtcNow.Ticks}";
        var createRequest = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                CustomerId = customerId,
                ProductId = productId,
                Reference = reference
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(createRequest, ct);
            var subscription = response?.Subscription;

            if (subscription == null)
            {
                throw new InvalidOperationException("Failed to create subscription: Empty response");
            }

            _logger.LogInformation(
                "Created subscription: {SubscriptionId}, State: {State}",
                subscription.Id, subscription.State?.Value);

            return new SubscriptionDto
            {
                Id = subscription.Id,
                State = subscription.State?.Value,
                Reference = subscription.Reference,
                NextAssessmentAt = subscription.NextAssessmentAt,
                CreatedAt = subscription.CreatedAt
            };
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Subscription creation failed: {Status}", ex.Error.StatusCode);
            throw new InvalidOperationException($"Failed to create subscription: {ex.Error.ReadAsString()}", ex);
        }
    }

    public async Task<List<UserSubscriptionDto>> ListUserSubscriptionsAsync(
        int customerId, CancellationToken ct)
    {
        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct);

            var dtos = new List<UserSubscriptionDto>();
            foreach (var item in subscriptions)
            {
                var subscription = item.Subscription;
                if (subscription?.Id.HasValue == true)
                {
                    dtos.Add(new UserSubscriptionDto
                    {
                        Id = subscription.Id,
                        State = subscription.State?.Value,
                        Reference = subscription.Reference,
                        NextAssessmentAt = subscription.NextAssessmentAt,
                        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                        CreatedAt = subscription.CreatedAt
                    });
                }
            }

            _logger.LogInformation("Listed {Count} subscriptions for customer {CustomerId}", dtos.Count, customerId);
            return dtos;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error listing subscriptions: {Status}", ex.Error.StatusCode);
            throw;
        }
    }
}

public record SubscriptionPlanDto
{
    public int? Id { get; init; }
    public string? Name { get; init; }
    public string? Handle { get; init; }
    public long? PriceInCents { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
}

public record SubscriptionDto
{
    public int? Id { get; init; }
    public string? State { get; init; }
    public int? CustomerId { get; init; }
    public int? ProductId { get; init; }
    public string? Reference { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}

public record UserSubscriptionDto
{
    public int? Id { get; init; }
    public string? State { get; init; }
    public int? ProductId { get; init; }
    public string? ProductHandle { get; init; }
    public string? Reference { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}
