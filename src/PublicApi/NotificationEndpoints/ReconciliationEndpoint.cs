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
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: lines up the provider's own record of messages for a date range
/// against what eShop believes it sent. Only messages sent from this application's
/// configured sending number are asked for, so other traffic on the account is excluded.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, HttpContext httpContext) =>
            {
                return await HandleAsync(from, to, httpContext);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, HttpContext httpContext)
    {
        var smsProvider = httpContext.RequestServices.GetRequiredService<ISmsProvider>();
        var notificationRepository = httpContext.RequestServices.GetRequiredService<IReadRepository<OrderNotification>>();

        if (to < from)
        {
            return Results.BadRequest("'to' must not be earlier than 'from'.");
        }

        var providerMessages = await smsProvider.ListMessagesAsync(from, to, httpContext.RequestAborted);
        var localNotifications = await notificationRepository.ListAsync(
            new NotificationsInRangeSpecification(from, to), httpContext.RequestAborted);

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = providerMessages.Select(m => m.Sid).ToHashSet();

        var response = new ReconciliationResponse
        {
            From = from,
            To = to,
            ProviderMessageCount = providerMessages.Count,
            LocalNotificationCount = localNotifications.Count
        };

        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.Sid, out var local))
            {
                response.Matched.Add(new ReconciliationMatch
                {
                    ProviderMessageSid = message.Sid,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    ProviderStatus = message.Status,
                    LocalStatus = local.ProviderStatus
                });
            }
            else
            {
                // The provider knows about this message; eShop does not.
                response.ProviderOnly.Add(new ReconciliationProviderMessage
                {
                    ProviderMessageSid = message.Sid,
                    Status = message.Status,
                    DateSent = message.DateSent
                });
            }
        }

        foreach (var notification in localNotifications)
        {
            if (notification.ProviderMessageSid is null)
            {
                // Never accepted by the provider, so it cannot appear in its records.
                response.NotAcceptedByProvider.Add(notification.Id);
            }
            else if (!providerSids.Contains(notification.ProviderMessageSid))
            {
                // eShop believes it sent this; the provider has no record of it.
                response.LocalOnly.Add(new ReconciliationLocalNotification
                {
                    NotificationId = notification.Id,
                    OrderId = notification.OrderId,
                    ProviderMessageSid = notification.ProviderMessageSid,
                    LocalStatus = notification.ProviderStatus
                });
            }
        }

        return Results.Ok(response);
    }
}
