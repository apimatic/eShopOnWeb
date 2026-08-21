using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderRequest
{
    public int OrderId { get; set; }
    /// <summary>Optional partial amount. When omitted, the remaining refundable balance is refunded.</summary>
    public decimal? Amount { get; set; }
    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public OrderPaymentDto Payment { get; set; } = new();
}

/// <summary>
/// POST /api/orders/{orderId}/refunds — return after fulfilment: refund the captured payment in full or
/// in part. A partly-refunded order never becomes refundable beyond what was captured. Shopper-scoped.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService, user);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user)
    {
        var buyerId = CallerIdentity.BuyerId(user);

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new PaymentException("A refund requires an idempotencyKey.", PaymentErrorKind.Validation);

        var refund = await paymentService.RefundAsync(buyerId, request.OrderId, request.Amount, request.IdempotencyKey);

        // Reload the payment so the response reflects the updated refund totals.
        var payments = await paymentService.GetMyOrdersAsync(buyerId);
        var payment = payments.First(p => p.OrderId == request.OrderId);

        var response = new RefundOrderResponse
        {
            RefundId = refund.PayPalRefundId,
            OrderId = request.OrderId,
            Amount = refund.Amount,
            Status = refund.Status,
            Payment = PaymentMapper.ToDto(payment)
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.PayPalRefundId}", response);
    }
}
