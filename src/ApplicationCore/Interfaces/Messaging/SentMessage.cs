using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

/// <summary>
/// Outcome of handing a message to the provider: its identifier and the delivery outcome it
/// reported at that moment.
/// </summary>
/// <param name="ProviderMessageSid">The provider's identifier for the message.</param>
/// <param name="Status">The delivery outcome reported by the provider.</param>
/// <param name="ErrorCode">Provider error code, when one was reported.</param>
public record SentMessage(string ProviderMessageSid, NotificationDeliveryStatus Status, int? ErrorCode);
