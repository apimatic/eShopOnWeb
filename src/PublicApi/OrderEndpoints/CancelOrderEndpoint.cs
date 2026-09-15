using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IRepository<Order>>
{
    private readonly IOrderNotificationService _notifications;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CancelOrderEndpoint(IOrderNotificationService notifications, IHttpContextAccessor httpContextAccessor)
    {
        _notifications = notifications;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orders) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), orders);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IRepository<Order> orders)
    {
        var order = await orders.GetByIdAsync(request.OrderId);
        if (order is null)
        {
            throw new EntityNotFoundException("Order was not found.");
        }

        order.MarkCancelled();
        await orders.UpdateAsync(order);

        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? default;
        await _notifications.NotifyOrderCancelledAsync(order, ct);

        return Results.Ok(new CancelOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}
