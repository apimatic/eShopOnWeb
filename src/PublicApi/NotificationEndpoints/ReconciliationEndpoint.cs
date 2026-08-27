using System;
using System.Collections.Generic;
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

public class ReconciliationEntryDto
{
    /// <summary>Matched, ProviderOnly or EshopOnly.</summary>
    public string Match { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EshopStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int EshopNotificationCount { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EshopOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

/// <summary>
/// Operator action: lines up the provider's own record of messages sent from this
/// application's configured sending number against what eShop believes it sent, over a date
/// range. Messages the provider knows about and eShop doesn't — and the reverse — are visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, IRepository<OrderNotification>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IRepository<OrderNotification> notificationRepository,
                ISmsGateway smsGateway, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(from, to, notificationRepository, smsGateway, cancellationToken);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IRepository<OrderNotification> notificationRepository)
        => throw new NotSupportedException("Use the routed overload with the date range.");

    private async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to,
        IRepository<OrderNotification> notificationRepository, ISmsGateway smsGateway, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            return Results.BadRequest(new ReconciliationResponse());
        }

        var providerMessages = await smsGateway.ListMessagesAsync(from, to, cancellationToken);
        var eshopNotifications = await notificationRepository.ListAsync(
            new NotificationsCreatedInRangeSpecification(from, to), cancellationToken);

        var eshopBySid = eshopNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var entries = new List<ReconciliationEntryDto>();
        var matchedNotificationIds = new HashSet<int>();

        foreach (var message in providerMessages)
        {
            if (eshopBySid.TryGetValue(message.MessageSid, out var notification))
            {
                matchedNotificationIds.Add(notification.Id);
                entries.Add(new ReconciliationEntryDto
                {
                    Match = "Matched",
                    ProviderMessageSid = message.MessageSid,
                    NotificationId = notification.Id,
                    ProviderStatus = message.Status,
                    EshopStatus = notification.Status,
                    DateSent = message.DateSent ?? message.DateCreated
                });
            }
            else
            {
                entries.Add(new ReconciliationEntryDto
                {
                    Match = "ProviderOnly",
                    ProviderMessageSid = message.MessageSid,
                    ProviderStatus = message.Status,
                    DateSent = message.DateSent ?? message.DateCreated
                });
            }
        }

        foreach (var notification in eshopNotifications.Where(n => !matchedNotificationIds.Contains(n.Id)))
        {
            entries.Add(new ReconciliationEntryDto
            {
                Match = "EshopOnly",
                ProviderMessageSid = notification.ProviderMessageSid,
                NotificationId = notification.Id,
                EshopStatus = notification.Status,
                DateSent = notification.CreatedAt
            });
        }

        var response = new ReconciliationResponse
        {
            From = from,
            To = to,
            ProviderMessageCount = providerMessages.Count,
            EshopNotificationCount = eshopNotifications.Count,
            MatchedCount = matchedNotificationIds.Count,
            ProviderOnlyCount = entries.Count(e => e.Match == "ProviderOnly"),
            EshopOnlyCount = entries.Count(e => e.Match == "EshopOnly"),
            Entries = entries.OrderBy(e => e.DateSent).ToList()
        };
        return Results.Ok(response);
    }
}
