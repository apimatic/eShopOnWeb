using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioClient _maxioClient;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioClient maxioClient,
        IRepository<Subscription> subscriptionRepository,
        ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _subscriptionRepository = subscriptionRepository;
        _logger = logger;
    }

    public async Task<Subscription> SubscribeAsync(string userId, string email, string firstName, string lastName, string productHandle)
    {
        try
        {
            var maxioCustomer = await _maxioClient.CreateCustomerAsync(email, firstName, lastName);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}", maxioCustomer.Id, userId);

            var maxioSubscription = await _maxioClient.CreateSubscriptionAsync(maxioCustomer.Id, productHandle);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for customer {CustomerId}", maxioSubscription.Id, maxioCustomer.Id);

            var nextBilling = DateTime.TryParse(maxioSubscription.NextBillingAt, out var nextBillingDate)
                ? nextBillingDate
                : DateTime.UtcNow.AddMonths(1);

            var subscription = new Subscription(
                userId,
                maxioCustomer.Id,
                maxioSubscription.Id,
                maxioSubscription.ProductHandle,
                maxioSubscription.State,
                maxioSubscription.CurrentPrice,
                nextBilling
            );

            await _subscriptionRepository.AddAsync(subscription);
            _logger.LogInformation("Stored subscription {SubscriptionId} for user {UserId}", subscription.Id, userId);

            return subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for user {UserId}", userId);
            throw;
        }
    }

    public async Task<IEnumerable<Subscription>> GetUserSubscriptionsAsync(string userId)
    {
        return await _subscriptionRepository.ListAsync(new SubscriptionsByUserSpecification(userId));
    }
}

public class SubscriptionsByUserSpecification : Specification<Subscription>
{
    public SubscriptionsByUserSpecification(string userId)
    {
        Query.Where(s => s.UserId == userId);
    }
}
