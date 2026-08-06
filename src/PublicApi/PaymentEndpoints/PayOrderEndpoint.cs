using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Pays for the shopper's order with PayPal, using either one-off card details or a saved card.
/// Idempotent: a repeated call never charges twice. (Flow 1)
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class PayOrderEndpoint : EndpointBaseAsync
    .WithRequest<PayOrderRequest>
    .WithActionResult<PayOrderResponse>
{
    private readonly IPaymentService _paymentService;

    public PayOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost("api/orders/{orderId}/pay")]
    [SwaggerOperation(
        Summary = "Pays for an order with PayPal",
        Description = "Pays with one-off card details or a saved card id. Idempotent per order.",
        OperationId = "orders.pay",
        Tags = new[] { "PaymentEndpoints" })]
    public override async Task<ActionResult<PayOrderResponse>> HandleAsync(
        PayOrderRequest request, CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }

        var body = request.Body ?? new PayOrderBody();
        var hasCard = body.Card is not null;
        var hasSaved = body.PaymentMethodId.HasValue;

        if (hasCard == hasSaved)
        {
            return BadRequest("Provide either card details or a saved paymentMethodId (exactly one).");
        }

        var instruction = hasSaved
            ? PaymentInstruction.WithSavedCard(body.PaymentMethodId!.Value)
            : PaymentInstruction.WithNewCard(body.Card!.ToCardDetails());

        var order = await _paymentService.PayOrderAsync(request.OrderId, buyerId, instruction, cancellationToken);

        var response = new PayOrderResponse
        {
            OrderId = order.Id,
            PaymentStatus = order.PaymentStatus.ToString(),
            PayPalOrderId = order.PayPalOrderId,
            CaptureId = order.PaymentCaptureId,
            Order = order.ToSummary()
        };

        return Ok(response);
    }
}
