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

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IRepository<Order>>
{
    private readonly IOrderNotificationService _notifications;

    public ListOrderNotificationsEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orders, HttpContext httpContext) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest(orderId)
                {
                    BuyerId = httpContext.GetBuyerId(),
                    IsAdministrator = httpContext.IsAdministrator()
                }, orders);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IRepository<Order> orders)
    {
        var order = await orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order is null)
        {
            return Results.NotFound();
        }

        if (!request.IsAdministrator && !order.BelongsTo(request.BuyerId))
        {
            return Results.NotFound();
        }

        var notifications = await _notifications.ListForOrderAsync(order.Id, refreshFromProvider: true);
        var response = new ListOrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = order.Id
        };
        foreach (var notification in notifications)
        {
            response.Notifications.Add(OrderNotificationDto.From(notification));
        }

        return Results.Ok(response);
    }
}

public class ListOrderNotificationsRequest : BaseRequest
{
    public ListOrderNotificationsRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; }
    internal string BuyerId { get; set; } = string.Empty;
    internal bool IsAdministrator { get; set; }
}
