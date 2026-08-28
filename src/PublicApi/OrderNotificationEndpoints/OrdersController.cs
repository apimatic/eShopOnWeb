using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Notifications;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private const string Administrators = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    private readonly OrderNotificationService _service;

    public OrdersController(OrderNotificationService service) => _service = service;

    [HttpPost("orders")]
    public async Task<IActionResult> Place(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var items = request.Items.Select(x => new OrderLineInput(x.CatalogItemId, x.Quantity)).ToList();
            var order = await _service.PlaceOrderAsync(BuyerId(), items, cancellationToken);
            return Created($"/api/orders/{order.Id}", new { orderId = order.Id });
        }
        catch (WorkflowValidationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("orders/{orderId:int}/dispatch")]
    [Authorize(Roles = Administrators, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Dispatch(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _service.DispatchOrderAsync(orderId, cancellationToken);
            return order is null ? NotFound() : Ok(new { orderId = order.Id, status = order.Status.ToString() });
        }
        catch (WorkflowConflictException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = Administrators, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Cancel(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _service.CancelOrderAsync(orderId, cancellationToken);
            return order is null ? NotFound() : Ok(new { orderId = order.Id, status = order.Status.ToString() });
        }
        catch (WorkflowConflictException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpGet("my-orders")]
    public async Task<IActionResult> MyOrders(CancellationToken cancellationToken)
    {
        var orders = await _service.GetOrdersAsync(BuyerId(), cancellationToken);
        var notificationMap = await _service.GetNotificationMapAsync(orders.Select(x => x.Id).ToArray(), cancellationToken);
        return Ok(new
        {
            orders = orders.Select(order => new
            {
                orderId = order.Id,
                orderDate = order.OrderDate,
                status = order.Status.ToString(),
                total = order.Total(),
                items = order.OrderItems.Select(item => new
                {
                    catalogItemId = item.ItemOrdered.CatalogItemId,
                    productName = item.ItemOrdered.ProductName,
                    quantity = item.Units,
                    unitPrice = item.UnitPrice
                }),
                notifications = notificationMap.TryGetValue(order.Id, out var notifications)
                    ? notifications.Select(NotificationResponse.From)
                    : Array.Empty<NotificationResponse>()
            })
        });
    }

    [HttpGet("orders/{orderId:int}/notifications")]
    public async Task<IActionResult> Notifications(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _service.GetOrderNotificationsAsync(BuyerId(), orderId, cancellationToken);
        return notifications is null
            ? NotFound()
            : Ok(new { orderId, notifications = notifications.Select(NotificationResponse.From) });
    }

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new UnauthorizedAccessException("The token has no name claim.");
}

public sealed class PlaceOrderRequest
{
    public List<PlaceOrderItemRequest> Items { get; set; } = new();
}

public sealed class PlaceOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
