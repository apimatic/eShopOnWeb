using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderEndpoint : IEndpoint<IResult, int, ICatalogOrderService>
{
    private readonly IOrderNotificationService _notifications;

    public DispatchOrderEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, ICatalogOrderService orders) =>
            {
                return await HandleAsync(orderId, orders);
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, ICatalogOrderService orders)
    {
        var order = await orders.DispatchAsync(orderId);
        await _notifications.NotifyOrderDispatchedAsync(order);
        return Results.Ok(new OrderActionResponse
        {
            OrderId = order.Id,
            Status = order.Status
        });
    }
}
