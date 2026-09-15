using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// Lines the provider's own record of messages (for the configured sending number, over a date range)
/// up against what eShop believes it sent, so a message one side knows about and the other doesn't is
/// visible from either direction.
/// </summary>
public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ProviderMessage> ProviderOnly,
    IReadOnlyList<OrderNotification> EShopOnly);

/// <summary>A message present on both sides, matched by the provider's message identifier.</summary>
public sealed record ReconciliationMatch(OrderNotification Notification, ProviderMessage ProviderMessage);
