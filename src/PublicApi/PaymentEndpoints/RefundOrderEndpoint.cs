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
/// POST /api/orders/{orderId}/refunds — refund a captured order, in full or in part. The
/// caller-supplied idempotency key makes a repeated request return the same refund; two
/// distinct keys produce two legitimate partial refunds. Never refundable beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundRequest request, IOrderPaymentService service, ClaimsPrincipal user) =>
            {
                var identity = CallerIdentity.Require(user);
                if (string.IsNullOrWhiteSpace(request?.IdempotencyKey))
                {
                    return Results.BadRequest(new { message = "An 'idempotencyKey' is required for refunds." });
                }

                var refund = await service.RefundAsync(identity, orderId, request.Amount, request.IdempotencyKey);

                // refundId is returned as a top-level field so the flow can be driven end to end.
                return Results.Ok(new
                {
                    refundId = refund.Id,
                    payPalRefundId = refund.PayPalRefundId,
                    amount = refund.Amount,
                    currencyCode = refund.CurrencyCode,
                    status = refund.Status
                });
            })
            .WithTags("OrderPaymentEndpoints");
    }
}
