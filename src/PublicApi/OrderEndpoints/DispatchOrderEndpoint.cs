using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IRepository<Order>>
{
    private readonly IOrderNotificationService _notifications;

    public DispatchOrderEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orders) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId), orders);
            })
            .Produces<OrderSummaryDto>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, IRepository<Order> orders)
    {
        var order = await orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order is null)
        {
            return Results.NotFound();
        }

        var alreadyDispatched = order.Status == OrderStatus.Dispatched;
        if (!alreadyDispatched)
        {
            order.MarkDispatched();
            await orders.UpdateAsync(order);
            await _notifications.NotifyOrderDispatchedAsync(order);
        }

        var notifications = await _notifications.ListForOrderAsync(order.Id, refreshFromProvider: true);
        return Results.Ok(OrderSummaryDto.From(order, notifications));
    }
}

public class DispatchOrderRequest : BaseRequest
{
    public DispatchOrderRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; }
}
