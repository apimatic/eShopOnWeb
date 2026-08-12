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
/// Operator action: marks an order dispatched. The shopper is told it is on its way and a "how did the
/// delivery go?" follow-up is queued with the provider for a few days later. Administrators only.
/// </summary>
public class DispatchOrderEndpoint : AuthenticatedEndpointBase,
    IEndpoint<IResult, OrderIdRequest, IOrderNotificationService>
{
    public DispatchOrderEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
    {
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) =>
                await HandleAsync(new OrderIdRequest(orderId), service))
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderIdRequest request, IOrderNotificationService service)
    {
        await service.DispatchAsync(request.OrderId, RequestAborted);
        return Results.Ok(new OrderActionResponse { OrderId = request.OrderId, Status = "Dispatched" });
    }
}
