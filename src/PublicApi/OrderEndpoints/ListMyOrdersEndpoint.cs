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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's orders, each with where its notifications got to
/// (last known outcome; the per-order notifications endpoint refreshes live from the provider).
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IOrderNotificationService, IRepository<OrderNotification>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IOrderNotificationService orderNotificationService, IRepository<OrderNotification> notificationRepository) =>
            {
                return await HandleAsync(new ListMyOrdersRequest { BuyerId = httpContext.User.GetBuyerId() }, orderNotificationService, notificationRepository);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IOrderNotificationService orderNotificationService,
        IRepository<OrderNotification> notificationRepository)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await orderNotificationService.ListMyOrdersAsync(request.BuyerId);

        var response = new ListMyOrdersResponse();
        foreach (var order in orders)
        {
            var notifications = await notificationRepository.ListAsync(new NotificationsByOrderSpecification(order.Id));
            response.Orders.Add(new OrderSummaryDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Items = order.OrderItems.Select(i => new OrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    Units = i.Units,
                    UnitPrice = i.UnitPrice
                }).ToList(),
                Notifications = notifications.Select(ToDto).ToList()
            });
        }

        return Results.Ok(response);
    }

    internal static NotificationDto ToDto(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        Kind = notification.Kind.ToString(),
        Status = notification.Status,
        MessageSid = notification.MessageSid,
        ScheduledFor = notification.ScheduledFor,
        DateSent = notification.DateSent,
        ErrorCode = notification.ProviderErrorCode,
        IsContentRedacted = notification.IsContentRedacted,
        CreatedAt = notification.CreatedAt
    };
}

public class ListMyOrdersRequest : BaseRequest
{
    public string? BuyerId { get; set; }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}
