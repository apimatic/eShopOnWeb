using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioService
{
    Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync();
    Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string planHandle, string firstName, string lastName, string email);
    Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId);
}

public class MaxioService : IMaxioService
{
    private readonly MaxioClient _client;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(MaxioClient client, ILogger<MaxioService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync()
    {
        _logger.LogInformation("Fetching subscription plans from Maxio");

        var response = await _client.GetAsync<MaxioProductsResponse>("/products.json");

        if (response?.Items == null)
        {
            _logger.LogWarning("No products returned from Maxio");
            return new List<SubscriptionPlanDto>();
        }

        var plans = response.Items
            .Where(item => item.Product != null)
            .Select(item => new SubscriptionPlanDto
            {
                Id = item.Product!.Id,
                Handle = item.Product!.Handle ?? string.Empty,
                Name = item.Product!.Name,
                Description = item.Product!.Description,
                PriceInCents = item.Product!.PriceInCents,
                Interval = item.Product!.Interval,
                IntervalUnit = item.Product!.IntervalUnit
            })
            .ToList();

        _logger.LogInformation("Found {count} plans", plans.Count);
        return plans;
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string userId,
        string planHandle,
        string firstName,
        string lastName,
        string email)
    {
        _logger.LogInformation("Creating subscription for userId={userId}, plan={planHandle}", userId, planHandle);

        var request = new CreateSubscriptionRequest
        {
            Subscription = new SubscriptionData
            {
                ProductHandle = planHandle,
                CustomerReference = userId,
                CustomerAttributes = new CustomerData
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = userId
                },
                PaymentCollectionMethod = "automatic"
            }
        };

        var response = await _client.PostAsync<MaxioSubscriptionResponse>("/subscriptions.json", request);

        if (response?.Subscription == null)
        {
            throw new InvalidOperationException("Failed to create subscription: no subscription in response");
        }

        return MapToDto(response.Subscription);
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId)
    {
        _logger.LogInformation("Fetching subscriptions for userId={userId}", userId);

        // First, try to look up the customer by reference
        MaxioCustomerResponse? customerResponse;
        try
        {
            customerResponse = await _client.GetAsync<MaxioCustomerResponse>($"/customers/lookup.json?reference={Uri.EscapeDataString(userId)}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Customer not found for userId={userId}", userId);
            return new List<SubscriptionDto>();
        }

        if (customerResponse?.Customer?.Id == null)
        {
            _logger.LogWarning("Customer lookup returned no customer for userId={userId}", userId);
            return new List<SubscriptionDto>();
        }

        var customerId = customerResponse.Customer.Id;
        _logger.LogInformation("Found customer id={customerId} for userId={userId}", customerId, userId);

        // Get subscriptions for this customer
        var subsResponse = await _client.GetAsync<MaxioSubscriptionsResponse>($"/customers/{customerId}/subscriptions.json");

        if (subsResponse?.Subscriptions == null || subsResponse.Subscriptions.Count == 0)
        {
            _logger.LogInformation("No subscriptions found for customerId={customerId}", customerId);
            return new List<SubscriptionDto>();
        }

        var subs = subsResponse.Subscriptions
            .Select(MapToDto)
            .ToList();

        _logger.LogInformation("Found {count} subscriptions for customerId={customerId}", subs.Count, customerId);
        return subs;
    }

    private static SubscriptionDto MapToDto(MaxioSubscription sub)
    {
        return new SubscriptionDto
        {
            Id = sub.Id,
            State = sub.State,
            ProductName = sub.Product?.Name ?? "Unknown",
            ProductHandle = sub.Product?.Handle ?? string.Empty,
            PriceInCents = sub.Product?.PriceInCents ?? 0,
            Balance = sub.BalanceInCents / 100m,
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            NextBillingAt = sub.NextAssessmentAt ?? sub.CurrentPeriodEndsAt,
            CreatedAt = sub.CreatedAt,
            UpdatedAt = sub.UpdatedAt
        };
    }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";

    public decimal Price => PriceInCents / 100m;
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Balance { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public decimal Price => PriceInCents / 100m;
}
