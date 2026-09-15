using System;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// How far in the future the "how was delivery?" follow-up is queued with the provider when an order is
/// dispatched. Populated from configuration at start-up.
/// </summary>
public class NotificationSchedulingSettings
{
    public TimeSpan FollowUpDelay { get; set; } = TimeSpan.FromDays(3);
}
