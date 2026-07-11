using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IBillingClient
{
    Task<BillingCustomer?> GetOrCreateCustomerAsync(string userEmail);
    Task<BillingSubscription> CreateSubscriptionAsync(int customerId, int productId);
    Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId);
    Task<List<BillingProduct>> ListProductsAsync(int productFamilyId);
    Task<BillingProduct> GetProductAsync(int productId);
    Task<BillingComponent?> GetComponentByHandleAsync(int productFamilyId, string componentHandle);
    Task RecordUsageAsync(int subscriptionId, int componentId, decimal quantity, string? memo = null);
    Task<UsageData> GetUsageAsync(int subscriptionId, int componentId);
    Task<ChangeSubscriptionPlanPreview> PreviewPlanChangeAsync(int subscriptionId, int newProductId);
    Task<BillingSubscription> ChangeSubscriptionPlanAsync(int subscriptionId, int newProductId);
    Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId);
    Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId);
    Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, bool atEndOfPeriod = false);
    Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId);
}

public class BillingCustomer
{
    public int Id { get; set; }
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
    public decimal? CurrentPeriodEndsAt { get; set; }
    public decimal? NextBillingAt { get; set; }
}

public class BillingProduct
{
    public int Id { get; set; }
    public int FamilyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string PricingScheme { get; set; } = string.Empty;
}

public class BillingComponent
{
    public int Id { get; set; }
    public int ProductFamilyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class UsageData
{
    public int Id { get; set; }
    public decimal CurrentUsage { get; set; }
    public decimal UnitPrice { get; set; }
}

public class ChangeSubscriptionPlanPreview
{
    public decimal HighestChargeInTermsOfStatusAmount { get; set; }
    public decimal LowestChargeInTermsOfStatusAmount { get; set; }
    public decimal AccruedProrationAdjustmentAmount { get; set; }
}
