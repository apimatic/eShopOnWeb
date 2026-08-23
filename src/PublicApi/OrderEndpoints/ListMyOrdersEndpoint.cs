using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IShopperOrderService service) =>
            {
                var userName = httpContext.GetUserName();
                if (string.IsNullOrWhiteSpace(userName))
                {
                    return Results.Unauthorized();
                }

                var orders = await service.GetMyOrdersAsync(userName, CancellationToken.None);
                return Results.Ok(new
                {
                    orders = orders.Select(summary => new
                    {
                        orderId = summary.Order.Id,
                        status = summary.Order.Status.ToString(),
                        orderDate = summary.Order.OrderDate,
                        total = summary.Order.Total(),
                        notifications = summary.Notifications.Select(NotificationDto.From)
                    })
                });
            })
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IShopperOrderService service)
    {
        return Task.FromResult(Results.Ok());
    }
}
