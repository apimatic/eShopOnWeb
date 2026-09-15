using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IShopOrderService>
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
            (IShopOrderService service) =>
            {
                return await HandleAsync(new ListMyOrdersRequest(), service);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IShopOrderService service)
    {
        var http = _httpContextAccessor.HttpContext!;
        var orders = await service.ListMyOrdersAsync(http.RequireUserName(), http.RequestAborted);
        var response = new ListMyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(o => new ShopperOrderDto
            {
                OrderId = o.Order.Id,
                Status = o.Order.Status.ToString(),
                OrderDate = o.Order.OrderDate,
                Total = o.Order.Total(),
                Items = o.Order.OrderItems.Select(i => new ShopperOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Quantity = i.Units
                }).ToList(),
                Notifications = o.Notifications.Select(MapNotification).ToList()
            }).ToList()
        };
        return Results.Ok(response);
    }

    internal static OrderNotificationDto MapNotification(OrderNotification notification) =>
        new()
        {
            NotificationId = notification.Id,
            Kind = notification.Kind,
            ProviderSid = notification.ProviderSid,
            ProviderStatus = notification.ProviderStatus,
            ErrorCode = notification.ErrorCode,
            ErrorMessage = notification.ErrorMessage,
            Body = notification.ContentDisposed ? null : notification.Body,
            CreatedAt = notification.CreatedAt,
            SendAt = notification.SendAt,
            ContentDisposed = notification.ContentDisposed,
            SendFailure = notification.SendFailure
        };
}
