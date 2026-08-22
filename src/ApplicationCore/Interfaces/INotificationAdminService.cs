using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Twilio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<TwilioMessage> ProviderOnly,
    IReadOnlyList<OrderNotification> ApplicationOnly);

public record ReconciliationMatch(OrderNotification Notification, TwilioMessage ProviderMessage);

public interface INotificationAdminService
{
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
