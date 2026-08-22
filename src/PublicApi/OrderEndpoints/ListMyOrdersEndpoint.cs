using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderNotificationService service, HttpContext http) =>
            {
                var buyerId = http.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var result = await service.GetMyOrdersAsync(buyerId, http.RequestAborted);
                var byOrder = result.Notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());
                var response = new ListMyOrdersResponse
                {
                    Orders = result.Orders.Select(order => new MyOrderDto
                    {
                        OrderId = order.Id,
                        Status = order.Status.ToString(),
                        OrderDate = order.OrderDate,
                        Total = order.Total(),
                        Notifications = byOrder.TryGetValue(order.Id, out var notes)
                            ? notes.Select(Map).ToList()
                            : new()
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service)
    {
        return Task.FromResult<IResult>(Results.Ok());
    }

    internal static NotificationDto Map(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            ProviderSid = notification.ProviderMessageSid,
            Status = notification.ProviderStatus,
            ErrorCode = notification.ProviderErrorCode,
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            ScheduledSendAt = notification.ScheduledSendAt,
            CreatedAt = notification.CreatedAt
        };
    }
}
