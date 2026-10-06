using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The state of a user's subscription in the billing system of record.
/// </summary>
public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CreatedAt { get; set; }

    public static SubscriptionDto FromDetails(SubscriptionDetails details) =>
        new SubscriptionDto
        {
            SubscriptionId = details.SubscriptionId,
            PlanHandle = details.PlanHandle,
            PlanName = details.PlanName,
            Price = details.Price,
            State = details.State,
            NextBillingDate = details.NextBillingDate,
            CreatedAt = details.CreatedAt
        };
}