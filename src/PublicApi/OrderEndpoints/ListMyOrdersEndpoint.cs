using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, ICatalogOrderService>
{
    private readonly IOrderNotificationService _notifications;

    public ListMyOrdersEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ICatalogOrderService orders) =>
            {
                return await HandleAsync(user, orders);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ICatalogOrderService orders)
        => HandleAsync(new ClaimsPrincipal(), orders);

    private async Task<IResult> HandleAsync(ClaimsPrincipal user, ICatalogOrderService orders)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var myOrders = await orders.ListForBuyerAsync(buyerId, default);
        var notes = await _notifications.ListForOrdersAsync(myOrders.Select(o => o.Id).ToList(), default);

        var response = new ListMyOrdersResponse
        {
            Orders = myOrders.Select(o =>
            {
                notes.TryGetValue(o.Id, out var forOrder);
                return OrderApiMapper.ToSummary(o, forOrder ?? System.Array.Empty<OrderNotification>());
            }).ToList()
        };

        return Results.Ok(response);
    }
}
