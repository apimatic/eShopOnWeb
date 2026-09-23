using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public record CancelOrderRequest(int OrderId);
public record CancelOrderResponse(int OrderId, string Status);

/// <summary>Operator action: cancel an order, tell the shopper, and call off any not-yet-sent delivery
/// follow-up so a "how did delivery go?" for a cancelled order can never reach them.</summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), service);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderNotificationService service)
    {
        var transitioned = await service.CancelAsync(request.OrderId, default);
        return transitioned
            ? Results.Ok(new CancelOrderResponse(request.OrderId, "Cancelled"))
            : Results.Conflict(new { message = "Order is already cancelled." });
    }
}
