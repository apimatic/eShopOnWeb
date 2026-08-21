using System.Collections.Generic;
using System.Linq;
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

public class OrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, int, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext, IShopperOrderService shopperOrderService) =>
            {
                return await HandleAsync(orderId, shopperOrderService, httpContext);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IShopperOrderService shopperOrderService)
        => HandleAsync(orderId, shopperOrderService, httpContext: null!);

    private async Task<IResult> HandleAsync(int orderId, IShopperOrderService shopperOrderService, HttpContext httpContext)
    {
        var buyerId = ShopperIdentity.TryGetBuyerId(httpContext);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var notifications = await shopperOrderService.ListOrderNotificationsAsync(
            orderId, buyerId, ShopperIdentity.IsAdministrator(httpContext));

        return Results.Ok(new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        });
    }
}
