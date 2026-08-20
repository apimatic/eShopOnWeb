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

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, IShopOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext httpContext, IShopOrderService orderService) =>
            {
                return await HandleAsync(orderId, orderService, httpContext);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IShopOrderService orderService)
        => HandleAsync(orderId, orderService, null!);

    private async Task<IResult> HandleAsync(int orderId, IShopOrderService orderService, HttpContext httpContext)
    {
        var order = await orderService.GetOrderForCallerAsync(
            orderId,
            httpContext.GetBuyerId(),
            httpContext.IsAdministrator());
        if (order == null)
        {
            return Results.NotFound();
        }

        var notifications = await orderService.ListNotificationsForOrderAsync(orderId);
        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        });
    }
}
