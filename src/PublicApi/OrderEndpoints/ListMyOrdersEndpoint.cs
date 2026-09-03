using System.Linq;
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

public class ListMyOrdersEndpoint : IEndpoint<IResult, HttpContext, IShopOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, IShopOrderService service) =>
            {
                return await HandleAsync(http, service);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, IShopOrderService service)
    {
        var buyerId = http.User.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var orders = await service.GetMyOrdersAsync(buyerId, http.RequestAborted);
        var response = new ListMyOrdersResponse
        {
            Orders = orders.Select(o => new MyOrderDto
            {
                OrderId = o.Order.Id,
                Status = o.Order.Status.ToString(),
                OrderDate = o.Order.OrderDate,
                Total = o.Order.Total(),
                Notifications = o.Notifications.Select(MapNotification).ToList()
            }).ToList()
        };
        return Results.Ok(response);
    }

    internal static NotificationStatusDto MapNotification(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        Kind = n.Kind.ToString(),
        Status = n.Status,
        ProviderSid = n.ProviderSid,
        ErrorCode = n.ErrorCode,
        Body = n.ContentRedacted ? null : n.Body,
        ContentRedacted = n.ContentRedacted,
        CreatedAt = n.CreatedAt
    };
}
