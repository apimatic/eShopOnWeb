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
            (HttpContext httpContext, IOrderNotificationService service) =>
            {
                var unauthorized = httpContext.UnauthorizedIfAnonymous();
                if (unauthorized is not null) return unauthorized;
                return await HandleAsync(service, httpContext.GetBuyerId()!);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service) => HandleAsync(service, string.Empty);

    private async Task<IResult> HandleAsync(IOrderNotificationService service, string buyerId)
    {
        var orders = await service.ListBuyerOrdersAsync(buyerId, default);
        var response = new ListMyOrdersResponse();
        foreach (var order in orders)
        {
            var notifications = await service.ListOrderNotificationsAsync(order.Id, buyerId, isAdministrator: false, default);
            response.Orders.Add(new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status,
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Items = order.OrderItems.Select(i => new OrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Notifications = notifications.Select(MapNotification).ToList()
            });
        }

        return Results.Ok(response);
    }

    internal static NotificationDto MapNotification(OrderNotification n) =>
        new()
        {
            NotificationId = n.Id,
            Kind = n.Kind,
            Status = n.Status,
            ProviderSid = n.ProviderSid,
            ErrorCode = n.ErrorCode,
            ErrorMessage = n.ErrorMessage,
            Body = n.ContentDisposed ? null : n.Body,
            ContentDisposed = n.ContentDisposed,
            CreatedUtc = n.CreatedUtc,
            ScheduledForUtc = n.ScheduledForUtc,
            ResendOfNotificationId = n.ResendOfNotificationId
        };
}
