using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, int, ICatalogOrderService>
{
    private readonly IOrderNotificationService _notifications;

    public CancelOrderEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, ICatalogOrderService orders) =>
            {
                return await HandleAsync(orderId, orders);
            })
            .Produces<CreateOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, ICatalogOrderService orders)
    {
        try
        {
            var order = await orders.CancelAsync(orderId);
            await _notifications.NotifyOrderCancelledAsync(order);
            return Results.Ok(new CreateOrderResponse
            {
                OrderId = order.Id,
                Status = order.Status.ToString()
            });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }
}
