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

public class DispatchOrderEndpoint : IEndpoint<IResult, IShopperOrderService>
{
    private readonly IOrderNotificationService _notificationService;

    public DispatchOrderEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IShopperOrderService orderService) =>
            {
                try
                {
                    var order = await orderService.DispatchAsync(orderId);
                    await _notificationService.NotifyOrderDispatchedAsync(order);
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

public class OrderStatusResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
