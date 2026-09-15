using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Route("api/orders")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersPaymentController : ControllerBase
{
    private readonly CommerceService _service;

    public OrdersPaymentController(CommerceService service) => _service = service;

    [HttpPost]
    [ProducesResponseType<PlaceOrderResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PlaceOrderResponse>> PlaceOrder(PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.PlaceOrderAsync(UserName(), request, cancellationToken);
        return Created($"/api/orders/{response.OrderId}", response);
    }

    [HttpPost("{orderId:int}/pay")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _service.PayAsync(UserName(), orderId, request, cancellationToken));

    [HttpPost("{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId,
        CancellationToken cancellationToken) =>
        Ok(await _service.FulfilAsync(orderId, cancellationToken));

    [HttpPost("{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId,
        CancellationToken cancellationToken) =>
        Ok(await _service.CancelAsync(orderId, cancellationToken));

    [HttpPost("{orderId:int}/refunds")]
    [ProducesResponseType<RefundOrderResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundOrderResponse>> Refund(int orderId,
        RefundOrderRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.RefundAsync(UserName(), orderId, request, cancellationToken);
        return Created($"/api/orders/{orderId}/refunds/{response.RefundId}", response);
    }

    private string UserName() => User.Identity?.Name
        ?? throw new UnauthorizedAccessException("The bearer token has no name claim.");
}
