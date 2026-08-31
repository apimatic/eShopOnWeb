using System;
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
/// Operator action: lines the provider's own record of messages (restricted server-side to
/// this application's configured sending number) up against what eShop believes it sent,
/// over the whole [from, to) range. from/to are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, IRepository<OrderNotification>>
{
    private readonly ISmsProvider _smsProvider;

    public ReconciliationEndpoint(ISmsProvider smsProvider)
    {
        _smsProvider = smsProvider;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IRepository<OrderNotification> notificationRepository) =>
            {
                return await HandleAsync(from, to, notificationRepository);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IRepository<OrderNotification> notificationRepository)
    {
        if (to <= from)
        {
            return Results.BadRequest("'to' must be after 'from'. Both are ISO-8601 date-times.");
        }

        var providerMessages = await _smsProvider.ListMessagesAsync(from, to);
        var ourNotifications = await notificationRepository.ListAsync(new NotificationsInRangeSpecification(from, to));

        var oursBySid = ourNotifications
            .Where(n => n.MessageSid is not null)
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = providerMessages.Select(m => m.MessageSid).ToHashSet();

        var response = new ReconciliationResponse
        {
            From = from,
            To = to,
            ProviderMessageCount = providerMessages.Count,
            EshopNotificationCount = ourNotifications.Count
        };

        foreach (var message in providerMessages)
        {
            if (oursBySid.TryGetValue(message.MessageSid, out var ours))
            {
                response.Matched.Add(new ReconciledNotificationDto
                {
                    NotificationId = ours.Id,
                    MessageSid = message.MessageSid,
                    ProviderStatus = message.Status,
                    RecordedStatus = ours.Status
                });
            }
            else
            {
                response.OnlyAtProvider.Add(new ProviderOnlyMessageDto
                {
                    MessageSid = message.MessageSid,
                    To = message.To,
                    Status = message.Status,
                    DateSent = message.DateSent
                });
            }
        }

        foreach (var ours in ourNotifications.Where(n => n.MessageSid is not null && !providerSids.Contains(n.MessageSid!)))
        {
            response.OnlyInEshop.Add(new EshopOnlyNotificationDto
            {
                NotificationId = ours.Id,
                MessageSid = ours.MessageSid!,
                OrderId = ours.OrderId,
                Status = ours.Status
            });
        }

        return Results.Ok(response);
    }
}
