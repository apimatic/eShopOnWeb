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

/// <summary>
/// POST /api/orders/{orderId}/refunds — returns a captured payment in full or in part. Scoped to the
/// caller's own order (operators may act on any). Carries a caller-supplied idempotency key so repeats
/// never refund twice, while distinct partial refunds remain legitimate.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.Caller = CallerContext.From(user);
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new PaymentException("An 'idempotencyKey' is required for refunds.");

        var (refundId, payment) = await paymentService.RefundAsync(
            request.OrderId, request.Caller.Username, request.Caller.IsAdmin,
            request.Amount, request.IdempotencyKey, request.NoteToPayer);

        var created = payment.Refunds.First(r => r.RefundId == refundId);
        var response = new RefundOrderResponse
        {
            RefundId = refundId,
            PayPalRefundId = created.PayPalRefundId,
            Amount = created.Amount,
            Currency = created.Currency,
            Status = created.Status,
            Payment = payment
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refundId}", response);
    }
}
