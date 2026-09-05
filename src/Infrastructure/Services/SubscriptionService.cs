using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioApiClient _maxioClient;
    private readonly AppIdentityDbContext _identityContext;
    private readonly IReadRepository<MaxioCustomer> _maxioCustomerRepository;
    private readonly IReadRepository<MaxioSubscription> _maxioSubscriptionRepository;
    private readonly IRepository<MaxioCustomer> _maxioCustomerWriteRepository;
    private readonly IRepository<MaxioSubscription> _maxioSubscriptionWriteRepository;
    private readonly string _productFamilyHandle;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioApiClient maxioClient,
        AppIdentityDbContext identityContext,
        IReadRepository<MaxioCustomer> maxioCustomerRepository,
        IReadRepository<MaxioSubscription> maxioSubscriptionRepository,
        IRepository<MaxioCustomer> maxioCustomerWriteRepository,
        IRepository<MaxioSubscription> maxioSubscriptionWriteRepository,
        string productFamilyHandle,
        ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _identityContext = identityContext;
        _maxioCustomerRepository = maxioCustomerRepository;
        _maxioSubscriptionRepository = maxioSubscriptionRepository;
        _maxioCustomerWriteRepository = maxioCustomerWriteRepository;
        _maxioSubscriptionWriteRepository = maxioSubscriptionWriteRepository;
        _productFamilyHandle = productFamilyHandle;
        _logger = logger;
    }

    public async Task<IEnumerable<SubscriptionPlanDto>> GetAvailablePlansAsync()
    {
        try
        {
            var products = await _maxioClient.GetProductsByFamilyHandleAsync(_productFamilyHandle);
            return products.Select(p => new SubscriptionPlanDto
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                Price = p.PriceInCents / 100m,
                PriceDisplay = $"${p.PriceInCents / 100m:F2} per {p.IntervalUnit}",
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available plans");
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string productHandle)
    {
        try
        {
            var maxioCustomer = await GetOrCreateMaxioCustomerAsync(userId);

            var subscription = await _maxioClient.CreateSubscriptionAsync(maxioCustomer.Id, productHandle);

            var dbSubscription = new MaxioSubscription
            {
                ApplicationUserId = userId,
                MaxioSubscriptionId = subscription.Id,
                ProductHandle = subscription.ProductHandle,
                State = subscription.State,
                ProductPriceInCents = subscription.ProductPriceInCents,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt,
                CreatedAt = subscription.CreatedAt,
                UpdatedAt = subscription.UpdatedAt
            };

            await _maxioSubscriptionWriteRepository.AddAsync(dbSubscription);

            return MapToSubscriptionDto(dbSubscription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for user {UserId} and product {ProductHandle}", userId, productHandle);
            throw;
        }
    }

    public async Task<IEnumerable<SubscriptionDto>> GetUserSubscriptionsAsync(string userId)
    {
        try
        {
            var dbSubscriptions = _identityContext.MaxioSubscriptions
                .Where(s => s.ApplicationUserId == userId)
                .ToList();

            return dbSubscriptions.Select(MapToSubscriptionDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscriptions for user {UserId}", userId);
            throw;
        }
    }

    private async Task<MaxioCustomerResponse> GetOrCreateMaxioCustomerAsync(string userId)
    {
        var existingCustomer = _identityContext.MaxioCustomers
            .FirstOrDefault(c => c.ApplicationUserId == userId);

        if (existingCustomer != null)
        {
            return new MaxioCustomerResponse { Id = existingCustomer.MaxioCustomerId };
        }

        var user = await _identityContext.Users.FindAsync(userId);
        if (user == null)
        {
            throw new InvalidOperationException($"User {userId} not found");
        }

        var maxioCustomer = await _maxioClient.CreateOrGetCustomerAsync(
            userId,
            user.UserName ?? "User",
            "",
            user.Email ?? ""
        );

        var dbMaxioCustomer = new MaxioCustomer
        {
            ApplicationUserId = userId,
            MaxioCustomerId = maxioCustomer.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _maxioCustomerWriteRepository.AddAsync(dbMaxioCustomer);

        return maxioCustomer;
    }

    private static SubscriptionDto MapToSubscriptionDto(MaxioSubscription dbSubscription)
    {
        return new SubscriptionDto
        {
            Id = dbSubscription.Id,
            MaxioSubscriptionId = dbSubscription.MaxioSubscriptionId,
            ProductHandle = dbSubscription.ProductHandle,
            State = dbSubscription.State,
            Price = dbSubscription.ProductPriceInCents / 100m,
            PriceDisplay = $"${dbSubscription.ProductPriceInCents / 100m:F2}",
            CurrentPeriodEndsAt = dbSubscription.CurrentPeriodEndsAt,
            NextAssessmentAt = dbSubscription.NextAssessmentAt,
            ActivatedAt = dbSubscription.ActivatedAt
        };
    }
}
