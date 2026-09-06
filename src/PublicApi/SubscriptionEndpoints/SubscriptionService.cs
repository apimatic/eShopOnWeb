using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionService
{
    Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync();
    Task<CreateSubscriptionResponse> CreateSubscriptionAsync(string userId, string? productHandle);
    Task<List<MySubscriptionDto>> GetMySubscriptionsAsync(string userId);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioApiClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionService(IMaxioApiClient maxioClient, UserManager<ApplicationUser> userManager)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync()
    {
        var products = await _maxioClient.ListProductsAsync("eshop-subscribe");

        var plans = products
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                Description = p.Description,
                Price = p.PriceInCents / 100m,
                BillingIntervalDays = p.Interval,
                BillingInterval = p.IntervalUnit,
                RequiresCreditCard = p.RequiresCreditCard
            })
            .ToList();

        return plans;
    }

    public async Task<CreateSubscriptionResponse> CreateSubscriptionAsync(string userId, string? productHandle)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            throw new InvalidOperationException("User not found");
        }

        var maxioCustomer = await _maxioClient.GetOrCreateCustomerAsync(
            userId,
            user.Email ?? "",
            user.UserName ?? "",
            user.UserName ?? ""
        );

        var subscriptionRequest = new CreateMaxioSubscriptionRequest
        {
            CustomerId = maxioCustomer.Id,
            ProductHandle = productHandle,
            PaymentCollectionMethod = "remittance"
        };

        var subscription = await _maxioClient.CreateSubscriptionAsync(subscriptionRequest);

        var response = new CreateSubscriptionResponse
        {
            Id = subscription.Id,
            State = subscription.State,
            ProductName = subscription.Product?.Name,
            ProductHandle = subscription.Product?.Handle,
            PricePerBillingCycle = subscription.Product?.PriceInCents / 100m ?? 0,
            BillingIntervalDays = subscription.Product?.Interval ?? 0,
            BillingInterval = subscription.Product?.IntervalUnit,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt
        };

        return response;
    }

    public async Task<List<MySubscriptionDto>> GetMySubscriptionsAsync(string userId)
    {
        try
        {
            var customer = await _maxioClient.GetCustomerByReferenceAsync(userId);
            var subscriptions = await _maxioClient.ListSubscriptionsAsync(customer.Id.ToString());

            var subscriptionDtos = subscriptions
                .Select(s => new MySubscriptionDto
                {
                    Id = s.Id,
                    State = s.State,
                    ProductName = s.Product?.Name,
                    ProductHandle = s.Product?.Handle,
                    PricePerBillingCycle = s.Product?.PriceInCents / 100m ?? 0,
                    BillingIntervalDays = s.Product?.Interval ?? 0,
                    BillingInterval = s.Product?.IntervalUnit,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                    NextAssessmentAt = s.NextAssessmentAt,
                    ActivatedAt = s.ActivatedAt,
                    CreatedAt = s.CreatedAt
                })
                .ToList();

            return subscriptionDtos;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new List<MySubscriptionDto>();
        }
    }
}
