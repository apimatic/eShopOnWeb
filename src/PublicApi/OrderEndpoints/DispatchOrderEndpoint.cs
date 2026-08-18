using System.Net;
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
/// Operator action: marks an order dispatched. The shopper is told it is on its way, and a
/// "how did the delivery go?" follow-up is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, int, HttpContext>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public DispatchOrderEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http) => await HandleAsync(orderId, http))
            .Produces<OrderStatusResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext http)
    {
        try
        {
            var order = await _orderNotificationService.DispatchOrderAsync(orderId, http.RequestAborted);
            if (order is null)
                return Results.NotFound();

            return Results.Ok(new OrderStatusResponse { OrderId = order.Id, Status = order.Status.ToString() });
        }
        catch (InvalidOrderStatusTransitionException ex)
        {
            return Results.Problem(ex.Message, statusCode: (int)HttpStatusCode.Conflict);
        }
    }
}
