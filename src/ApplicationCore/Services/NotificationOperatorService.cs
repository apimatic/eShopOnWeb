using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationOperatorService : INotificationOperatorService
{
    private const int MaxReconciliationPages = 50;

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IMessagingProvider _messaging;
    private readonly OrderNotificationPublisher _publisher;
    private readonly IAppLogger<NotificationOperatorService> _logger;

    public NotificationOperatorService(
        IRepository<OrderNotification> notifications,
        IMessagingProvider messaging,
        OrderNotificationPublisher publisher,
        IAppLogger<NotificationOperatorService> logger)
    {
        _notifications = notifications;
        _messaging = messaging;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            throw new NotificationNotFoundException();
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new ResendByIdempotencySpec(notificationId, idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        if (original.ContentDisposed || string.IsNullOrEmpty(original.Body))
        {
            throw new InvalidOrderStateException("The original message content is no longer available to resend.");
        }

        var destination = original.DestinationNumber;
        ProviderMessage? sent = null;
        string? failure = null;
        try
        {
            sent = await _messaging.SendAsync(destination, original.Body, cancellationToken);
        }
        catch (Exception ex)
        {
            failure = ex is MessagingProviderException mpe ? mpe.Message : "The messaging provider call did not complete.";
            _logger.LogWarning("Resend of notification {NotificationId} failed: {Reason}", notificationId, failure);
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            NotificationKinds.Resend,
            destination,
            original.Body,
            sent?.Sid,
            sent?.Status,
            sent?.ErrorCode,
            sent?.ErrorMessage,
            sendAt: null,
            sourceNotificationId: original.Id,
            idempotencyKey: idempotencyKey,
            sendFailure: failure);

        await _notifications.AddAsync(resend, cancellationToken);
        return resend;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new NotificationNotFoundException();
        }

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            try
            {
                var redacted = await _messaging.RedactBodyAsync(notification.ProviderSid, cancellationToken);
                var fetched = await _messaging.FetchAsync(notification.ProviderSid, cancellationToken);
                if (!string.IsNullOrEmpty(fetched.Body))
                {
                    _logger.LogWarning("Provider still returned message text after dispose for notification {NotificationId}.", notificationId);
                }

                notification.ApplyProviderState(fetched.Sid ?? redacted.Sid, fetched.Status ?? redacted.Status, fetched.ErrorCode, fetched.ErrorMessage, body: null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Provider dispose failed for notification {NotificationId}: {Reason}", notificationId,
                    ex is MessagingProviderException mpe ? mpe.Message : "The messaging provider call did not complete.");
                throw;
            }
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new InvalidOrderStateException("The reconciliation range is invalid.");
        }

        var providerMessages = await ListAllProviderMessagesAsync(from, to, cancellationToken);
        var local = await _notifications.ListAsync(new NotificationsCreatedInRangeSpec(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerSids = new HashSet<string>(StringComparer.Ordinal);
        var matched = new List<ReconciliationRow>();
        var providerOnly = new List<ReconciliationRow>();

        foreach (var message in providerMessages)
        {
            if (string.IsNullOrEmpty(message.Sid))
            {
                continue;
            }

            providerSids.Add(message.Sid);
            if (localBySid.TryGetValue(message.Sid, out var localRow))
            {
                matched.Add(new ReconciliationRow(
                    message.Sid,
                    localRow.Id,
                    localRow.Kind,
                    message.Status,
                    localRow.ProviderStatus,
                    message.DateSent,
                    "matched"));
            }
            else
            {
                providerOnly.Add(new ReconciliationRow(
                    message.Sid,
                    null,
                    null,
                    message.Status,
                    null,
                    message.DateSent,
                    "providerOnly"));
            }
        }

        var applicationOnly = local
            .Where(n => string.IsNullOrEmpty(n.ProviderSid) || !providerSids.Contains(n.ProviderSid))
            .Select(n => new ReconciliationRow(
                n.ProviderSid,
                n.Id,
                n.Kind,
                null,
                n.ProviderStatus ?? n.SendFailure,
                null,
                "applicationOnly"))
            .ToList();

        return new ReconciliationReport(from, to, matched, providerOnly, applicationOnly);
    }

    private async Task<List<ProviderMessage>> ListAllProviderMessagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var all = new List<ProviderMessage>();
        string? pageToken = null;
        string? previousUri = null;
        var pages = 0;

        while (pages < MaxReconciliationPages)
        {
            var page = await _messaging.ListSentFromConfiguredNumberAsync(
                from, to, pageSize: 1000, page: pages, pageToken: pageToken, cancellationToken);
            if (page.Messages.Count > 0)
            {
                all.AddRange(page.Messages);
            }

            pages++;
            if (string.IsNullOrEmpty(page.NextPageUri) || string.Equals(page.NextPageUri, previousUri, StringComparison.Ordinal))
            {
                break;
            }

            previousUri = page.NextPageUri;
            pageToken = PageTokenFrom(page.NextPageUri);
            if (string.IsNullOrEmpty(pageToken) && page.Messages.Count == 0)
            {
                break;
            }
        }

        if (pages >= MaxReconciliationPages)
        {
            _logger.LogWarning("Reconciliation stopped after {PageCap} provider pages.", MaxReconciliationPages);
        }

        return all;
    }

    private static string? PageTokenFrom(string nextPageUri)
    {
        var relative = nextPageUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? new Uri(nextPageUri)
            : new Uri("https://placeholder.local" + (nextPageUri.StartsWith('/') ? nextPageUri : "/" + nextPageUri));

        var query = relative.Query.TrimStart('?');
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0].Equals("PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return null;
    }
}
