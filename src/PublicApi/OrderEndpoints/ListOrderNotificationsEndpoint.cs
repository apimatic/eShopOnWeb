using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, ICatalogOrderService>
{
    private readonly IOrderNotificationService _notifications;

    public ListOrderNotificationsEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext httpContext, ICatalogOrderService orders) =>
            {
                return await HandleAsync(orderId, httpContext, orders);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, ICatalogOrderService orders)
    {
        throw new System.NotSupportedException();
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext httpContext, ICatalogOrderService orders)
    {
        var order = await orders.GetByIdAsync(orderId);
        if (order == null)
        {
            return Results.NotFound();
        }

        var buyerId = httpContext.GetBuyerId();
        if (order.BuyerId != buyerId && !httpContext.User.IsAdministrator())
        {
            return Results.Forbid();
        }

        var notifications = await _notifications.ListForOrderAsync(orderId, refreshFromProvider: true);
        var response = new ListOrderNotificationsResponse();
        response.Notifications.AddRange(notifications.Select(NotificationDto.FromEntity));
        return Results.Ok(response);
    }
}
