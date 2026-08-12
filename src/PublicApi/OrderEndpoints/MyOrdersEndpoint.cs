using System.Linq;
using System.Security.Claims;
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

/// <summary>The signed-in shopper's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IShopperOrderService service, CancellationToken cancellationToken) =>
            {
                var buyerId = CurrentUser.GetUserName(user);
                if (buyerId is null)
                {
                    return Results.Unauthorized();
                }

                var orders = await service.GetOrdersForBuyerAsync(buyerId, cancellationToken);

                var response = new MyOrdersResponse
                {
                    Orders = orders.Select(o => new OrderSummaryDto
                    {
                        OrderId = o.OrderId,
                        Status = o.Status.ToString(),
                        OrderDate = o.OrderDate,
                        Total = o.Total,
                        Notifications = o.Notifications.Select(NotificationDto.FromView).ToList()
                    }).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }
}
