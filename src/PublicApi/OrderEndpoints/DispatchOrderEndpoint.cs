using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: marks an order dispatched, tells the shopper it is on its way, and queues a follow-up
/// with the provider for a few days later asking how the delivery went.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, OrderOperationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new OrderOperationRequest { OrderId = orderId }, service);
            })
            .Produces<OrderOperationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderOperationRequest request, IOrderNotificationService service)
    {
        var dispatched = await service.DispatchAsync(request.OrderId);
        if (!dispatched) return Results.NotFound();

        return Results.Ok(new OrderOperationResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Status = OrderStatus.Dispatched.ToString()
        });
    }
}
