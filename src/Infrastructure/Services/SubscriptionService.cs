using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface ISubscriptionService
{
    Task<SubscriptionDto> SubscribeToPlanAsync(string userId, string email, string firstName, string lastName, string planHandle);
    Task<SubscriptionDto[]> GetUserSubscriptionsAsync(string userId);
    Task<AvailablePlanDto[]> GetAvailablePlansAsync();
}

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioApiClient _maxioClient;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly MaxioSettings _maxioSettings;

    public SubscriptionService(
        IMaxioApiClient maxioClient,
        IRepository<Subscription> subscriptionRepository,
        ILogger<SubscriptionService> logger,
        MaxioSettings maxioSettings)
    {
        _maxioClient = maxioClient;
        _subscriptionRepository = subscriptionRepository;
        _logger = logger;
        _maxioSettings = maxioSettings;
    }

    public async Task<SubscriptionDto> SubscribeToPlanAsync(string userId, string email, string firstName, string lastName, string planHandle)
    {
        if (string.IsNullOrEmpty(_maxioSettings.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio product family handle is not configured");
        }

        var customerReference = $"eshop_user_{userId}";

        _logger.LogInformation("Creating or retrieving Maxio customer for user {UserId}", userId);
        var customer = await _maxioClient.GetOrCreateCustomerAsync(customerReference, email, firstName, lastName);

        _logger.LogInformation("Creating subscription for customer {CustomerId} to plan {PlanHandle}", customer.Id, planHandle);
        var subscription = await _maxioClient.CreateSubscriptionAsync(customer.Id, planHandle);

        _logger.LogInformation("Saving subscription to database for user {UserId}", userId);
        var localSubscription = new Subscription(
            buyerId: 0,
            identityId: userId,
            maxioSubscriptionId: subscription.Id,
            maxioCustomerId: subscription.CustomerId,
            planHandle: subscription.ProductHandle,
            planName: subscription.ProductName,
            priceInCents: (decimal)subscription.PriceInCents,
            status: subscription.State,
            currentPeriodStartAt: subscription.CreatedAt,
            currentPeriodEndAt: subscription.NextBillingAt);

        await _subscriptionRepository.AddAsync(localSubscription);
        await _subscriptionRepository.SaveChangesAsync();

        return MapToDto(subscription);
    }

    public async Task<SubscriptionDto[]> GetUserSubscriptionsAsync(string userId)
    {
        var subscriptions = await _subscriptionRepository.ListAsync(
            new UserSubscriptionsSpecification(userId));

        if (!subscriptions.Any())
        {
            _logger.LogInformation("No subscriptions found for user {UserId}", userId);
            return Array.Empty<SubscriptionDto>();
        }

        var result = new List<SubscriptionDto>();
        foreach (var subscription in subscriptions)
        {
            try
            {
                var maxioSubscription = await _maxioClient.GetSubscriptionAsync(subscription.MaxioSubscriptionId);
                result.Add(MapToDto(maxioSubscription));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving subscription {SubscriptionId} from Maxio", subscription.MaxioSubscriptionId);
            }
        }

        return result.ToArray();
    }

    public async Task<AvailablePlanDto[]> GetAvailablePlansAsync()
    {
        if (string.IsNullOrEmpty(_maxioSettings.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio product family handle is not configured");
        }

        _logger.LogInformation("Fetching available plans from Maxio");
        var products = await _maxioClient.ListProductsAsync(_maxioSettings.ProductFamilyHandle);

        return products
            .Select(p => new AvailablePlanDto
            {
                Handle = p.Handle ?? p.Id.ToString(),
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                PriceFormatted = FormatPrice(p.PriceInCents)
            })
            .ToArray();
    }

    private static SubscriptionDto MapToDto(MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            CustomerId = subscription.CustomerId,
            PlanHandle = subscription.ProductHandle,
            PlanName = subscription.ProductName,
            Status = subscription.State,
            PriceInCents = subscription.PriceInCents,
            PriceFormatted = FormatPrice(subscription.PriceInCents),
            CurrentPeriodStartsAt = subscription.CreatedAt,
            NextBillingAt = subscription.NextBillingAt
        };
    }

    private static string FormatPrice(long priceInCents)
    {
        var dollars = priceInCents / 100.0m;
        return dollars.ToString("C");
    }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string PlanHandle { get; set; } = null!;
    public string PlanName { get; set; } = null!;
    public string Status { get; set; } = null!;
    public long PriceInCents { get; set; }
    public string PriceFormatted { get; set; } = null!;
    public DateTime CurrentPeriodStartsAt { get; set; }
    public DateTime NextBillingAt { get; set; }
}

public class AvailablePlanDto
{
    public string Handle { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public string PriceFormatted { get; set; } = null!;
}
