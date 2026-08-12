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
    public int OrderId { get; set; }
}

/// <summary>
/// Operator action: marks an order dispatched, tells the shopper it is on its way, and queues a
/// delivery follow-up with the provider for a few days later. Restricted to administrators.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IOrderFulfillmentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderFulfillmentService service) =>
            {
                return await HandleAsync(new DispatchOrderRequest { OrderId = orderId }, service);
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, IOrderFulfillmentService service)
    {
        var result = await service.DispatchAsync(request.OrderId);
        return result.Outcome switch
        {
            ActionOutcome.Success => Results.Ok(new OrderActionResponse(request.CorrelationId())
            {
                OrderId = request.OrderId,
                Message = "Order dispatched. The shopper has been notified and a delivery follow-up has been queued."
            }),
            ActionOutcome.NotFound => Results.NotFound(new { error = result.Error }),
            _ => Results.Conflict(new { error = result.Error })
        };
    }
}
