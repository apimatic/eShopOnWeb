using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the caller's orders, each showing where its notifications got to.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<Order> orderRepository,
                IRepository<OrderNotification> notificationRepository, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, orderRepository, notificationRepository, cancellationToken);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    private async Task<IResult> HandleAsync(ClaimsPrincipal user, IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository, CancellationToken cancellationToken)
    {
        var buyerId = user.Identity?.Name ?? string.Empty;
        var response = new ListMyOrdersResponse();

        var orders = (await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken))
            .OrderByDescending(o => o.OrderDate)
            .ToList();

        var notifications = (await notificationRepository.ListAsync(cancellationToken))
            .Where(n => n.BuyerId == buyerId)
            .ToList();

        foreach (var order in orders)
        {
            response.Orders.Add(new OrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Notifications = notifications
                    .Where(n => n.OrderId == order.Id)
                    .Select(NotificationDto.FromEntity)
                    .ToList()
            });
        }

        return Results.Ok(response);
    }
}
