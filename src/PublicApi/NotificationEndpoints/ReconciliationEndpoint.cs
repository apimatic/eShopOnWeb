using System;
using System.Collections.Generic;
using System.Linq;
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
/// Operator action: lines up the provider's own record of messages for a date range against
/// what eShop believes it sent. The provider is asked only for messages from this
/// application's configured sending number.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset>
{
    private readonly ISmsMessagingClient _messagingClient;
    private readonly IRepository<OrderNotification> _notificationRepository;

    public ReconciliationEndpoint(ISmsMessagingClient messagingClient,
        IRepository<OrderNotification> notificationRepository)
    {
        _messagingClient = messagingClient;
        _notificationRepository = notificationRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) =>
            {
                return await HandleAsync(from, to);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (from == default || to == default || from > to)
        {
            return Results.BadRequest(new ReconciliationResponse
            {
                Message = "Both 'from' and 'to' are required ISO-8601 date-times, and 'from' must not be after 'to'."
            });
        }

        var providerMessages = await _messagingClient.ListMessagesAsync(from, to);
        var localNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsInRangeSpecification(from, to));

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var entries = new List<ReconciliationEntry>();
        foreach (var message in providerMessages)
        {
            var known = localBySid.TryGetValue(message.ProviderMessageSid, out var local);
            entries.Add(new ReconciliationEntry
            {
                ProviderMessageSid = message.ProviderMessageSid,
                ProviderStatus = message.Status,
                DateSent = message.DateSent,
                NotificationId = known ? local!.Id : null,
                LocalStatus = known ? local!.Status : null,
                Match = known ? "matched" : "missingLocally"
            });
        }

        var providerSids = new HashSet<string>(providerMessages.Select(m => m.ProviderMessageSid));
        foreach (var local in localNotifications)
        {
            if (local.ProviderMessageSid is null || !providerSids.Contains(local.ProviderMessageSid))
            {
                entries.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = local.ProviderMessageSid,
                    NotificationId = local.Id,
                    LocalStatus = local.Status,
                    Match = "missingAtProvider"
                });
            }
        }

        var response = new ReconciliationResponse
        {
            From = from,
            To = to,
            ProviderMessageCount = providerMessages.Count,
            LocalNotificationCount = localNotifications.Count,
            MatchedCount = entries.Count(e => e.Match == "matched"),
            Entries = entries.OrderBy(e => e.DateSent ?? DateTimeOffset.MinValue).ToList()
        };
        return Results.Ok(response);
    }
}
