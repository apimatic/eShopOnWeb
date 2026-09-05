using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services.Maxio;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services.Subscriptions;

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioApiClient _maxioClient;
    private readonly IRepository<SubscriptionPlan> _planRepository;
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioApiClient maxioClient,
        IRepository<SubscriptionPlan> planRepository,
        IRepository<UserSubscription> userSubscriptionRepository,
        ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _planRepository = planRepository;
        _userSubscriptionRepository = userSubscriptionRepository;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlan>> GetAvailablePlansAsync()
    {
        try
        {
            var plans = await _planRepository.ListAsync();
            var activePlans = plans.Where(p => !p.IsArchived).ToList();

            if (!activePlans.Any())
            {
                _logger.LogInformation("No plans found in database, refreshing from Maxio");
                return new List<SubscriptionPlan>();
            }

            return activePlans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available plans");
            return new List<SubscriptionPlan>();
        }
    }

    public async Task<UserSubscription> CreateSubscriptionAsync(string userId, string userEmail, string? firstName, string? lastName, string planHandle)
    {
        try
        {
            var maxioCustomer = await _maxioClient.CreateOrGetCustomerAsync(userId, userEmail, firstName, lastName);
            _logger.LogInformation("Created/got Maxio customer: {customerId} for userId: {userId}", maxioCustomer.Id, userId);

            var maxioSubscription = await _maxioClient.CreateSubscriptionAsync(maxioCustomer.Id, planHandle);
            _logger.LogInformation("Created Maxio subscription: {subscriptionId} for customerId: {customerId}", maxioSubscription.Id, maxioCustomer.Id);

            var plan = await GetPlanByHandleAsync(planHandle);
            if (plan == null)
            {
                throw new InvalidOperationException($"Plan with handle {planHandle} not found");
            }

            var userSubscription = new UserSubscription
            {
                UserId = userId,
                MaxioCustomerId = maxioCustomer.Id,
                MaxioSubscriptionId = maxioSubscription.Id,
                SubscriptionPlanId = plan.Id,
                State = maxioSubscription.State,
                CurrentPriceInDollars = maxioSubscription.ProductPriceInCents / 100m,
                NextAssessmentAt = maxioSubscription.NextAssessmentAt != null ? DateTime.Parse(maxioSubscription.NextAssessmentAt) : null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _userSubscriptionRepository.AddAsync(userSubscription);

            return userSubscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for userId: {userId}", userId);
            throw;
        }
    }

    public async Task<List<UserSubscription>> GetUserSubscriptionsAsync(string userId)
    {
        try
        {
            var subscriptions = await _userSubscriptionRepository.ListAsync();
            return subscriptions.Where(s => s.UserId == userId).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscriptions for userId: {userId}", userId);
            return new List<UserSubscription>();
        }
    }

    public async Task<SubscriptionPlan?> GetPlanByHandleAsync(string handle)
    {
        try
        {
            var plans = await _planRepository.ListAsync();
            return plans.FirstOrDefault(p => p.Handle == handle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting plan by handle: {handle}", handle);
            return null;
        }
    }
}
