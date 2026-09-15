using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// GET /api/orders/{orderId}/notifications — what was sent for this order, and what became of each message. Each
/// entry carries its own notificationId (what the operator endpoints act on). Visible to the order's owner, and to
/// operators (administrators) who act on the notifications. Delivery outcomes are refreshed from the provider.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, int, OrderEndpointServices>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, OrderEndpointServices services) => await HandleAsync(orderId, services))
            .Produces<OrderNotificationsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, OrderEndpointServices services)
    {
        var buyerId = services.User.UserName();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var order = await services.Orders.GetByIdAsync(orderId);

        // Hide other shoppers' orders entirely: a non-owner who is not an operator sees the same "not found".
        if (order is null || (order.BuyerId != buyerId && !services.User.IsAdministrator()))
            return Results.NotFound();

        var notifications = await services.Notifier.GetOrderNotificationsAsync(orderId);

        var response = new OrderNotificationsResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
        };
        return Results.Ok(response);
    }
}
