using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

/// <summary>
/// The provider's own record of one message, as returned when listing messages for reconciliation.
/// </summary>
/// <param name="ProviderMessageSid">The provider's identifier for the message.</param>
/// <param name="Status">Delivery outcome as the provider records it.</param>
/// <param name="DateSent">When the provider sent it, when known.</param>
/// <param name="ErrorCode">Provider error code, when one is recorded.</param>
public record ProviderMessage(
    string ProviderMessageSid,
    NotificationDeliveryStatus Status,
    DateTimeOffset? DateSent,
    int? ErrorCode);
