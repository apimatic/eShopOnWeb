using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public record DispatchOrderRequest(int OrderId);
public record DispatchOrderResponse(int OrderId, string Status);

/// <summary>Operator action: mark an order dispatched, tell the shopper it is on its way, and queue the
/// delivery follow-up with the provider for a few days later.</summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId), service);
            })
            .Produces<DispatchOrderResponse>()
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, IOrderNotificationService service)
    {
        var transitioned = await service.DispatchAsync(request.OrderId, default);
        return transitioned
            ? Results.Ok(new DispatchOrderResponse(request.OrderId, "Dispatched"))
            : Results.Conflict(new { message = "Order is not in a state that can be dispatched." });
    }
}
