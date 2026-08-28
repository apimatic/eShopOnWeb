using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Route("api/orders")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly OrderNotificationService _service;

    public OrdersController(OrderNotificationService service) => _service = service;

    [HttpPost]
    [ProducesResponseType(typeof(PlaceOrderResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Place(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.PlaceOrderAsync(User.Identity!.Name!, request, cancellationToken);
        if (!result.Succeeded)
        {
            return ToProblem(result.Error, result.Message!);
        }

        var response = new PlaceOrderResponse(result.Value!.Id);
        return Created($"/api/orders/{response.OrderId}", response);
    }

    [HttpPost("{orderId:int}/dispatch")]
    [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Dispatch(int orderId, CancellationToken cancellationToken)
    {
        var result = await _service.DispatchOrderAsync(orderId, cancellationToken);
        return result.Succeeded
            ? Ok(new ChangeOrderStateResponse(result.Value!.Id, result.Value.Status.ToString()))
            : ToProblem(result.Error, result.Message!);
    }

    [HttpPost("{orderId:int}/cancel")]
    [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var result = await _service.CancelOrderAsync(orderId, cancellationToken);
        return result.Succeeded
            ? Ok(new ChangeOrderStateResponse(result.Value!.Id, result.Value.Status.ToString()))
            : ToProblem(result.Error, result.Message!);
    }

    [HttpGet("/api/my-orders")]
    [ProducesResponseType(typeof(MyOrderDto[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> MyOrders(CancellationToken cancellationToken) =>
        Ok(await _service.GetMyOrdersAsync(User.Identity!.Name!, cancellationToken));

    [HttpGet("{orderId:int}/notifications")]
    [ProducesResponseType(typeof(NotificationDto[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> Notifications(int orderId, CancellationToken cancellationToken)
    {
        var result = await _service.GetOrderNotificationsAsync(User.Identity!.Name!, orderId, cancellationToken);
        return result.Succeeded
            ? Ok(result.Value!.Select(OrderNotificationService.ToDto))
            : ToProblem(result.Error, result.Message!);
    }

    private ObjectResult ToProblem(OperationError error, string message) => Problem(
        statusCode: error switch
        {
            OperationError.Invalid => StatusCodes.Status400BadRequest,
            OperationError.NotFound => StatusCodes.Status404NotFound,
            OperationError.Conflict => StatusCodes.Status409Conflict,
            OperationError.ProviderUnavailable => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status500InternalServerError
        },
        title: message);
}
