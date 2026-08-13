using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = "cancelled";
}

/// <summary>
/// Operator action: cancels an order. The shopper is told, and any delivery follow-up that has not yet
/// gone out is called off with the provider so it can never reach them. Restricted to the administrator
/// role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http) => await HandleAsync(orderId, http))
            .Produces<CancelOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext http)
    {
        var orderRepository = http.RequestServices.GetRequiredService<IRepository<Order>>();
        var notifications = http.RequestServices.GetRequiredService<IOrderNotificationService>();

        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), http.RequestAborted);
        if (order is null)
            return Results.NotFound();

        await notifications.NotifyOrderCancelledAsync(order, http.RequestAborted);

        return Results.Ok(new CancelOrderResponse { OrderId = orderId });
    }
}
