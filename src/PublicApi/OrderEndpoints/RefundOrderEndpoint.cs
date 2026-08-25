using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a fulfilled order's captured payment, in full or in part. Idempotent on the
/// caller-supplied idempotency key: repeating a request under the same key returns the original
/// refund rather than refunding twice, while distinct keys can issue distinct legitimate partial
/// refunds up to what was captured.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class RefundOrderEndpoint : EndpointBaseAsync
    .WithRequest<RefundOrderRequest>
    .WithActionResult<RefundOrderResponse>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public RefundOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    [HttpPost("api/orders/{orderId}/refunds")]
    [SwaggerOperation(
        Summary = "Refunds an order",
        Description = "Refunds the captured payment for one of the caller's own orders, in full or in part",
        OperationId = "orders.refund",
        Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<RefundOrderResponse>> HandleAsync(RefundOrderRequest request, CancellationToken cancellationToken = default)
    {
        var response = new RefundOrderResponse(request.CorrelationId());
        var buyerId = User.Identity!.Name!;

        var refund = await _orderPaymentService.RefundOrderAsync(
            buyerId,
            request.OrderId,
            request.Body.Amount,
            request.Body.IdempotencyKey,
            cancellationToken);

        response.OrderId = request.OrderId;
        response.RefundId = refund.Id;
        response.Refund = refund.ToDto();

        return response;
    }
}
