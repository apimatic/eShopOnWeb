using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class SubscriptionDto
{
    public int Id { get; set; }
    public int BillingProviderId { get; set; }
    public string BillingProviderSubscriptionHandle { get; set; } = null!;
    public string ProductHandle { get; set; } = null!;
    public int ProductId { get; set; }
    public decimal CurrentPrice { get; set; }
    public string State { get; set; } = null!;
    public DateTime NextBillingDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public interface ISubscriptionService
{
    Task<List<BillingProduct>> ListAvailableProductsAsync(CancellationToken cancellationToken = default);
    Task<SubscriptionDto> SubscribeAsync(string userId, string userEmail, int productId, CancellationToken cancellationToken = default);
    Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
    Task<SubscriptionDto> GetSubscriptionAsync(string userId, int subscriptionId, CancellationToken cancellationToken = default);
    Task RecordUsageAsync(string userId, int subscriptionId, int componentId, int quantity, string? memo = null, CancellationToken cancellationToken = default);
    Task<decimal> GetUsageAsync(string userId, int subscriptionId, int componentId, CancellationToken cancellationToken = default);
    Task<PlanChangePreview> PreviewPlanChangeAsync(string userId, int subscriptionId, int newProductId, bool prorationOnChange, CancellationToken cancellationToken = default);
    Task ChangePlanAsync(string userId, int subscriptionId, int newProductId, bool prorationOnChange, CancellationToken cancellationToken = default);
    Task PauseSubscriptionAsync(string userId, int subscriptionId, CancellationToken cancellationToken = default);
    Task ResumeSubscriptionAsync(string userId, int subscriptionId, CancellationToken cancellationToken = default);
    Task CancelSubscriptionAsync(string userId, int subscriptionId, bool immediate = false, CancellationToken cancellationToken = default);
    Task ReactivateSubscriptionAsync(string userId, int subscriptionId, CancellationToken cancellationToken = default);
}
