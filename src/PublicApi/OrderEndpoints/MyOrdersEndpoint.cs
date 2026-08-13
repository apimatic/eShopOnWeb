using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// GET /api/my-orders — the caller's orders, each showing where its notifications got to. Only the caller's own
/// orders are returned. Notification delivery outcomes are refreshed from the provider before returning.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, OrderEndpointServices>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (OrderEndpointServices services) => await HandleAsync(services))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderEndpointServices services)
    {
        var buyerId = services.User.UserName();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var orders = await services.Orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var orderIds = orders.Select(o => o.Id).ToList();

        var notifications = await services.Notifications.ListAsync(new NotificationsByOrdersSpecification(orderIds));
        await services.Notifier.RefreshStatusesAsync(notifications);

        var response = new MyOrdersResponse
        {
            Orders = orders
                .OrderByDescending(o => o.OrderDate)
                .Select(o => new MyOrderDto
                {
                    OrderId = o.Id,
                    Status = o.Status.ToString(),
                    OrderDate = o.OrderDate,
                    Total = o.Total(),
                    Notifications = notifications
                        .Where(n => n.OrderId == o.Id)
                        .Select(NotificationDto.FromEntity)
                        .ToList()
                })
                .ToList()
        };
        return Results.Ok(response);
    }
}
