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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private const string Administrators = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    private readonly NotificationWorkflowService _workflow;

    public OrdersController(NotificationWorkflowService workflow)
    {
        _workflow = workflow;
    }

    [HttpPost("api/orders")]
    [ProducesResponseType(typeof(PlaceOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PlaceOrderResponse>> Place(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var lines = request.Items.Select(x => new OrderLineInput(x.CatalogItemId, x.Quantity)).ToList();
        var address = request.ShippingAddress is null
            ? null
            : new ShippingAddressInput(
                request.ShippingAddress.Street,
                request.ShippingAddress.City,
                request.ShippingAddress.State,
                request.ShippingAddress.Country,
                request.ShippingAddress.ZipCode);
        var order = await _workflow.PlaceOrderAsync(ShopperId(), lines, address, cancellationToken);
        return Created($"/api/orders/{order.Id}", new PlaceOrderResponse(order.Id));
    }

    [HttpPost("api/orders/{orderId:int}/dispatch")]
    [Authorize(Roles = Administrators, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderTransitionResponse>> Dispatch(int orderId, CancellationToken cancellationToken)
    {
        var order = await _workflow.DispatchOrderAsync(orderId, cancellationToken);
        return order is null
            ? NotFound()
            : Ok(new OrderTransitionResponse(order.Id, order.Status.ToString()));
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = Administrators, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderTransitionResponse>> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var order = await _workflow.CancelOrderAsync(orderId, cancellationToken);
        return order is null
            ? NotFound()
            : Ok(new OrderTransitionResponse(order.Id, order.Status.ToString()));
    }

    [HttpGet("api/my-orders")]
    public async Task<ActionResult<IReadOnlyList<MyOrderResponse>>> MyOrders(CancellationToken cancellationToken)
    {
        var shopperId = ShopperId();
        var orders = await _workflow.GetShopperOrdersAsync(shopperId, cancellationToken);
        var notifications = await _workflow.GetNotificationsForOrdersAsync(
            shopperId,
            orders.Select(x => x.Id).ToList(),
            cancellationToken);
        return Ok(orders.Select(order => MapOrder(order, notifications.GetValueOrDefault(order.Id, new List<OrderNotification>()))));
    }

    [HttpGet("api/orders/{orderId:int}/notifications")]
    public async Task<ActionResult<IReadOnlyList<NotificationResponse>>> Notifications(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _workflow.GetOrderNotificationsAsync(ShopperId(), orderId, cancellationToken);
        return Ok(notifications.Select(NotificationResponse.FromEntity));
    }

    private static MyOrderResponse MapOrder(
        Order order,
        IReadOnlyList<OrderNotification> notifications) => new(
        order.Id,
        order.OrderDate,
        order.Status.ToString(),
        order.Total(),
        order.OrderItems.Select(x => new MyOrderItemResponse(
            x.ItemOrdered.CatalogItemId,
            x.ItemOrdered.ProductName,
            x.UnitPrice,
            x.Units)).ToList(),
        notifications.Select(NotificationResponse.FromEntity).ToList());

    private string ShopperId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new UnauthorizedAccessException();
}
