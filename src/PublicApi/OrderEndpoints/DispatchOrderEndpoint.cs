using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext http) => await HandleAsync(orderId, http))
            .Produces<Response>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext http)
    {
        try
        {
            var orders = http.GetRequired<IShopperOrderService>();
            var notifications = http.GetRequired<IOrderNotificationService>();
            var order = await orders.DispatchAsync(orderId);
            var list = await notifications.ListForOrderAsync(order.Order.Id);
            return Results.Ok(new Response
            {
                OrderId = order.Order.Id,
                Status = order.Status.ToString(),
                Notifications = list.Select(NotificationDto.From).ToList()
            });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (OrderStateException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

    public class Response
    {
        public int OrderId { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<NotificationDto> Notifications { get; set; } = new();
    }
}
