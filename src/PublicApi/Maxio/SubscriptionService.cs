using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioApiClient _maxioClient;
    private readonly MaxioBillingDbContext _dbContext;
    private readonly MaxioSettings _maxioSettings;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioApiClient maxioClient,
        MaxioBillingDbContext dbContext,
        MaxioSettings maxioSettings,
        ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _dbContext = dbContext;
        _maxioSettings = maxioSettings;
        _logger = logger;
    }

    public async Task<SubscriptionPlan[]> GetAvailablePlansAsync()
    {
        try
        {
            var response = await _maxioClient.ListProductsByFamilyAsync(_maxioSettings.ProductFamilyHandle);

            var plans = response?.Products
                .Where(p => p.Product != null)
                .Select(p => new SubscriptionPlan
                {
                    Handle = p.Product!.Handle ?? string.Empty,
                    Name = p.Product!.Name ?? string.Empty,
                    PriceInCents = p.Product!.PriceInCents,
                    Interval = p.Product!.IntervalUnit ?? "month",
                    IntervalCount = p.Product!.Interval,
                    ProductId = p.Product!.Id
                })
                .ToArray() ?? Array.Empty<SubscriptionPlan>();

            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans");
            throw;
        }
    }

    public async Task<SubscriptionDto?> CreateSubscriptionAsync(string userId, string userEmail, string firstName, string lastName, string planHandle)
    {
        try
        {
            var maxioCustomerId = await GetOrCreateMaxioCustomerAsync(userId, userEmail, firstName, lastName);

            var createRequest = new CreateSubscriptionRequest
            {
                Subscription = new()
                {
                    CustomerId = maxioCustomerId,
                    ProductHandle = planHandle,
                    PaymentCollectionMethod = "remittance"
                }
            };

            var response = await _maxioClient.CreateSubscriptionAsync(createRequest);

            if (response?.Subscription != null)
            {
                return MapToSubscriptionDto(response.Subscription);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for user {UserId}", userId);
            throw;
        }
    }

    public async Task<SubscriptionDto[]> GetUserSubscriptionsAsync(string userId)
    {
        try
        {
            var mapping = await _dbContext.MaxioCustomerMappings
                .FirstOrDefaultAsync(m => m.UserId == userId);

            if (mapping == null)
            {
                return Array.Empty<SubscriptionDto>();
            }

            var response = await _maxioClient.ListSubscriptionsByCustomerAsync(mapping.MaxioCustomerId);

            var subscriptions = response?.Subscriptions
                .Where(s => s.Subscription != null)
                .Select(s => MapToSubscriptionDto(s.Subscription!))
                .ToArray() ?? Array.Empty<SubscriptionDto>();

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for user {UserId}", userId);
            throw;
        }
    }

    private async Task<long> GetOrCreateMaxioCustomerAsync(string userId, string userEmail, string firstName, string lastName)
    {
        var mapping = await _dbContext.MaxioCustomerMappings
            .FirstOrDefaultAsync(m => m.UserId == userId);

        if (mapping != null)
        {
            return mapping.MaxioCustomerId;
        }

        var customerLookup = await _maxioClient.LookupCustomerAsync(userId);

        if (customerLookup?.Customer != null)
        {
            mapping = new MaxioCustomerMapping
            {
                UserId = userId,
                MaxioCustomerId = customerLookup.Customer.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.MaxioCustomerMappings.Add(mapping);
            await _dbContext.SaveChangesAsync();
            return customerLookup.Customer.Id;
        }

        var createRequest = new CreateCustomerRequest
        {
            Customer = new()
            {
                FirstName = firstName,
                LastName = lastName,
                Email = userEmail,
                Reference = userId
            }
        };

        var createResponse = await _maxioClient.CreateCustomerAsync(createRequest);
        if (createResponse?.Customer == null)
        {
            throw new InvalidOperationException("Failed to create Maxio customer");
        }

        mapping = new MaxioCustomerMapping
        {
            UserId = userId,
            MaxioCustomerId = createResponse.Customer.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.MaxioCustomerMappings.Add(mapping);
        await _dbContext.SaveChangesAsync();

        return createResponse.Customer.Id;
    }

    private SubscriptionDto MapToSubscriptionDto(MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            SubscriptionId = subscription.Id,
            State = subscription.State ?? "unknown",
            ProductName = subscription.Product?.Name ?? string.Empty,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            PriceInCents = subscription.Product?.PriceInCents ?? 0,
            Interval = subscription.Product?.IntervalUnit ?? "month",
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            UpdatedAt = subscription.UpdatedAt
        };
    }
}
