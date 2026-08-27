using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.Infrastructure.Notifications;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly OrderNotificationCoordinator _coordinator;

    public OrdersController(OrderNotificationCoordinator coordinator) => _coordinator = coordinator;

    [HttpPost("orders")]
    public async Task<IResult> PlaceOrder(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _coordinator.PlaceOrderAsync(
                UserName(),
                request.Items.Select(x => new OrderLineInput(x.CatalogItemId, x.Quantity)).ToList(),
                request.ShippingAddress is null
                    ? null
                    : new AddressInput(
                        request.ShippingAddress.Street,
                        request.ShippingAddress.City,
                        request.ShippingAddress.State,
                        request.ShippingAddress.Country,
                        request.ShippingAddress.ZipCode),
                cancellationToken);
            return Results.Created($"/api/orders/{order.Id}", new { orderId = order.Id });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("orders/{orderId:int}/dispatch")]
    [Authorize(
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IResult> Dispatch(int orderId, CancellationToken cancellationToken) =>
        ActionResult(await _coordinator.DispatchOrderAsync(orderId, cancellationToken), orderId, "dispatched");

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IResult> Cancel(int orderId, CancellationToken cancellationToken) =>
        ActionResult(await _coordinator.CancelOrderAsync(orderId, cancellationToken), orderId, "cancelled");

    [HttpGet("my-orders")]
    public async Task<IResult> MyOrders(CancellationToken cancellationToken)
    {
        var orders = await _coordinator.GetOrdersAsync(UserName(), cancellationToken);
        var summaries = await _coordinator.GetNotificationSummariesAsync(orders.Select(x => x.Id).ToList(), cancellationToken);
        return Results.Ok(new
        {
            orders = orders.Select(order => new
            {
                orderId = order.Id,
                orderDate = order.OrderDate,
                status = order.Status.ToString().ToLowerInvariant(),
                total = order.Total(),
                items = order.OrderItems.Select(x => new
                {
                    catalogItemId = x.ItemOrdered.CatalogItemId,
                    name = x.ItemOrdered.ProductName,
                    quantity = x.Units,
                    unitPrice = x.UnitPrice
                }),
                notifications = summaries.TryGetValue(order.Id, out var values)
                    ? values.Select(NotificationSummary)
                    : Enumerable.Empty<object>()
            })
        });
    }

    [HttpGet("orders/{orderId:int}/notifications")]
    public async Task<IResult> Notifications(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _coordinator.GetOrderNotificationsAsync(UserName(), orderId, cancellationToken);
        if (notifications is null) return Results.NotFound();
        return Results.Ok(new
        {
            orderId,
            notifications = notifications.Select(NotificationDetails)
        });
    }

    private static object NotificationSummary(OrderNotification x) => new
    {
        notificationId = x.Id,
        type = x.Kind.ToString(),
        status = x.ProviderStatus,
        scheduledFor = x.ScheduledFor,
        lastCheckedAt = x.LastCheckedAt
    };

    private static object NotificationDetails(OrderNotification x) => new
    {
        notificationId = x.Id,
        type = x.Kind.ToString(),
        content = x.Content,
        contentDisposedAt = x.ContentDisposedAt,
        providerMessageId = x.ProviderMessageSid,
        status = x.ProviderStatus,
        providerErrorCode = x.ProviderErrorCode,
        providerErrorMessage = x.ProviderErrorMessage,
        createdAt = x.CreatedAt,
        scheduledFor = x.ScheduledFor,
        providerCreatedAt = x.ProviderCreatedAt,
        providerSentAt = x.ProviderSentAt,
        lastCheckedAt = x.LastCheckedAt,
        sourceNotificationId = x.SourceNotificationId,
        cancellationPending = x.CancellationPending
    };

    private static IResult ActionResult(OrderActionResult result, int orderId, string status) => result switch
    {
        OrderActionResult.Success => Results.Ok(new { orderId, status }),
        OrderActionResult.NotFound => Results.NotFound(),
        _ => Results.Conflict(new { error = "The order cannot make that state transition." })
    };

    private string UserName() => User.Identity?.Name ?? throw new UnauthorizedAccessException();
}

public sealed class PlaceOrderRequest
{
    public List<PlaceOrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public sealed class PlaceOrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}
