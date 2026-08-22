using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetMyOrdersEndpoint : IEndpoint<IResult, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IRepository<Order> orders, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(user, orders, notifications);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IRepository<Order> orders)
        => HandleAsync(new ClaimsPrincipal(), orders, notifications: null!);

    private async Task<IResult> HandleAsync(
        ClaimsPrincipal user,
        IRepository<Order> orders,
        IOrderNotificationService notifications)
    {
        var buyerId = user.GetBuyerId();
        var orderList = await orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var notificationList = await notifications.ListForBuyerAsync(buyerId);
        var notificationsByOrder = notificationList.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new GetMyOrdersResponse();
        foreach (var order in orderList)
        {
            notificationsByOrder.TryGetValue(order.Id, out var orderNotifications);
            response.Orders.Add(new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Notifications = (orderNotifications ?? []).Select(NotificationDto.From).ToList()
            });
        }

        return Results.Ok(response);
    }
}
