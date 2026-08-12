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

/// <summary>Returns the signed-in shopper's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderNotificationService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var callerId = user.GetCallerId();
                if (string.IsNullOrEmpty(callerId))
                    return Results.Unauthorized();

                var ordersWithNotifications = await service.GetOrdersForBuyerAsync(callerId, ct);

                var response = new MyOrdersResponse
                {
                    Orders = ordersWithNotifications.Select(o => new MyOrderDto
                    {
                        OrderId = o.Order.Id,
                        Status = o.Order.Status.ToString(),
                        OrderDate = o.Order.OrderDate,
                        Total = o.Order.Total(),
                        Notifications = o.Notifications.Select(OrderNotificationDto.From).ToList()
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service)
        => Task.FromResult<IResult>(Results.Empty);
}
