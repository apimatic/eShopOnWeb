using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly PaymentWorkflowService _payments;

    public OrdersController(PaymentWorkflowService payments) => _payments = payments;

    [HttpPost("api/orders")]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateOrderResponse>> CreateOrder(CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _payments.CreateOrderAsync(CallerId(), request, cancellationToken);
        return Created($"/api/orders/{response.OrderId}", response);
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    [ProducesResponseType(typeof(PayOrderResponse), StatusCodes.Status200OK)]
    public Task<PayOrderResponse> Pay(int orderId, PayOrderRequest request, CancellationToken cancellationToken) =>
        _payments.PayAsync(CallerId(), orderId, request, cancellationToken);

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(PayOrderResponse), StatusCodes.Status200OK)]
    public Task<PayOrderResponse> Fulfil(int orderId, CancellationToken cancellationToken) =>
        _payments.FulfilAsync(orderId, cancellationToken);

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(PayOrderResponse), StatusCodes.Status200OK)]
    public Task<PayOrderResponse> Cancel(int orderId, CancellationToken cancellationToken) =>
        _payments.CancelAsync(orderId, cancellationToken);

    [HttpPost("api/orders/{orderId:int}/refunds")]
    [ProducesResponseType(typeof(RefundOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundOrderResponse>> Refund(int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _payments.RefundAsync(CallerId(), orderId, request, cancellationToken);
        return Created($"/api/orders/{orderId}/refunds/{response.RefundId}", response);
    }

    [HttpGet("api/my-orders")]
    [ProducesResponseType(typeof(IReadOnlyList<OrderResponse>), StatusCodes.Status200OK)]
    public Task<IReadOnlyList<OrderResponse>> MyOrders(CancellationToken cancellationToken) =>
        _payments.GetMyOrdersAsync(CallerId(), cancellationToken);

    private string CallerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new PaymentApiException(StatusCodes.Status401Unauthorized, "The token has no caller identity.");
}
