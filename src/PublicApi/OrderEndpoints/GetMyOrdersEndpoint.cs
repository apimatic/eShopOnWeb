using System;
using System.Collections.Generic;
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

public class GetMyOrdersEndpoint : IEndpoint<IResult, GetMyOrdersRequest, IShopperOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOrderNotificationService _notificationService;

    public GetMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor, IOrderNotificationService notificationService)
    {
        _httpContextAccessor = httpContextAccessor;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IShopperOrderService service) =>
            {
                return await HandleAsync(new GetMyOrdersRequest(), service);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMyOrdersRequest request, IShopperOrderService service)
    {
        try
        {
            var buyerId = _httpContextAccessor.HttpContext!.RequireUserName();
            var orders = await service.ListForBuyerAsync(buyerId);
            var notifications = await _notificationService.ListForOrdersAsync(orders.Select(o => o.Id));
            var notificationsByOrder = notifications
                .GroupBy(n => n.OrderId)
                .ToDictionary(g => g.Key, g => g.Select(NotificationDto.From).ToList());

            var response = new GetMyOrdersResponse(request.CorrelationId())
            {
                Orders = orders.Select(order => new ShopperOrderDto
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString(),
                    OrderDate = order.OrderDate,
                    Total = order.Total(),
                    Items = order.OrderItems.Select(item => new ShopperOrderItemDto
                    {
                        CatalogItemId = item.ItemOrdered.CatalogItemId,
                        ProductName = item.ItemOrdered.ProductName,
                        UnitPrice = item.UnitPrice,
                        Units = item.Units
                    }).ToList(),
                    Notifications = notificationsByOrder.TryGetValue(order.Id, out var orderNotifications)
                        ? orderNotifications
                        : new List<NotificationDto>()
                }).ToList()
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return ex.ToResult();
        }
    }
}
