using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class BillingProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string BillingCycle { get; set; } = string.Empty;
}

public class BillingCustomer
{
    public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}

public class BillingSubscription
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset ActivatedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset NextBillingAt { get; set; }
}

public class BillingComponent
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public decimal PricingSchemePrice { get; set; }
}

public class UsageData
{
    public int Id { get; set; }
    public int SubscriptionId { get; set; }
    public int ComponentId { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public interface IBillingClient
{
    Task<BillingCustomer> CreateOrGetCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);
    Task<BillingCustomer> GetCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    Task<List<BillingProduct>> ListProductsAsync(int productFamilyId, CancellationToken cancellationToken = default);
    Task<BillingProduct> GetProductAsync(int productId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CreateSubscriptionAsync(int customerId, int productId, CancellationToken cancellationToken = default);
    Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<BillingComponent> GetComponentByHandleAsync(int productFamilyId, string componentHandle, CancellationToken cancellationToken = default);

    Task<UsageData> RecordUsageAsync(int subscriptionId, int componentId, decimal quantity, string? memo = null, CancellationToken cancellationToken = default);
    Task<decimal> GetUsageTotalAsync(int subscriptionId, int componentId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> UpdateSubscriptionAsync(int subscriptionId, int newProductId, CancellationToken cancellationToken = default);
    Task<decimal> GetProratedAmountAsync(int subscriptionId, int newProductId, CancellationToken cancellationToken = default);

    Task PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
    Task ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
    Task CancelSubscriptionAsync(int subscriptionId, bool cancelImmediately = false, CancellationToken cancellationToken = default);
    Task ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
