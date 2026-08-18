using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Policy knobs for order notifications.</summary>
public class OrderNotificationOptions
{
    /// <summary>How far in the future the delivery-feedback follow-up is queued with the provider. "A few days"
    /// — kept comfortably inside the provider's scheduling window.</summary>
    public TimeSpan FollowUpDelay { get; set; } = TimeSpan.FromDays(3);
}
