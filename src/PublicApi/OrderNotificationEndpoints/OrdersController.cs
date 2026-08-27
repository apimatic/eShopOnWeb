using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderNotificationService _service;
    public OrdersController(IOrderNotificationService service) => _service = service;

    [HttpPost("api/orders")]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateOrderResponse>> Create(CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.ShipToAddress is null)
            throw new NotificationValidationException("Items and shipToAddress are required.");
        var address = new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
            request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);
        var order = await _service.PlaceOrderAsync(BuyerId, address,
            request.Items.Select(x => new OrderLineRequest(x.CatalogItemId, x.Quantity)).ToList(), cancellationToken);
        return Created($"/api/orders/{order.OrderId}", new CreateOrderResponse(order.OrderId,
            order.Status, order.Notifications));
    }

    [HttpPost("api/orders/{orderId:int}/dispatch")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResult>> Dispatch(int orderId, CancellationToken cancellationToken)
    {
        var result = await _service.DispatchOrderAsync(orderId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResult>> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var result = await _service.CancelOrderAsync(orderId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("api/my-orders")]
    public Task<IReadOnlyList<OrderResult>> MyOrders(CancellationToken cancellationToken) =>
        _service.GetOrdersAsync(BuyerId, cancellationToken);

    [HttpGet("api/orders/{orderId:int}/notifications")]
    public async Task<ActionResult<IReadOnlyList<NotificationResult>>> Notifications(int orderId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetNotificationsAsync(BuyerId, orderId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    private string BuyerId => User.Identity?.Name ?? string.Empty;
}

public sealed record CreateOrderLineRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country,
    string ZipCode);
public sealed record CreateOrderRequest(IReadOnlyList<CreateOrderLineRequest> Items,
    ShippingAddressRequest ShipToAddress);
public sealed record CreateOrderResponse(int OrderId, string Status,
    IReadOnlyList<NotificationResult> Notifications);
