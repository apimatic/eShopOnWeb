using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Tunables for the notification flow that are not provider credentials. Kept as an interface in the
/// core so the application service does not depend on the hosting/config stack.
/// </summary>
public interface INotificationOptions
{
    /// <summary>How long after dispatch the "how did delivery go?" follow-up should be sent.</summary>
    TimeSpan DeliveryFollowUpDelay { get; }
}
