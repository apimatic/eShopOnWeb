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
/// Operator action: cancels an order. The shopper is told, and any follow-up that has not yet gone
/// out is called off with the provider — a cancelled order must never get the delivery-feedback text.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, HttpContext>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public CancelOrderEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
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
            var order = await _orderNotificationService.CancelOrderAsync(orderId, http.RequestAborted);
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
