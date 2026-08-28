using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly PaymentService _payments;

    public OrdersController(PaymentService payments) => _payments = payments;

    [HttpPost("api/orders")]
    public async Task<ActionResult<PlaceOrderResponse>> PlaceOrder(PlaceOrderRequest request, CancellationToken ct)
    {
        var response = await _payments.PlaceOrderAsync(OwnerId(), request, ct);
        return Created($"/api/orders/{response.OrderId}", response);
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    public Task<PaymentStateResponse> Pay(int orderId, PayOrderRequest request, CancellationToken ct) =>
        _payments.PayAsync(OwnerId(), orderId, request, ct);

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<PaymentStateResponse> Fulfil(int orderId, CancellationToken ct) =>
        _payments.FulfilAsync(orderId, ct);

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<PaymentStateResponse> Cancel(int orderId, CancellationToken ct) =>
        _payments.CancelAsync(orderId, ct);

    [HttpPost("api/orders/{orderId:int}/refunds")]
    public async Task<ActionResult<RefundResponse>> Refund(int orderId, RefundOrderRequest request,
        CancellationToken ct)
    {
        var response = await _payments.RefundAsync(OwnerId(), orderId, request, ct);
        return Ok(response);
    }

    [HttpGet("api/my-orders")]
    public Task<System.Collections.Generic.IReadOnlyList<PaymentStateResponse>> MyOrders(CancellationToken ct) =>
        _payments.MyOrdersAsync(OwnerId(), ct);

    [HttpGet("api/reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<ReconciliationResponse> Reconciliation([FromQuery] string from, [FromQuery] string to,
        CancellationToken ct)
    {
        if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start) ||
            !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var end))
            throw new PaymentOperationException(400, "from and to must be ISO-8601 date-times.");
        return _payments.ReconcileAsync(start, end, ct);
    }

    private string OwnerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new PaymentOperationException(401, "The token does not contain a shopper identity.");
}
