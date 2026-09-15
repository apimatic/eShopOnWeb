using System;
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

public class CancelOrderRequest : BaseRequest
{
    public CancelOrderRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse() { }
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderService>
{
    private readonly IOrderNotificationService _notifications;
    private readonly IRepository<ApplicationCore.Entities.OrderAggregate.Order> _orders;

    public CancelOrderEndpoint(
        IOrderNotificationService notifications,
        IRepository<ApplicationCore.Entities.OrderAggregate.Order> orders)
    {
        _notifications = notifications;
        _orders = orders;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderService orderService) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), orderService);
            })
            .Produces<CancelOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderService orderService)
    {
        try
        {
            await orderService.CancelAsync(request.OrderId);
            var order = await _orders.GetByIdAsync(request.OrderId);
            if (order is not null)
            {
                await _notifications.NotifyOrderCancelledAsync(order.Id, order.BuyerId);
            }

            return Results.Ok(new CancelOrderResponse(request.CorrelationId())
            {
                OrderId = request.OrderId,
                Status = "Cancelled"
            });
        }
        catch (OrderNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOrderStateException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }
}
