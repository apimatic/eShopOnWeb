namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Tunables for the order notification flow, kept in the core so the orchestrator needs no
/// dependency on the provider integration.</summary>
public class OrderNotificationOptions
{
    /// <summary>How far after dispatch the "how did delivery go?" follow-up is scheduled. A few days by default.</summary>
    public int FollowUpDelayHours { get; set; } = 72;
}
