using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : EndpointBaseAsync
    .WithRequest<RefundOrderRequest>
    .WithActionResult<RefundDto>
{
    private readonly IPaymentService _paymentService;

    public RefundOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost("api/orders/{orderId:int}/refunds")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Refund a captured order payment",
        Description = "Refunds the shopper's captured payment in full or in part. Repeating a request with the same idempotencyKey does not refund twice.",
        OperationId = "orders.refunds",
        Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<RefundDto>> HandleAsync(
        RefundOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var buyerId = PaymentApiMapper.BuyerId(User);
            var orderId = PaymentApiMapper.RouteId(RouteData, "orderId");
            var amount = request.AmountInCents.HasValue
                ? request.AmountInCents.Value / 100m
                : request.Amount ?? request.RefundAmount ?? 0m;
            var refund = await _paymentService.RefundOrderAsync(
                buyerId,
                orderId,
                amount,
                request.IdempotencyKey,
                cancellationToken);

            return Ok(refund);
        }
        catch (System.Exception ex)
        {
            return PaymentApiMapper.ErrorResult(ex);
        }
    }
}

public class RefundOrderRequest : BaseRequest
{
    public decimal? Amount { get; set; }

    public long? AmountInCents { get; set; }

    public decimal? RefundAmount { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}
