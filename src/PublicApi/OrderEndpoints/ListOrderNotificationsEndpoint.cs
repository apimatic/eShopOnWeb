using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IShopperOrderService shopperOrderService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(orderId, shopperOrderService, user);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IShopperOrderService shopperOrderService)
        => HandleAsync(orderId, shopperOrderService, null);

    private async Task<IResult> HandleAsync(int orderId, IShopperOrderService shopperOrderService, ClaimsPrincipal? user)
    {
        var buyerId = BuyerIdentity.GetBuyerId(user ?? new ClaimsPrincipal());
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var notifications = await shopperOrderService.ListNotificationsAsync(buyerId, orderId, default);
        if (notifications is null)
        {
            return Results.NotFound();
        }

        var response = new ListOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };

        return Results.Ok(response);
    }
}
