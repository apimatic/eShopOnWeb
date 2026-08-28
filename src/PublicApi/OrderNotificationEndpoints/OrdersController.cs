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

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api")]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderNotificationService _service;

    public OrdersController(IOrderNotificationService service) => _service = service;

    [HttpPost("orders")]
    [ProducesResponseType<OrderCreatedResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderCreatedResponse>> PlaceOrder(
        PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var items = request.Items?.Select(x => new OrderLineInput(x.CatalogItemId, x.Quantity)).ToList()
            ?? new List<OrderLineInput>();
        var address = request.ShippingAddress is null
            ? null
            : new ShippingAddressInput(
                request.ShippingAddress.Street,
                request.ShippingAddress.City,
                request.ShippingAddress.State,
                request.ShippingAddress.Country,
                request.ShippingAddress.ZipCode);
        var orderId = await _service.PlaceOrderAsync(BuyerId(), items, address, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new OrderCreatedResponse(orderId));
    }

    [HttpPost("orders/{orderId:int}/dispatch")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)]
    public async Task<ActionResult<OrderOperationResponse>> Dispatch(int orderId, CancellationToken cancellationToken)
    {
        await _service.DispatchOrderAsync(orderId, cancellationToken);
        return Ok(new OrderOperationResponse(orderId, "Dispatched"));
    }

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)]
    public async Task<ActionResult<OrderOperationResponse>> Cancel(int orderId, CancellationToken cancellationToken)
    {
        await _service.CancelOrderAsync(orderId, cancellationToken);
        return Ok(new OrderOperationResponse(orderId, "Cancelled"));
    }

    [HttpGet("my-orders")]
    public Task<IReadOnlyList<OrderView>> MyOrders(CancellationToken cancellationToken) =>
        _service.GetOrdersAsync(BuyerId(), cancellationToken);

    [HttpGet("orders/{orderId:int}/notifications")]
    public Task<IReadOnlyList<NotificationView>> Notifications(int orderId, CancellationToken cancellationToken) =>
        _service.GetOrderNotificationsAsync(BuyerId(), orderId, cancellationToken);

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name)!;
}

public sealed class PlaceOrderRequest
{
    public List<PlaceOrderItemRequest>? Items { get; set; }
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public sealed class PlaceOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public sealed record OrderCreatedResponse(int OrderId);
public sealed record OrderOperationResponse(int OrderId, string Status);
