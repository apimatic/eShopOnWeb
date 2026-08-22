using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ShopOrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IShopOrderService _orders;
    private readonly IOrderNotificationService _notifications;

    public ListMyOrdersEndpoint(IShopOrderService orders, IOrderNotificationService notifications)
    {
        _orders = orders;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(user);
            })
            .Produces<ListMyOrdersResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("ShopOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var unauthorized = EndpointIdentity.RequireBuyer(user, out var buyerId);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var orders = await _orders.ListForBuyerAsync(buyerId, default);
        var response = new ListMyOrdersResponse();
        foreach (var order in orders)
        {
            var notes = await _notifications.ListForOrderAsync(order.Id, default);
            response.Orders.Add(new ShopOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Items = order.OrderItems.Select(i => new ShopOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Notifications = notes.Select(ToDto).ToList()
            });
        }

        return Results.Ok(response);
    }

    internal static OrderNotificationDto ToDto(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        Kind = notification.Kind.ToString(),
        ProviderSid = notification.ProviderSid,
        Status = notification.ProviderStatus,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        Body = notification.ContentRedacted ? null : notification.Body,
        ContentRedacted = notification.ContentRedacted,
        CreatedAt = notification.CreatedAt,
        ScheduledFor = notification.ScheduledFor
    };
}
