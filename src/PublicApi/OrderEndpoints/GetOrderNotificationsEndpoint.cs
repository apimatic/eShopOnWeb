using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetOrderNotificationsRequest : BaseRequest
{
    public GetOrderNotificationsRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; }
}

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IRepository<Order>>
{
    private readonly IOrderNotificationService _notifications;

    public GetOrderNotificationsEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext httpContext, IRepository<Order> orders) =>
            {
                return await HandleAsync(new GetOrderNotificationsRequest(orderId), orders, httpContext);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IRepository<Order> orders) =>
        throw new System.NotSupportedException();

    private async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IRepository<Order> orders, HttpContext httpContext)
    {
        var buyerId = httpContext.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await orders.GetByIdAsync(request.OrderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        if (!httpContext.IsAdministrator() && order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var notifications = await _notifications.ListForOrderAsync(request.OrderId);
        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationMapper.ToDto).ToList()
        });
    }
}
