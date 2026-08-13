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

public class DispatchOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = "dispatched";
}

/// <summary>
/// Operator action: marks an order dispatched. The shopper is told it is on its way and a "how did
/// delivery go?" follow-up is queued with the provider for a few days later. Restricted to the
/// administrator role.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http) => await HandleAsync(orderId, http))
            .Produces<DispatchOrderResponse>()
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

        await notifications.NotifyOrderDispatchedAsync(order, http.RequestAborted);

        return Results.Ok(new DispatchOrderResponse { OrderId = orderId });
    }
}
