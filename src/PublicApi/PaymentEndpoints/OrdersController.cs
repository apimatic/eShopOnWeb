using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly PaymentWorkflow _workflow;

    public OrdersController(PaymentWorkflow workflow) => _workflow = workflow;

    [HttpPost("orders")]
    [ProducesResponseType(typeof(PlaceOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PlaceOrderResponse>> PlaceOrder(PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _workflow.PlaceOrderAsync(Caller(), request, cancellationToken);
        return Created($"/api/orders/{response.OrderId}", response);
    }

    [HttpPost("orders/{orderId:int}/pay")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _workflow.PayAsync(Caller(), orderId, request, cancellationToken));

    [HttpPost("orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId, CancellationToken cancellationToken) =>
        Ok(await _workflow.FulfilAsync(orderId, cancellationToken));

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId, CancellationToken cancellationToken) =>
        Ok(await _workflow.CancelAsync(orderId, cancellationToken));

    [HttpPost("orders/{orderId:int}/refunds")]
    [ProducesResponseType(typeof(RefundResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundResponse>> Refund(int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _workflow.RefundAsync(Caller(), orderId, request, cancellationToken);
        return Created($"/api/orders/{orderId}/refunds/{response.RefundId}", response);
    }

    [HttpGet("my-orders")]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> MyOrders(CancellationToken cancellationToken) =>
        Ok(await _workflow.MyOrdersAsync(Caller(), cancellationToken));

    private string Caller() => User.FindFirstValue(ClaimTypes.Name) ??
                               throw new ApiProblemException(StatusCodes.Status401Unauthorized,
                                   "CALLER_IDENTITY_MISSING", "The token does not identify a caller.");
}
