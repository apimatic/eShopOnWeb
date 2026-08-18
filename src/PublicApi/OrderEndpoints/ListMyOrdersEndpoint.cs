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

/// <summary>Returns the caller's orders, each showing where its notifications got to.</summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, string, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                return await HandleAsync(user.Identity!.Name!, service);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IOrderNotificationService service)
    {
        var orders = await service.GetMyOrdersAsync(buyerId);
        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => new MyOrderDto
            {
                OrderId = o.Order.Id,
                Status = o.Order.Status.ToString(),
                Total = o.Order.Total(),
                OrderDate = o.Order.OrderDate,
                Notifications = o.Notifications.Select(OrderNotificationDto.From).ToList()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
