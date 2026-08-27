using System.Linq;
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
/// Lists the signed-in shopper's orders, each with where its notifications got to.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _notificationService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMyOrdersEndpoint(IRepository<Order> orderRepository,
        IOrderNotificationService notificationService,
        IHttpContextAccessor httpContextAccessor)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
            {
                return await HandleAsync();
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var ordersSpec = new CustomerOrdersWithItemsSpecification(buyerId);
        var orders = await _orderRepository.ListAsync(ordersSpec);

        var response = new ListMyOrdersResponse();
        foreach (var order in orders)
        {
            var notifications = await _notificationService.GetOrderNotificationsAsync(order.Id);
            response.Orders.Add(new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Items = order.OrderItems.Select(i => new MyOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Notifications = notifications.Select(ToDto).ToList()
            });
        }

        return Results.Ok(response);
    }

    internal static OrderNotificationDto ToDto(OrderNotification notification)
    {
        return new OrderNotificationDto
        {
            NotificationId = notification.Id,
            NotificationType = notification.NotificationType.ToString(),
            Status = notification.ProviderStatus,
            ErrorCode = notification.ProviderErrorCode,
            ProviderMessageSid = notification.ProviderMessageSid,
            CreatedAt = notification.CreatedAt,
            ScheduledFor = notification.ScheduledFor,
            ContentRedacted = notification.ContentRedacted,
            Body = notification.ContentRedacted ? null : notification.Body,
            ResendOfNotificationId = notification.ResendOfNotificationId
        };
    }
}
