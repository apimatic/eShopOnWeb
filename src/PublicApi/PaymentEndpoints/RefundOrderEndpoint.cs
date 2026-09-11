using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/refunds — refund a captured payment, in full or in part. The refund
/// carries a caller-supplied idempotency key; repeating under the same key never refunds twice.
/// Scoped to the caller's own order.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user) =>
            {
                request ??= new RefundOrderRequest();
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService, user);
            })
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user)
    {
        var buyerId = PaymentApi.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var result = await paymentService.RefundOrderAsync(buyerId, request.OrderId, request.Amount, request.IdempotencyKey);
        return PaymentApi.ToHttp(result, refund => new
        {
            refundId = refund.PayPalRefundId,
            orderId = request.OrderId,
            amount = refund.Amount,
            currency = refund.CurrencyCode,
            status = refund.Status
        });
    }
}
