using System.Linq;
using System.Security.Claims;
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

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orders;
    private readonly IOrderNotificationService _notifications;

    public ListOrderNotificationsEndpoint(IRepository<Order> orders, IOrderNotificationService notifications)
    {
        _orders = orders;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, ClaimsPrincipal user) =>
                await HandleAsync(orderId, user))
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user)
    {
        var order = await _orders.GetByIdAsync(orderId);
        if (order == null)
        {
            return Results.NotFound();
        }

        var buyerId = user.GetRequiredBuyerId();
        if (!user.IsAdministrator() && order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var notifications = await _notifications.GetAndRefreshForOrderAsync(orderId);
        var response = new ListOrderNotificationsResponse();
        response.Notifications.AddRange(notifications.Select(NotificationDtoMapper.ToDto));
        return Results.Ok(response);
    }
}
