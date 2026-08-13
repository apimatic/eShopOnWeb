using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>The caller's own orders, each showing where its notifications got to.</summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, IRepository<Order>, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (System.Security.Claims.ClaimsPrincipal user, IRepository<Order> orderRepository,
                IOrderNotificationService notifications) =>
            {
                var owner = CallerIdentity.GetUserName(user);
                if (string.IsNullOrEmpty(owner))
                {
                    return Results.Unauthorized();
                }

                var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(owner));
                var orderIds = orders.Select(o => o.Id).ToArray();
                var notificationsByOrder = (await notifications.GetNotificationsForOrdersAsync(orderIds))
                    .GroupBy(n => n.OrderId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var response = new ListMyOrdersResponse
                {
                    Orders = orders.Select(o => new OrderDto
                    {
                        OrderId = o.Id,
                        Status = o.Status.ToString(),
                        OrderDate = o.OrderDate,
                        Total = o.Total(),
                        Items = o.OrderItems.Select(i => new OrderLineDto
                        {
                            CatalogItemId = i.ItemOrdered.CatalogItemId,
                            ProductName = i.ItemOrdered.ProductName,
                            UnitPrice = i.UnitPrice,
                            Units = i.Units
                        }).ToList(),
                        Notifications = (notificationsByOrder.TryGetValue(o.Id, out var list) ? list : new())
                            .OrderByDescending(n => n.CreatedAt)
                            .Select(NotificationDto.From)
                            .ToList()
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IRepository<Order> orderRepository, IOrderNotificationService notifications) =>
        Task.FromResult<IResult>(Results.Empty);
}

public class ListMyOrdersResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}
