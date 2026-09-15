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

public class ListMyOrdersRequest : BaseRequest
{
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IPublicOrderService>
{
    private readonly IOrderNotificationService _notifications;

    public ListMyOrdersEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IPublicOrderService service) =>
            {
                return await HandleAsync(new ListMyOrdersRequest(), httpContext, service);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ListMyOrdersRequest request, IPublicOrderService service)
        => HandleAsync(request, null!, service);

    private async Task<IResult> HandleAsync(ListMyOrdersRequest request, HttpContext httpContext, IPublicOrderService service)
    {
        var buyerId = AuthenticatedUser.GetBuyerId(httpContext.User);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await service.ListOrdersForBuyerAsync(buyerId);
        var notifications = await _notifications.ListForBuyerAsync(buyerId);
        var notificationsByOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new ListMyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(order => new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Items = order.OrderItems.Select(i => new MyOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    Quantity = i.Units,
                    UnitPrice = i.UnitPrice
                }).ToList(),
                Notifications = notificationsByOrder.TryGetValue(order.Id, out var orderNotes)
                    ? NotificationDtoMapper.ToDtos(orderNotes)
                    : new()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
