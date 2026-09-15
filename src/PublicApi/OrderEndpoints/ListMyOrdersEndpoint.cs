using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, IPublicApiOrderService>
{
    private readonly IOrderNotificationService _notificationService;

    public ListMyOrdersEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IPublicApiOrderService orderService, HttpContext httpContext) =>
            {
                return await HandleAsync(orderService, httpContext);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IPublicApiOrderService orderService)
        => HandleAsync(orderService, null!);

    private async Task<IResult> HandleAsync(IPublicApiOrderService orderService, HttpContext httpContext)
    {
        var buyerId = httpContext.User.GetBuyerId();
        var orders = await orderService.ListMyOrdersAsync(buyerId);
        var response = new ListMyOrdersResponse();

        foreach (var order in orders)
        {
            var notifications = await _notificationService.ListForOrderAsync(order.Id, refreshFromProvider: true);
            response.Orders.Add(new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total(),
                OrderDate = order.OrderDate,
                Items = order.OrderItems.Select(i => new MyOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    Units = i.Units,
                    UnitPrice = i.UnitPrice
                }).ToList(),
                Notifications = notifications.Select(NotificationDto.From).ToList()
            });
        }

        return Results.Ok(response);
    }
}
