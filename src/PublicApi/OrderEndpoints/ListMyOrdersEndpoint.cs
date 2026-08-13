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
using Microsoft.eShopWeb.PublicApi.Configuration;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersResponse : BaseResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

/// <summary>
/// Lists the signed-in shopper's own orders, each showing where its notifications got to.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, IReadRepository<Order>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IReadRepository<Order> orderRepository) =>
                await HandleAsync(orderRepository))
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IReadRepository<Order> orderRepository)
    {
        var ownerId = _httpContextAccessor.GetOwnerId();
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var ct = _httpContextAccessor.RequestAborted();
        var notificationService = _httpContextAccessor.RequestService<IOrderNotificationService>();

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(ownerId), ct);

        var response = new ListMyOrdersResponse();
        foreach (var order in orders)
        {
            // Summary view uses the last-known notification state (no provider round-trips); the per-order
            // notifications endpoint refreshes live status.
            var notifications = await notificationService.GetOrderNotificationsAsync(order.Id, refreshFromProvider: false, ct);

            response.Orders.Add(new OrderSummaryDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Items = order.OrderItems.Select(oi => new OrderItemDto
                {
                    CatalogItemId = oi.ItemOrdered.CatalogItemId,
                    ProductName = oi.ItemOrdered.ProductName,
                    UnitPrice = oi.UnitPrice,
                    Units = oi.Units
                }).ToList(),
                Notifications = notifications.Select(n => n.ToDto()).ToList()
            });
        }

        return Results.Ok(response);
    }
}
