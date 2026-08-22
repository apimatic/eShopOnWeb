using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, int, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IShopperOrderService orders, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(orderId, orders, notifications);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IShopperOrderService orders)
    {
        throw new System.NotSupportedException("Use the routed handler that supplies the current request services.");
    }

    private static async Task<IResult> HandleAsync(int orderId, IShopperOrderService orders, IOrderNotificationService notifications)
    {
        var order = await orders.CancelAsync(orderId);
        var list = await notifications.ListForOrderAsync(order.Id, refreshFromProvider: true);
        return Results.Ok(new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Notifications = list.Select(OrderNotificationDtoMapper.ToDto).ToList()
        });
    }
}
