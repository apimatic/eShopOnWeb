using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderRequest : BaseRequest
{
    public int OrderId { get; init; }
    public DispatchOrderRequest(int orderId) => OrderId = orderId;
}

/// <summary>
/// POST /api/orders/{orderId}/dispatch — an operator marks the order dispatched. The shopper is told
/// it is on its way and a delivery follow-up is queued with the provider for a few days later.
/// Operator-only (administrator role).
/// </summary>
public class DispatchOrderEndpoint : ApiEndpointBase,
    IEndpoint<IResult, DispatchOrderRequest, IApiOrderService>
{
    public DispatchOrderEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IApiOrderService orderService) =>
                await HandleAsync(new DispatchOrderRequest(orderId), orderService))
            .Produces<OrderTransitionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, IApiOrderService orderService)
    {
        var result = await orderService.DispatchAsync(request.OrderId, Aborted);
        return result.Outcome switch
        {
            OrderTransitionOutcome.OrderNotFound => Results.NotFound(),
            OrderTransitionOutcome.InvalidState => Results.Conflict(new { message = result.Error }),
            _ => Results.Ok(new OrderTransitionResponse(request.CorrelationId())
            {
                OrderId = request.OrderId,
                Status = result.Order!.Status.ToString()
            })
        };
    }
}
