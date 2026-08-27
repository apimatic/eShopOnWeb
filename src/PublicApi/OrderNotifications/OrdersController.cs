using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly OrderNotificationService _service;

    public OrdersController(OrderNotificationService service) => _service = service;

    [HttpPost("orders")]
    [ProducesResponseType(typeof(PlaceOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PlaceOrderResponse>> Place(PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.PlaceOrderAsync(BuyerId(), request, cancellationToken);
        return Created($"/api/orders/{response.OrderId}", response);
    }

    [HttpPost("orders/{orderId:int}/dispatch")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(OrderStateResponse), StatusCodes.Status200OK)]
    public Task<OrderStateResponse> Dispatch(int orderId, CancellationToken cancellationToken) =>
        _service.DispatchOrderAsync(orderId, cancellationToken);

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(OrderStateResponse), StatusCodes.Status200OK)]
    public Task<OrderStateResponse> Cancel(int orderId, CancellationToken cancellationToken) =>
        _service.CancelOrderAsync(orderId, cancellationToken);

    [HttpGet("my-orders")]
    [ProducesResponseType(typeof(MyOrdersResponse), StatusCodes.Status200OK)]
    public Task<MyOrdersResponse> MyOrders(CancellationToken cancellationToken) =>
        _service.GetMyOrdersAsync(BuyerId(), cancellationToken);

    [HttpGet("orders/{orderId:int}/notifications")]
    [ProducesResponseType(typeof(NotificationListResponse), StatusCodes.Status200OK)]
    public Task<NotificationListResponse> Notifications(int orderId,
        CancellationToken cancellationToken) =>
        _service.GetOrderNotificationsAsync(BuyerId(), orderId, cancellationToken);

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name) ??
        throw new NotificationApiException(StatusCodes.Status401Unauthorized,
            "The token does not contain a shopper identity.");
}
