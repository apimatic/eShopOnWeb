using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

/// <summary>
/// Best-effort refresh of the stored delivery outcome from the provider, so a report reflects the message's
/// <em>current</em> state (e.g. delivered / undelivered), not just the status captured at send time. A provider
/// failure while refreshing is swallowed — the last-known status is reported instead.
/// </summary>
internal static class NotificationStatusRefresher
{
    // Outcomes that will not change again — no need to ask the provider.
    private static readonly HashSet<string> Terminal =
        new(StringComparer.OrdinalIgnoreCase) { "delivered", "failed", "undelivered", "canceled", "read" };

    public static async Task RefreshAsync(
        IEnumerable<Notification> notifications,
        ISmsProvider smsProvider,
        IRepository<Notification> repository,
        CancellationToken cancellationToken)
    {
        foreach (var n in notifications)
        {
            if (n.ProviderSid is null)
            {
                continue; // nothing was handed to the provider.
            }
            if (n.DeliveryStatus != null && Terminal.Contains(n.DeliveryStatus))
            {
                continue; // already settled.
            }

            try
            {
                var status = await smsProvider.GetStatusAsync(n.ProviderSid, cancellationToken);
                if (status.Status != n.DeliveryStatus
                    || status.ErrorCode != n.ProviderErrorCode
                    || status.ErrorMessage != n.ProviderErrorMessage)
                {
                    n.UpdateDeliveryStatus(status.Status, status.ErrorCode, status.ErrorMessage);
                    await repository.UpdateAsync(n, cancellationToken);
                }
            }
            catch (SmsProviderException)
            {
                // Reporting must not fail because a status read failed — keep the last-known outcome.
            }
        }
    }
}
