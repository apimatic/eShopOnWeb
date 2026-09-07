using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioSubscriptionService
{
    Task<List<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken ct);
    Task<int> EnsureCustomerExistsAsync(string userId, string email, string firstName, string lastName, CancellationToken ct);
    Task<UserSubscriptionDto> CreateSubscriptionAsync(string userId, int maxioCustomerId, int productId, string productHandle, CancellationToken ct);
    Task<List<UserSubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly CatalogContext _catalogContext;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient maxioClient,
        CatalogContext catalogContext,
        string productFamilyHandle)
    {
        _maxioClient = maxioClient;
        _catalogContext = catalogContext;
        _productFamilyHandle = productFamilyHandle;
    }

    public async Task<List<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlanDto>();

        try
        {
            var proPlan = await _maxioClient.Products.ReadProductByHandle(apiHandle: "eshop-pro", ct: ct);
            if (proPlan?.Product != null)
            {
                plans.Add(new SubscriptionPlanDto
                {
                    Id = proPlan.Product.Id.GetValueOrDefault(),
                    Name = proPlan.Product.Name,
                    Handle = proPlan.Product.Handle,
                    Description = proPlan.Product.Description,
                    PriceInCents = proPlan.Product.PriceInCents.GetValueOrDefault(),
                    Interval = proPlan.Product.Interval.GetValueOrDefault(),
                    IntervalUnit = proPlan.Product.IntervalUnit?.ToString() ?? "month"
                });
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw new Exception($"Failed to fetch Pro plan: {ex.Error.StatusCode}", ex);
        }

        try
        {
            var basicPlan = await _maxioClient.Products.ReadProductByHandle(apiHandle: "basic-plan", ct: ct);
            if (basicPlan?.Product != null)
            {
                plans.Add(new SubscriptionPlanDto
                {
                    Id = basicPlan.Product.Id.GetValueOrDefault(),
                    Name = basicPlan.Product.Name,
                    Handle = basicPlan.Product.Handle,
                    Description = basicPlan.Product.Description,
                    PriceInCents = basicPlan.Product.PriceInCents.GetValueOrDefault(),
                    Interval = basicPlan.Product.Interval.GetValueOrDefault(),
                    IntervalUnit = basicPlan.Product.IntervalUnit?.ToString() ?? "month"
                });
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw new Exception($"Failed to fetch Basic plan: {ex.Error.StatusCode}", ex);
        }

        return plans;
    }

    public async Task<int> EnsureCustomerExistsAsync(string userId, string email, string firstName, string lastName, CancellationToken ct)
    {
        try
        {
            var existingCustomer = await _maxioClient.Customers.ReadCustomerByReference(reference: userId, ct: ct);
            if (existingCustomer?.Customer?.Id != null)
            {
                var customerId = existingCustomer.Customer.Id.Value;
                await UpdateOrCreateMappingAsync(userId, customerId, ct);
                return customerId;
            }
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode != HttpStatusCode.NotFound)
            {
                throw new Exception($"Failed to lookup customer: {ex.Error.StatusCode}", ex);
            }
        }

        try
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = userId
                }
            };

            var response = await _maxioClient.Customers.CreateCustomer(body: createRequest, ct: ct);
            if (response?.Customer?.Id != null)
            {
                var customerId = response.Customer.Id.Value;
                await UpdateOrCreateMappingAsync(userId, customerId, ct);
                return customerId;
            }

            throw new Exception("Failed to create customer: no ID in response");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var custError))
            {
                throw new Exception($"Customer validation error: {custError}", ex);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                throw new Exception($"Failed to create customer: {rawError.StatusCode}", ex);
            }
            throw;
        }
    }

    public async Task<UserSubscriptionDto> CreateSubscriptionAsync(string userId, int maxioCustomerId, int productId, string productHandle, CancellationToken ct)
    {
        try
        {
            var createRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = maxioCustomerId,
                    ProductId = productId,
                    Reference = $"{userId}_{productHandle}"
                }
            };

            var response = await _maxioClient.Subscriptions.CreateSubscription(body: createRequest, ct: ct);
            if (response?.Subscription == null)
            {
                throw new Exception("Failed to create subscription: no subscription in response");
            }

            var subscription = response.Subscription;
            var subscriptionId = subscription.Id.GetValueOrDefault();

            var userSub = new UserSubscription
            {
                UserId = userId,
                MaxioSubscriptionId = subscriptionId,
                MaxioProductId = subscription.Product?.Id,
                ProductHandle = productHandle,
                State = subscription.State?.ToString() ?? "unknown",
                BalanceInCents = subscription.BalanceInCents,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
            };

            _catalogContext.UserSubscriptions.Add(userSub);
            await _catalogContext.SaveChangesAsync(ct);

            return new UserSubscriptionDto
            {
                Id = subscriptionId,
                ProductId = subscription.Product?.Id ?? 0,
                ProductHandle = productHandle,
                State = subscription.State?.ToString() ?? "unknown",
                BalanceInCents = subscription.BalanceInCents ?? 0,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errList))
            {
                throw new Exception($"Subscription creation error: {errList}", ex);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                throw new Exception($"Failed to create subscription: {rawError.StatusCode}", ex);
            }
            throw;
        }
    }

    public async Task<List<UserSubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct)
    {
        var userSubs = await _catalogContext.UserSubscriptions
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);

        var result = new List<UserSubscriptionDto>();
        foreach (var userSub in userSubs)
        {
            result.Add(new UserSubscriptionDto
            {
                Id = userSub.MaxioSubscriptionId,
                ProductId = userSub.MaxioProductId ?? 0,
                ProductHandle = userSub.ProductHandle,
                State = userSub.State,
                BalanceInCents = userSub.BalanceInCents ?? 0,
                CurrentPeriodEndsAt = userSub.CurrentPeriodEndsAt
            });
        }

        return result;
    }

    private async Task UpdateOrCreateMappingAsync(string userId, int maxioCustomerId, CancellationToken ct)
    {
        var existing = await _catalogContext.SubscriptionMappings
            .FirstOrDefaultAsync(m => m.UserId == userId, ct);

        if (existing != null)
        {
            existing.MaxioCustomerId = maxioCustomerId;
            _catalogContext.SubscriptionMappings.Update(existing);
        }
        else
        {
            var mapping = new SubscriptionMapping
            {
                UserId = userId,
                MaxioCustomerId = maxioCustomerId
            };
            _catalogContext.SubscriptionMappings.Add(mapping);
        }

        await _catalogContext.SaveChangesAsync(ct);
    }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Handle { get; set; } = null!;
    public string Description { get; set; } = null!;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = null!;
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string State { get; set; } = null!;
    public long BalanceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}
