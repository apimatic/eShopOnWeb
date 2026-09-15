using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Route("api/orders")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController(OrderNotificationApplicationService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(OrderCreatedResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderCreatedResponse>> Place(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var orderId = await service.PlaceOrderAsync(ShopperId(), request, cancellationToken);
        return Created($"/api/orders/{orderId}", new OrderCreatedResponse(orderId));
    }

    [HttpPost("{orderId:int}/dispatch")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)]
    public async Task<ActionResult<OrderStateResponse>> Dispatch(int orderId, CancellationToken cancellationToken)
    {
        var status = await service.DispatchOrderAsync(orderId, cancellationToken);
        return Ok(new OrderStateResponse(orderId, status.ToString()));
    }

    [HttpPost("{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)]
    public async Task<ActionResult<OrderStateResponse>> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var status = await service.CancelOrderAsync(orderId, cancellationToken);
        return Ok(new OrderStateResponse(orderId, status.ToString()));
    }

    [HttpGet("/api/my-orders")]
    [ProducesResponseType(typeof(IReadOnlyList<MyOrderResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MyOrderResponse>>> MyOrders(CancellationToken cancellationToken) =>
        Ok(await service.GetMyOrdersAsync(ShopperId(), cancellationToken));

    [HttpGet("{orderId:int}/notifications")]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<NotificationResponse>>> Notifications(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await service.GetOrderNotificationsAsync(ShopperId(), orderId, cancellationToken);
        return notifications is null ? NotFound() : Ok(notifications);
    }

    private string ShopperId() => User.Identity?.Name
        ?? throw new ApiRequestException(StatusCodes.Status401Unauthorized, "Authentication is required.");
}
