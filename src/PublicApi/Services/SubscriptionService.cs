using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.MaxioDtos;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class SubscriptionService : ISubscriptionService
{
    private readonly MaxioClient _maxio;
    private readonly IRepository<UserSubscription> _subscriptionRepo;
    private readonly MaxioOptions _options;

    public SubscriptionService(
        MaxioClient maxio,
        IRepository<UserSubscription> subscriptionRepo,
        MaxioOptions options)
    {
        _maxio = maxio;
        _subscriptionRepo = subscriptionRepo;
        _options = options;
    }

    public async Task<SubscriptionPlanListResult> ListPlansAsync()
    {
        try
        {
            var products = await _maxio.ListProductsAsync();
            var plans = products
                .Where(p => p.ArchivedAt == null)
                .Select(p => new SubscriptionPlanDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Handle = p.Handle,
                    Description = p.Description,
                    Price = p.PriceInCents / 100m,
                    IntervalUnit = p.IntervalUnit,
                    Interval = p.Interval
                })
                .ToList();

            return new SubscriptionPlanListResult { IsSuccess = true, Plans = plans };
        }
        catch (Exception ex)
        {
            return new SubscriptionPlanListResult { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<SubscriptionResult> SubscribeAsync(string userId, string userEmail, string productHandle)
    {
        try
        {
            var customer = await _maxio.FindCustomerByEmailAsync(userEmail);
            if (customer == null)
            {
                var parts = userEmail.Split('@');
                var firstName = parts.Length > 0 ? parts[0] : "User";
                var lastName = parts.Length > 1 ? parts[1] : "Unknown";
                customer = await _maxio.CreateCustomerAsync(firstName, lastName, userEmail, userId);
            }

            // Check for existing active subscription to the same product (idempotency)
            var existingSubs = await _maxio.ListCustomerSubscriptionsAsync(customer.Id);
            var existingSub = existingSubs.FirstOrDefault(s =>
                s.Product?.Handle == productHandle &&
                s.State is "active" or "trialing");

            MaxioSubscription subscription;
            if (existingSub != null)
            {
                subscription = existingSub;
            }
            else
            {
                subscription = await _maxio.CreateSubscriptionAsync(productHandle, customer.Id);
            }

            // Check for existing local record (idempotency — double-click guard)
            var localSpec = new UserSubscriptionsByUserIdSpec(userId);
            var existingLocal = (await _subscriptionRepo.ListAsync(localSpec))
                .FirstOrDefault(s => s.MaxioSubscriptionId == subscription.Id);

            if (existingLocal != null)
            {
                return new SubscriptionResult
                {
                    IsSuccess = true,
                    SubscriptionId = existingLocal.Id,
                    PlanName = existingLocal.PlanName,
                    PlanHandle = existingLocal.PlanHandle,
                    Price = existingLocal.Price,
                    State = existingLocal.State,
                    NextBillingDate = existingLocal.NextBillingDate
                };
            }

            var userSub = new UserSubscription
            {
                UserId = userId,
                MaxioCustomerId = customer.Id,
                MaxioSubscriptionId = subscription.Id,
                PlanHandle = productHandle,
                PlanName = subscription.Product?.Name ?? productHandle,
                Price = subscription.ProductPriceInCents / 100m,
                State = subscription.State,
                NextBillingDate = ParseDateTime(subscription.NextAssessmentAt),
                CreatedAt = DateTime.UtcNow
            };
            await _subscriptionRepo.AddAsync(userSub);

            return new SubscriptionResult
            {
                IsSuccess = true,
                SubscriptionId = userSub.Id,
                PlanName = userSub.PlanName,
                PlanHandle = userSub.PlanHandle,
                Price = userSub.Price,
                State = userSub.State,
                NextBillingDate = userSub.NextBillingDate
            };
        }
        catch (Exception ex)
        {
            return new SubscriptionResult { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<UserSubscriptionListResult> GetMySubscriptionsAsync(string userId)
    {
        try
        {
            var spec = new UserSubscriptionsByUserIdSpec(userId);
            var subs = await _subscriptionRepo.ListAsync(spec);
            var dtos = subs.Select(s => new UserSubscriptionDto
            {
                Id = s.Id,
                MaxioSubscriptionId = s.MaxioSubscriptionId,
                PlanName = s.PlanName,
                PlanHandle = s.PlanHandle,
                Price = s.Price,
                State = s.State,
                NextBillingDate = s.NextBillingDate,
                CreatedAt = s.CreatedAt
            }).ToList();

            return new UserSubscriptionListResult { IsSuccess = true, Subscriptions = dtos };
        }
        catch (Exception ex)
        {
            return new UserSubscriptionListResult { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    private static DateTime? ParseDateTime(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return DateTime.TryParse(value, out var dt) ? dt : null;
    }
}
