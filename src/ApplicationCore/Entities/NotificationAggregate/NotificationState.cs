namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// This application's own view of a message's fate, independent of (and coarser than) the provider's
/// delivery status. A message that could not even be handed to the provider is <see cref="Failed"/>;
/// one that was never attempted (no contact on file) is <see cref="Suppressed"/>.
/// </summary>
public enum NotificationState
{
    /// <summary>Nothing was sent — e.g. the shopper had no number on file.</summary>
    Suppressed = 0,
    /// <summary>The provider rejected or never received the send attempt; no message id was obtained.</summary>
    Failed = 1,
    /// <summary>The provider accepted the message; a provider message id and delivery status are on file.</summary>
    Sent = 2
}
