using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: lines up the provider's own record of messages sent from
/// this application's configured sending number over a date range against what
/// eShop believes it sent, so a discrepancy in either direction is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest>
{
    private readonly ISmsProvider _smsProvider;
    private readonly IRepository<OrderNotification> _notificationRepository;

    public ReconciliationEndpoint(ISmsProvider smsProvider, IRepository<OrderNotification> notificationRepository)
    {
        _smsProvider = smsProvider;
        _notificationRepository = notificationRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, cancellationToken);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request)
        => HandleAsync(request, default);

    private async Task<IResult> HandleAsync(ReconciliationRequest request, CancellationToken cancellationToken)
    {
        if (!DateTimeOffset.TryParse(request.From, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var from)
            || !DateTimeOffset.TryParse(request.To, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var to))
        {
            return Results.BadRequest(new { error = "Both 'from' and 'to' are required and must be ISO-8601 date-times." });
        }

        if (to < from)
        {
            return Results.BadRequest(new { error = "'to' must not be earlier than 'from'." });
        }

        var providerMessages = await _smsProvider.ListMessagesAsync(from, to, cancellationToken);
        var localNotifications = await _notificationRepository.ListAsync(
            new NotificationsCreatedBetweenSpecification(from, to), cancellationToken);

        var entries = new List<ReconciliationEntry>();
        var matchedNotificationIds = new HashSet<int>();

        foreach (var message in providerMessages)
        {
            var local = localNotifications.FirstOrDefault(n => n.ProviderMessageSid == message.MessageSid);
            if (local is not null)
            {
                matchedNotificationIds.Add(local.Id);
            }

            entries.Add(new ReconciliationEntry
            {
                ProviderMessageSid = message.MessageSid,
                MaskedTo = PhoneNumberMask.Mask(message.To),
                ProviderStatus = message.Status,
                ProviderDateSent = message.DateSent,
                NotificationId = local?.Id,
                LocalStatus = local?.Status,
                Match = local is not null ? "matched" : "providerOnly"
            });
        }

        foreach (var notification in localNotifications.Where(n => !matchedNotificationIds.Contains(n.Id)))
        {
            string match;
            string? note = null;
            string? providerStatus = null;

            if (notification.ProviderMessageSid is null)
            {
                match = "localOnly";
                note = "Never accepted by the provider (send failed).";
            }
            else
            {
                // The provider's date-sent filtered list does not cover messages it still
                // holds (scheduled messages have no DateSent); ask about this one directly.
                var details = await _smsProvider.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
                if (details is not null)
                {
                    match = "matched";
                    providerStatus = details.Status;
                    note = "Known to the provider but outside the date-sent range (e.g. still scheduled).";
                }
                else
                {
                    match = "localOnly";
                    note = "Unknown to the provider.";
                }
            }

            entries.Add(new ReconciliationEntry
            {
                ProviderMessageSid = notification.ProviderMessageSid,
                MaskedTo = PhoneNumberMask.Mask(notification.ToNumber),
                ProviderStatus = providerStatus,
                NotificationId = notification.Id,
                LocalStatus = notification.Status,
                Match = match,
                Note = note
            });
        }

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = from,
            To = to,
            ProviderMessageCount = providerMessages.Count,
            LocalNotificationCount = localNotifications.Count,
            MatchedCount = entries.Count(e => e.Match == "matched"),
            ProviderOnlyCount = entries.Count(e => e.Match == "providerOnly"),
            LocalOnlyCount = entries.Count(e => e.Match == "localOnly"),
            Entries = entries
                .OrderBy(e => e.ProviderDateSent ?? DateTimeOffset.MaxValue)
                .ThenBy(e => e.NotificationId)
                .ToList()
        };

        return Results.Ok(response);
    }
}

public class ReconciliationRequest : BaseRequest
{
    /// <summary>Start of the range, ISO-8601 date-time.</summary>
    public string? From { get; set; }

    /// <summary>End of the range, ISO-8601 date-time.</summary>
    public string? To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) {}
    public ReconciliationResponse() {}

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int LocalNotificationCount { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int LocalOnlyCount { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new();
}

public class ReconciliationEntry
{
    public string? ProviderMessageSid { get; set; }

    /// <summary>Destination number, masked to its last digits.</summary>
    public string MaskedTo { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public int? NotificationId { get; set; }
    public string? LocalStatus { get; set; }

    /// <summary>"matched", "providerOnly" (provider knows it, eShop doesn't) or "localOnly" (the reverse).</summary>
    public string Match { get; set; } = string.Empty;
    public string? Note { get; set; }
}
