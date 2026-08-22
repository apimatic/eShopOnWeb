using System.Collections.Generic;
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

public class CancelOrderEndpoint : IEndpoint<IResult, IShopperOrderService>
{
    private readonly IOrderNotificationService _notificationService;

    public CancelOrderEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IShopperOrderService orderService) =>
            {
                try
                {
                    var order = await orderService.CancelAsync(orderId);
                    await _notificationService.NotifyOrderCancelledAsync(order);
                    return Results.Ok(new OrderStatusResponse
                    {
                        OrderId = order.Id,
                        Status = order.Status.ToString()
                    });
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (InvalidOrderTransitionException ex)
                {
                    return Results.Conflict(new { message = ex.Message });
                }
            })
            .Produces<OrderStatusResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IShopperOrderService orderService)
    {
        return Task.FromResult<IResult>(Results.StatusCode(StatusCodes.Status501NotImplemented));
    }
}
