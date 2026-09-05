using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using BlazorShared.Authorization;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: refunds the captured payment, in full or in part. A partly-refunded
/// order can never be refunded beyond what was captured, and the same idempotency key
/// never produces a second refund.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
    {
        var result = await paymentService.RefundOrderAsync(request.OrderId, request.Amount, request.IdempotencyKey);
        if (!result.Succeeded)
        {
            return PaymentEndpointHelpers.FromError(result.Error!);
        }

        var refund = result.Refund!;
        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            Currency = refund.Currency,
            TotalRefunded = result.Payment!.RefundedAmountCommitted
        };

        return Results.Ok(response);
    }
}



