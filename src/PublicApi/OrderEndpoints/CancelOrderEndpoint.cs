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

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IRepository<Order>>
{
    private readonly IOrderNotificationService _notifications;

    public CancelOrderEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orders) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), orders);
            })
            .Produces<OrderSummaryDto>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IRepository<Order> orders)
    {
        var order = await orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order is null)
        {
            return Results.NotFound();
        }

        var alreadyCancelled = order.Status == OrderStatus.Cancelled;
        if (!alreadyCancelled)
        {
            order.MarkCancelled();
            await orders.UpdateAsync(order);
            await _notifications.NotifyOrderCancelledAsync(order);
        }

        var notifications = await _notifications.ListForOrderAsync(order.Id, refreshFromProvider: true);
        return Results.Ok(OrderSummaryDto.From(order, notifications));
    }
}

public class CancelOrderRequest : BaseRequest
{
    public CancelOrderRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; }
}
