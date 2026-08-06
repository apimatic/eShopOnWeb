using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Fully refunds the shopper's paid order. Idempotent: a repeated call never refunds twice. (Flow 1)
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class RefundOrderEndpoint : EndpointBaseAsync
    .WithRequest<RefundOrderRequest>
    .WithActionResult<RefundOrderResponse>
{
    private readonly IPaymentService _paymentService;

    public RefundOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost("api/orders/{orderId}/refunds")]
    [SwaggerOperation(
        Summary = "Refunds an order in full",
        Description = "Issues a full PayPal refund of the order's captured payment. Idempotent per order.",
        OperationId = "orders.refund",
        Tags = new[] { "PaymentEndpoints" })]
    public override async Task<ActionResult<RefundOrderResponse>> HandleAsync(
        RefundOrderRequest request, CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }

        var order = await _paymentService.RefundOrderAsync(request.OrderId, buyerId, cancellationToken);

        var response = new RefundOrderResponse
        {
            OrderId = order.Id,
            PaymentStatus = order.PaymentStatus.ToString(),
            RefundId = order.PaymentRefundId,
            Order = order.ToSummary()
        };

        return Ok(response);
    }
}
