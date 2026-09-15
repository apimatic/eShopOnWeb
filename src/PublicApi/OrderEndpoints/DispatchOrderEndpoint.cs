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

public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IRepository<Order>>
{
    private readonly IOrderNotificationService _notifications;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DispatchOrderEndpoint(IOrderNotificationService notifications, IHttpContextAccessor httpContextAccessor)
    {
        _notifications = notifications;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orders) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId), orders);
            })
            .Produces<DispatchOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, IRepository<Order> orders)
    {
        var order = await orders.GetByIdAsync(request.OrderId);
        if (order is null)
        {
            throw new EntityNotFoundException("Order was not found.");
        }

        order.MarkDispatched();
        await orders.UpdateAsync(order);

        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? default;
        await _notifications.NotifyOrderDispatchedAsync(order, ct);

        return Results.Ok(new DispatchOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}
