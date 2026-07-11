using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class BillingProduct
{
    public int Id { get; set; }
    public string Handle { get; set; } = null!;
    public string Name { get; set; } = null!;
    public decimal Price { get; set; }
    public string BillingInterval { get; set; } = null!;
    public bool RequiresPaymentMethod { get; set; }
}

public class BillingCustomer
{
    public int Id { get; set; }
    public string Reference { get; set; } = null!;
    public string Email { get; set; } = null!;
}

public class BillingSubscription
{
    public int Id { get; set; }
    public string Handle { get; set; } = null!;
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string State { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? PauseDate { get; set; }
    public DateTime? ResumeDate { get; set; }
    public DateTime? PendingCancelDate { get; set; }
    public DateTime NextBillingDate { get; set; }
    public decimal CurrentPrice { get; set; }
}

public class UsageRecordResult
{
    public bool Success { get; set; }
    public decimal PeriodToDateTotal { get; set; }
    public string ErrorMessage { get; set; } = null!;
}

public class PlanChangePreview
{
    public decimal ProrationCharge { get; set; }
    public decimal NewProductPrice { get; set; }
    public DateTime EffectiveDate { get; set; }
}

public interface IBillingClient
{
    Task<List<BillingProduct>> ListProductsAsync(int productFamilyId, CancellationToken cancellationToken = default);
    Task<BillingCustomer?> GetOrCreateCustomerAsync(string userReference, string email, CancellationToken cancellationToken = default);
    Task<BillingSubscription?> GetSubscriptionByCustomerAndProductAsync(int customerId, int productId, CancellationToken cancellationToken = default);
    Task<BillingSubscription> CreateSubscriptionAsync(int customerId, int productId, CancellationToken cancellationToken = default);
    Task RecordUsageAsync(int subscriptionId, int componentId, int quantity, string? memo = null, CancellationToken cancellationToken = default);
    Task<UsageRecordResult> GetUsageAsync(int subscriptionId, int componentId, CancellationToken cancellationToken = default);
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, int newProductId, bool prorationOnChange, CancellationToken cancellationToken = default);
    Task ChangePlanAsync(int subscriptionId, int newProductId, bool prorationOnChange, CancellationToken cancellationToken = default);
    Task PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
    Task ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
    Task CancelSubscriptionAsync(int subscriptionId, bool immediate = false, CancellationToken cancellationToken = default);
    Task ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
    Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
    Task ValidateComponentIsMeteredAsync(int productFamilyId, int componentId, CancellationToken cancellationToken = default);
}
