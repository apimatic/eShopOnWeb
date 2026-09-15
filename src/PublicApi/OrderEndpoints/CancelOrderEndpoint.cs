using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; init; }
    public CancelOrderRequest(int orderId) => OrderId = orderId;
}

/// <summary>
/// POST /api/orders/{orderId}/cancel — an operator cancels the order. The shopper is told, and any
/// not-yet-sent delivery follow-up is called off so it can never reach them. Operator-only.
/// </summary>
public class CancelOrderEndpoint : ApiEndpointBase,
    IEndpoint<IResult, CancelOrderRequest, IApiOrderService>
{
    public CancelOrderEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IApiOrderService orderService) =>
                await HandleAsync(new CancelOrderRequest(orderId), orderService))
            .Produces<OrderTransitionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IApiOrderService orderService)
    {
        var result = await orderService.CancelAsync(request.OrderId, Aborted);
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
