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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderNotificationService _service;
    public OrdersController(IOrderNotificationService service) => _service = service;

    [HttpPost("orders")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Place(PlaceOrderRequest request, CancellationToken ct)
    {
        var command = new PlaceOrderCommand(
            request.Items.Select(x => new PlaceOrderLine(x.CatalogItemId, x.Quantity)).ToList(),
            new ShippingAddress(request.ShippingAddress.Street, request.ShippingAddress.City,
                request.ShippingAddress.State, request.ShippingAddress.Country, request.ShippingAddress.ZipCode));
        var orderId = await _service.PlaceOrderAsync(BuyerId(), command, ct);
        return Created($"/api/orders/{orderId}", new { orderId });
    }

    [HttpPost("orders/{orderId:int}/dispatch")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Dispatch(int orderId, CancellationToken ct) =>
        await _service.DispatchOrderAsync(orderId, ct) ? Ok(new { orderId, progress = "Dispatched" }) : NotFound();

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Cancel(int orderId, CancellationToken ct) =>
        await _service.CancelOrderAsync(orderId, ct) ? Ok(new { orderId, progress = "Cancelled" }) : NotFound();

    [HttpGet("my-orders")]
    public Task<IReadOnlyList<OrderView>> MyOrders(CancellationToken ct) =>
        _service.GetMyOrdersAsync(BuyerId(), ct);

    [HttpGet("orders/{orderId:int}/notifications")]
    public async Task<IActionResult> Notifications(int orderId, CancellationToken ct)
    {
        var notifications = await _service.GetOrderNotificationsAsync(BuyerId(), orderId, ct);
        return notifications is null ? NotFound() : Ok(notifications);
    }

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new UnauthorizedAccessException("The token has no shopper identity.");
}

public sealed record PlaceOrderRequest(IReadOnlyList<PlaceOrderItemRequest> Items, ShippingAddressRequest ShippingAddress);
public sealed record PlaceOrderItemRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
