using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Messaging;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

[ApiController]
[Route("api/orders")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly OrderNotificationService _service;

    public OrdersController(OrderNotificationService service) => _service = service;

    [HttpPost]
    public async Task<IActionResult> Place(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var address = request.ShippingAddress is null
            ? new Address("Not provided", "Not provided", string.Empty, "Not provided", "Not provided")
            : request.ShippingAddress.ToAddress();
        var result = await _service.PlaceOrderAsync(
            BuyerId(),
            request.Items.Select(x => new OrderLine(x.CatalogItemId, x.Quantity)).ToList(),
            address,
            cancellationToken);

        return Created($"/api/orders/{result.Order.Id}", new
        {
            orderId = result.Order.Id,
            status = result.Order.Status.ToString(),
            notifications = result.Notifications.Select(ToNotificationResponse)
        });
    }

    [HttpPost("{orderId:int}/dispatch")]
    [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Dispatch(int orderId, CancellationToken cancellationToken)
    {
        var result = await _service.DispatchOrderAsync(orderId, cancellationToken);
        return Ok(new
        {
            orderId = result.Order.Id,
            status = result.Order.Status.ToString(),
            notifications = result.Notifications.Select(ToNotificationResponse)
        });
    }

    [HttpPost("{orderId:int}/cancel")]
    [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var result = await _service.CancelOrderAsync(orderId, cancellationToken);
        return Ok(new
        {
            orderId = result.Order.Id,
            status = result.Order.Status.ToString(),
            followUpCancellationFailures = result.FollowUpCancellationFailures,
            notifications = result.Notifications.Select(ToNotificationResponse)
        });
    }

    [HttpGet("/api/my-orders")]
    public async Task<IActionResult> MyOrders(CancellationToken cancellationToken)
    {
        var orders = await _service.GetOrdersForBuyerAsync(BuyerId(), cancellationToken);
        return Ok(orders.Select(x => new
        {
            orderId = x.Order.Id,
            orderDate = x.Order.OrderDate,
            status = x.Order.Status.ToString(),
            total = x.Order.Total(),
            items = x.Order.OrderItems.Select(item => new
            {
                catalogItemId = item.ItemOrdered.CatalogItemId,
                productName = item.ItemOrdered.ProductName,
                unitPrice = item.UnitPrice,
                quantity = item.Units
            }),
            notifications = x.Notifications.Select(ToNotificationResponse)
        }));
    }

    [HttpGet("{orderId:int}/notifications")]
    public async Task<IActionResult> Notifications(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _service.GetOrderNotificationsForBuyerAsync(BuyerId(), orderId, cancellationToken);
        return notifications is null
            ? NotFound()
            : Ok(notifications.Select(ToNotificationResponse));
    }

    internal static object ToNotificationResponse(OrderNotification notification) => new
    {
        notificationId = notification.Id,
        kind = notification.Kind.ToString(),
        content = notification.Content,
        contentRedactedAt = notification.ContentRedactedAt,
        providerMessageSid = notification.ProviderMessageSid,
        deliveryStatus = notification.ProviderStatus,
        providerErrorCode = notification.ProviderErrorCode,
        createdAt = notification.CreatedAt,
        providerSentAt = notification.ProviderSentAt,
        scheduledFor = notification.ScheduledFor,
        resendOfNotificationId = notification.ResendOfNotificationId
    };

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new InvalidOperationException("The authenticated token has no name claim.");
}

public sealed class PlaceOrderRequest
{
    [Required, MinLength(1)]
    public List<PlaceOrderItemRequest> Items { get; init; } = new();

    public ShippingAddressRequest? ShippingAddress { get; init; }
}

public sealed class PlaceOrderItemRequest
{
    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; init; }

    [Range(1, 100)]
    public int Quantity { get; init; }
}

public sealed class ShippingAddressRequest
{
    [Required, StringLength(180)]
    public string Street { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string City { get; init; } = string.Empty;

    [StringLength(60)]
    public string State { get; init; } = string.Empty;

    [Required, StringLength(90)]
    public string Country { get; init; } = string.Empty;

    [Required, StringLength(18)]
    public string ZipCode { get; init; } = string.Empty;

    internal Address ToAddress() => new(Street, City, State, Country, ZipCode);
}
