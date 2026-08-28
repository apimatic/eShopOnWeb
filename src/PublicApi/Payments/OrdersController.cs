using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Produces("application/json")]
public sealed class OrdersController(PaymentApplicationService payments) : ControllerBase
{
    [HttpPost("api/orders")]
    [ProducesResponseType(typeof(PlaceOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PlaceOrderResponse>> PlaceOrder(
        [FromBody] PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var response = await payments.PlaceOrderAsync(BuyerId(), request, cancellationToken);
        return Created($"/api/orders/{response.OrderId}", response);
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    [ProducesResponseType(typeof(PaymentStateResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaymentStateResponse>> Pay(
        int orderId, [FromBody] PayOrderRequest request, CancellationToken cancellationToken)
    {
        var response = await payments.PayAsync(BuyerId(), orderId, request, cancellationToken);
        return response.PaymentStatus is "AuthorizationPending"
            ? Accepted($"/api/my-orders", response)
            : Ok(response);
    }

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(PaymentStateResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaymentStateResponse>> Fulfil(int orderId, CancellationToken cancellationToken)
    {
        var response = await payments.FulfilAsync(orderId, cancellationToken);
        return response.PaymentStatus == "CapturePending"
            ? Accepted($"/api/orders/{orderId}/fulfil", response)
            : Ok(response);
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(PaymentStateResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaymentStateResponse>> Cancel(int orderId, CancellationToken cancellationToken) =>
        Ok(await payments.CancelAsync(orderId, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/refunds")]
    [ProducesResponseType(typeof(RefundResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundResponse>> Refund(
        int orderId,
        [FromBody] RefundOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var response = await payments.RefundAsync(BuyerId(), orderId, request.Amount, idempotencyKey,
            cancellationToken);
        return Created($"/api/orders/{orderId}/refunds/{response.RefundId}", response);
    }

    [HttpGet("api/my-orders")]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentStateResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PaymentStateResponse>>> MyOrders(CancellationToken cancellationToken) =>
        Ok(await payments.MyOrdersAsync(BuyerId(), cancellationToken));

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new PaymentApiException(StatusCodes.Status401Unauthorized, "identity_missing",
            "The authenticated token does not contain a shopper identity.");
}
