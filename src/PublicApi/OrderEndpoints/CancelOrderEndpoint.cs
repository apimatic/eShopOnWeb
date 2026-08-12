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
    public int OrderId { get; set; }
}

/// <summary>
/// Operator action: cancels an order, tells the shopper, and calls off any delivery follow-up that has
/// not yet gone out so it never reaches them. Restricted to administrators.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderFulfillmentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderFulfillmentService service) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, service);
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderFulfillmentService service)
    {
        var result = await service.CancelAsync(request.OrderId);
        return result.Outcome switch
        {
            ActionOutcome.Success => Results.Ok(new OrderActionResponse(request.CorrelationId())
            {
                OrderId = request.OrderId,
                Message = "Order cancelled. The shopper has been notified and any pending delivery follow-up has been called off."
            }),
            ActionOutcome.NotFound => Results.NotFound(new { error = result.Error }),
            _ => Results.Conflict(new { error = result.Error })
        };
    }
}
