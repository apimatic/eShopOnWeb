using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IOrderNotificationService service, CancellationToken cancellationToken) =>
            {
                var userName = http.GetUserName();
                if (string.IsNullOrEmpty(userName))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new ListMyOrdersRequest(), service, userName, cancellationToken);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ListMyOrdersRequest request, IOrderNotificationService service)
        => HandleAsync(request, service, string.Empty, CancellationToken.None);

    private async Task<IResult> HandleAsync(
        ListMyOrdersRequest request,
        IOrderNotificationService service,
        string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await service.ListBuyerOrdersAsync(buyerId, cancellationToken);
        var notifications = await service.ListNotificationsForOrdersAsync(orders.Select(o => o.Id).ToList(), cancellationToken);
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new ListMyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(order => new ShopperOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Items = order.OrderItems.Select(i => new ShopperOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    Name = i.ItemOrdered.ProductName,
                    Quantity = i.Units,
                    UnitPrice = i.UnitPrice
                }).ToList(),
                Notifications = byOrder.TryGetValue(order.Id, out var notes)
                    ? notes.Select(NotificationMapper.ToDto).ToList()
                    : new List<OrderNotificationDto>()
            }).ToList()
        };
        return Results.Ok(response);
    }
}

internal static class NotificationMapper
{
    public static OrderNotificationDto ToDto(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        Kind = notification.Kind.ToString(),
        Status = notification.Status,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        ProviderSid = notification.ProviderSid,
        Body = notification.ContentRedacted ? null : notification.Body,
        ContentRedacted = notification.ContentRedacted,
        CreatedUtc = notification.CreatedUtc,
        ScheduledSendAt = notification.ScheduledSendAt,
        DateSent = notification.DateSent
    };
}
