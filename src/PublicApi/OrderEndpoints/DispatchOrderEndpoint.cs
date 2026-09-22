using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: mark an order dispatched. The shopper is told it is on its way and a delivery-feedback
/// follow-up is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint
    : IEndpoint<IResult, OrderTransitionRequest, IOperatorOrderService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOperatorOrderService service, CancellationToken ct) =>
            {
                return await HandleAsync(new OrderTransitionRequest { OrderId = orderId }, service, ct);
            })
            .Produces<OrderTransitionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderTransitionRequest request, IOperatorOrderService service,
        CancellationToken ct)
    {
        var result = await service.DispatchAsync(request.OrderId, ct);
        return result switch
        {
            OperatorTransitionResult.NotFound => Results.NotFound(),
            OperatorTransitionResult.NoOp => Results.Conflict(
                new { error = "The order is not in a state that can be dispatched." }),
            _ => Results.Ok(new OrderTransitionResponse { OrderId = request.OrderId, Status = "Dispatched" })
        };
    }
}

public class OrderTransitionRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class OrderTransitionResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
