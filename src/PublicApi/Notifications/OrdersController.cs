using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly OrderNotificationService _service;

    public OrdersController(OrderNotificationService service) => _service = service;

    [HttpPost("api/orders")]
    public async Task<ActionResult<PlaceOrderResponse>> Place(PlaceOrderRequest request, CancellationToken ct)
    {
        try
        {
            var order = await _service.PlaceOrderAsync(User.Identity!.Name!, request, ct);
            return Created($"/api/orders/{order.Id}", new PlaceOrderResponse(order.Id));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message, Status = StatusCodes.Status400BadRequest });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = ex.Message, Status = StatusCodes.Status404NotFound });
        }
    }

    [HttpPost("api/orders/{orderId:int}/dispatch")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderActionResponse>> Dispatch(int orderId, CancellationToken ct)
    {
        try
        {
            var result = await _service.DispatchAsync(orderId, ct);
            return result.Order is null
                ? NotFound()
                : Ok(new OrderActionResponse(result.Order.Id, result.Order.Status.ToString()));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails { Title = ex.Message, Status = StatusCodes.Status409Conflict });
        }
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderActionResponse>> Cancel(int orderId, CancellationToken ct)
    {
        var result = await _service.CancelAsync(orderId, ct);
        return result.Order is null
            ? NotFound()
            : Ok(new OrderActionResponse(result.Order.Id, result.Order.Status.ToString()));
    }

    [HttpGet("api/my-orders")]
    public async Task<ActionResult> MyOrders(CancellationToken ct) =>
        Ok(await _service.GetMyOrdersAsync(User.Identity!.Name!, ct));

    [HttpGet("api/orders/{orderId:int}/notifications")]
    public async Task<ActionResult> Notifications(int orderId, CancellationToken ct)
    {
        var result = await _service.GetOrderNotificationsAsync(User.Identity!.Name!, orderId, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
