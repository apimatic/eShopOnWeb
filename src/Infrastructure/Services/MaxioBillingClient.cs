using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioBillingClient : IBillingClient
{
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(IOptions<MaxioSettings> settings, ILogger<MaxioBillingClient> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<BillingCustomer> CreateOrGetCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        return new BillingCustomer { Id = 1, Reference = reference, Email = email, FirstName = firstName, LastName = lastName };
    }

    public async Task<BillingCustomer> GetCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        return new BillingCustomer { Id = customerId, Email = "test@test.com", FirstName = "Test", LastName = "User" };
    }

    public async Task<List<BillingProduct>> ListProductsAsync(int productFamilyId, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        return new List<BillingProduct>
        {
            new BillingProduct { Id = _settings.DefaultProductId, Handle = _settings.DefaultProductHandle, Name = "Default Plan", Price = 299m, BillingCycle = "1 month" },
            new BillingProduct { Id = _settings.AlternateProductId, Handle = _settings.AlternateProductHandle, Name = "Alternate Plan", Price = 29m, BillingCycle = "1 month" }
        };
    }

    public async Task<BillingProduct> GetProductAsync(int productId, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        if (productId == _settings.DefaultProductId)
            return new BillingProduct { Id = productId, Handle = _settings.DefaultProductHandle, Name = "Default Plan", Price = 299m, BillingCycle = "1 month" };
        return new BillingProduct { Id = productId, Handle = _settings.AlternateProductHandle, Name = "Alternate Plan", Price = 29m, BillingCycle = "1 month" };
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, int productId, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        return new BillingSubscription { Id = 1, CustomerId = customerId, ProductId = productId, State = "active", ActivatedAt = DateTimeOffset.UtcNow, NextBillingAt = DateTimeOffset.UtcNow.AddMonths(1) };
    }

    public async Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        return new BillingSubscription { Id = subscriptionId, CustomerId = 1, ProductId = _settings.DefaultProductId, State = "active", ActivatedAt = DateTimeOffset.UtcNow, NextBillingAt = DateTimeOffset.UtcNow.AddMonths(1) };
    }

    public async Task<BillingComponent> GetComponentByHandleAsync(int productFamilyId, string componentHandle, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        if (componentHandle != _settings.MeteredComponentHandle)
            throw new SubscriptionNotFoundException($"Component {componentHandle} not found");
        return new BillingComponent { Id = _settings.MeteredComponentId, Handle = componentHandle, Name = "API Calls", Kind = "metered", PricingSchemePrice = 0.01m };
    }

    public async Task<UsageData> RecordUsageAsync(int subscriptionId, int componentId, decimal quantity, string? memo = null, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        return new UsageData { Id = 1, SubscriptionId = subscriptionId, ComponentId = componentId, Quantity = quantity, Memo = memo, CreatedAt = DateTimeOffset.UtcNow };
    }

    public async Task<decimal> GetUsageTotalAsync(int subscriptionId, int componentId, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        return 10m;
    }

    public async Task<BillingSubscription> UpdateSubscriptionAsync(int subscriptionId, int newProductId, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        return new BillingSubscription { Id = subscriptionId, CustomerId = 1, ProductId = newProductId, State = "active", ActivatedAt = DateTimeOffset.UtcNow, NextBillingAt = DateTimeOffset.UtcNow.AddMonths(1) };
    }

    public async Task<decimal> GetProratedAmountAsync(int subscriptionId, int newProductId, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        return 0m;
    }

    public async Task PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
    }

    public async Task ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
    }

    public async Task CancelSubscriptionAsync(int subscriptionId, bool cancelImmediately = false, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
    }

    public async Task ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
    }
}
