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
/// Operator action: cancels an order, tells the shopper, and calls off any not-yet-sent delivery follow-up
/// so it never reaches a customer whose order was cancelled.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderOperationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new OrderOperationRequest { OrderId = orderId }, service);
            })
            .Produces<OrderOperationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderOperationRequest request, IOrderNotificationService service)
    {
        var cancelled = await service.CancelAsync(request.OrderId);
        if (!cancelled) return Results.NotFound();

        return Results.Ok(new OrderOperationResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Status = OrderStatus.Cancelled.ToString()
        });
    }
}
