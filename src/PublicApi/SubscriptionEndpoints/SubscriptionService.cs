using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionService
{
    Task<List<MaxioProduct>> ListPlansAsync();
    Task<SubscriptionResult> CreateSubscriptionAsync(string userId, string productHandle);
    Task<List<SubscriptionDto>> ListUserSubscriptionsAsync(string userId);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IReadRepository<Subscription> _subscriptionReadRepository;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly MaxioConfiguration _config;

    public SubscriptionService(
        IMaxioClient maxioClient,
        UserManager<ApplicationUser> userManager,
        IRepository<Subscription> subscriptionRepository,
        IReadRepository<Subscription> subscriptionReadRepository,
        IAppLogger<SubscriptionService> logger,
        MaxioConfiguration config)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
        _subscriptionRepository = subscriptionRepository;
        _subscriptionReadRepository = subscriptionReadRepository;
        _logger = logger;
        _config = config;
    }

    public async Task<List<MaxioProduct>> ListPlansAsync()
    {
        return await _maxioClient.ListProductsByFamilyAsync(_config.ProductFamilyHandle);
    }

    public async Task<SubscriptionResult> CreateSubscriptionAsync(string userId, string productHandle)
    {
        try
        {
            var appUser = await _userManager.FindByIdAsync(userId);
            if (appUser == null)
            {
                return SubscriptionResult.Failure("User not found");
            }

            var maxioCustomer = await _maxioClient.GetOrCreateCustomerAsync(
                appUser.Id,
                appUser.Email ?? "",
                appUser.UserName ?? "",
                appUser.UserName ?? "");

            if (maxioCustomer == null)
            {
                return SubscriptionResult.Failure("Failed to create/retrieve customer in billing system");
            }

            var maxioSubscription = await _maxioClient.CreateSubscriptionAsync(
                maxioCustomer.Id,
                productHandle);

            if (maxioSubscription == null)
            {
                return SubscriptionResult.Failure("Failed to create subscription");
            }

            var subscription = new Subscription
            {
                UserId = appUser.Id,
                MaxioCustomerId = maxioCustomer.Id,
                MaxioSubscriptionId = maxioSubscription.Id,
                ProductHandle = maxioSubscription.ProductHandle,
                ProductName = maxioSubscription.ProductName,
                PriceInCents = maxioSubscription.PriceInCents,
                State = maxioSubscription.State,
                CurrentPeriodStartsAt = maxioSubscription.CurrentPeriodStartsAt,
                CurrentPeriodEndsAt = maxioSubscription.CurrentPeriodEndsAt,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _subscriptionRepository.AddAsync(subscription);

            var dto = new SubscriptionDto
            {
                Id = subscription.Id,
                ProductHandle = subscription.ProductHandle,
                ProductName = subscription.ProductName,
                PriceInDollars = subscription.PriceInCents / 100m,
                State = subscription.State,
                CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
            };

            _logger.LogInformation("Created subscription for user {userId}", userId);
            return SubscriptionResult.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription");
            return SubscriptionResult.Failure("An error occurred while creating the subscription");
        }
    }

    public async Task<List<SubscriptionDto>> ListUserSubscriptionsAsync(string userId)
    {
        try
        {
            var spec = new SubscriptionsByUserSpecification(userId);
            var localSubscriptions = await _subscriptionReadRepository.ListAsync(spec);

            if (!localSubscriptions.Any())
            {
                return new List<SubscriptionDto>();
            }

            var firstSub = localSubscriptions.First();
            var maxioSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(firstSub.MaxioCustomerId);

            var result = maxioSubscriptions.Select(ms => new SubscriptionDto
            {
                Id = ms.Id,
                ProductHandle = ms.ProductHandle,
                ProductName = ms.ProductName,
                PriceInDollars = ms.PriceInCents / 100m,
                State = ms.State,
                CurrentPeriodStartsAt = ms.CurrentPeriodStartsAt,
                CurrentPeriodEndsAt = ms.CurrentPeriodEndsAt
            }).ToList();

            _logger.LogInformation("Retrieved subscriptions for user {userId}", userId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving subscriptions");
            return new List<SubscriptionDto>();
        }
    }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal PriceInDollars { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
}

public class SubscriptionResult
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public SubscriptionDto? Data { get; set; }

    public static SubscriptionResult Success(SubscriptionDto data) =>
        new() { IsSuccess = true, Message = "Subscription created successfully", Data = data };

    public static SubscriptionResult Failure(string message) =>
        new() { IsSuccess = false, Message = message };
}
