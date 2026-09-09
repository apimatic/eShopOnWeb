using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Local record linking an eShopOnWeb user to a subscription that is managed
/// by the external billing system (Maxio Advanced Billing), which remains the
/// system of record for subscription state.
/// </summary>
public class SubscriptionRecord : BaseEntity, IAggregateRoot
{
    /// <summary>
    /// ASP.NET Identity user id; used as the stable reference for the Maxio customer.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public long MaxioCustomerId { get; set; }

    public long MaxioSubscriptionId { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string? Currency { get; set; }

    public decimal Price { get; set; }

    public DateTime? NextBillingDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
