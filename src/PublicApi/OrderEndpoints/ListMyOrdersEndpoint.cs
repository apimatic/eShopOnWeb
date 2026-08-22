using System.Linq;
using System.Security.Claims;
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

public class ListMyOrdersEndpoint : IEndpoint<IResult, EmptyRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderNotificationService service, ClaimsPrincipal user) =>
            {
                var unauthorized = HttpCaller.RequireBuyerId(user, out var buyerId);
                if (unauthorized is not null)
                {
                    return unauthorized;
                }

                return await HandleAsync(new EmptyRequest(), service, buyerId);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest request, IOrderNotificationService service)
        => HandleAsync(request, service, string.Empty);

    private async Task<IResult> HandleAsync(EmptyRequest request, IOrderNotificationService service, string buyerId)
    {
        var response = new ListMyOrdersResponse(request.CorrelationId());
        var summaries = await service.GetMyOrdersAsync(buyerId, default);
        response.Orders.AddRange(summaries.Select(summary => new MyOrderDto
        {
            OrderId = summary.Order.Id,
            Status = summary.Order.Status.ToString(),
            OrderDate = summary.Order.OrderDate,
            Total = summary.Order.Total(),
            Notifications = summary.Notifications.Select(MapNotification).ToList()
        }));
        return Results.Ok(response);
    }

    internal static NotificationDto MapNotification(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            Body = notification.Body,
            BodyRedacted = notification.BodyRedacted,
            ProviderMessageSid = notification.ProviderMessageSid,
            DeliveryStatus = notification.DeliveryStatus,
            ErrorCode = notification.ErrorCode,
            ErrorMessage = notification.ErrorMessage,
            CreatedAt = notification.CreatedAt,
            ScheduledFor = notification.ScheduledFor
        };
    }
}
