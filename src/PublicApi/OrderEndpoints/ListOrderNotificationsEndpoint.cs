using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IShopperOrderService orders, IOrderNotificationService notifications, HttpContext http) =>
            {
                return await HandleAsync(orderId, orders, notifications, http);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IShopperOrderService orders)
    {
        throw new System.NotSupportedException("Use the routed handler that supplies the current request services.");
    }

    private static async Task<IResult> HandleAsync(int orderId, IShopperOrderService orders, IOrderNotificationService notifications, HttpContext http)
    {
        var user = http.User;
        var order = await orders.GetByIdAsync(orderId);
        if (order == null)
        {
            throw new EntityNotFoundException("Order not found.");
        }

        if (!user.IsAdministrator() && order.BuyerId != user.GetBuyerId())
        {
            throw new EntityNotFoundException("Order not found.");
        }

        var list = await notifications.ListForOrderAsync(orderId, refreshFromProvider: true);
        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = list.Select(OrderNotificationDtoMapper.ToDto).ToList()
        });
    }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public System.Collections.Generic.List<OrderNotificationDto> Notifications { get; set; } = new();
}
