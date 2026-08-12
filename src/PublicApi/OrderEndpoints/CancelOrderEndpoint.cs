using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order. The shopper is told, and any queued delivery follow-up is called off
/// with the provider before it can go out. Administrators only.
/// </summary>
public class CancelOrderEndpoint : AuthenticatedEndpointBase,
    IEndpoint<IResult, OrderIdRequest, IOrderNotificationService>
{
    public CancelOrderEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
    {
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) =>
                await HandleAsync(new OrderIdRequest(orderId), service))
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderIdRequest request, IOrderNotificationService service)
    {
        await service.CancelAsync(request.OrderId, RequestAborted);
        return Results.Ok(new OrderActionResponse { OrderId = request.OrderId, Status = "Cancelled" });
    }
}
