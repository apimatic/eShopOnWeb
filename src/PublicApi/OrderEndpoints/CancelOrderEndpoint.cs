using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancel an order. Any not-yet-sent delivery follow-up is called off at the
/// provider so it can never reach the shopper, and the shopper is told the order was cancelled.
/// Restricted to the administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderActionRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new OrderActionRequest(orderId), service);
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IOrderNotificationService service)
    {
        try
        {
            var order = await service.CancelOrderAsync(request.OrderId);
            if (order is null)
            {
                return Results.NotFound();
            }
            return Results.Ok(new OrderActionResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString()
            });
        }
        catch (InvalidOrderStateException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}
