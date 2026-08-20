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

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext http) => await HandleAsync(orderId, http))
            .Produces<Response>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext http)
    {
        var buyerId = http.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = http.GetRequired<IShopperOrderService>();
        var order = await orders.GetOrderForCallerAsync(orderId, buyerId, http.IsAdministrator());
        if (order is null)
        {
            return Results.NotFound();
        }

        var notifications = await http.GetRequired<IOrderNotificationService>()
            .ListForOrderAsync(order.Order.Id);
        return Results.Ok(new Response
        {
            OrderId = order.Order.Id,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        });
    }

    public class Response
    {
        public int OrderId { get; set; }
        public System.Collections.Generic.List<NotificationDto> Notifications { get; set; } = new();
    }
}
