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
/// Operator action: cancel an order. The shopper is told, and any follow-up that has not yet gone out is
/// called off so it never reaches them.
/// </summary>
public class CancelOrderEndpoint
    : IEndpoint<IResult, OrderTransitionRequest, IOperatorOrderService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
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
        var result = await service.CancelAsync(request.OrderId, ct);
        return result switch
        {
            OperatorTransitionResult.NotFound => Results.NotFound(),
            OperatorTransitionResult.NoOp => Results.Conflict(new { error = "The order is already cancelled." }),
            _ => Results.Ok(new OrderTransitionResponse { OrderId = request.OrderId, Status = "Cancelled" })
        };
    }
}
