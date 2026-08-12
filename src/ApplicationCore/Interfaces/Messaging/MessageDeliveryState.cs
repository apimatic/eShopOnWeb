using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

/// <summary>The provider's current view of a message's delivery outcome.</summary>
/// <param name="Status">Current delivery outcome.</param>
/// <param name="ErrorCode">Provider error code, when one is available.</param>
public record MessageDeliveryState(NotificationDeliveryStatus Status, int? ErrorCode);
