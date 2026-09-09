using System;
using System.Security.Claims;
using System.Threading;
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
/// POST /api/orders/{orderId}/refunds — refunds the captured payment for the shopper's own order,
/// fully or partially. A caller-supplied idempotency key makes a repeated request return the original
/// refund; two distinct keys are two legitimate partial refunds.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                RefundRequest request,
                ClaimsPrincipal user,
                IPaymentOrderService service,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    return Results.BadRequest(new { message = "An idempotencyKey is required for refunds." });
                }

                try
                {
                    var refund = await service.RefundAsync(
                        user.GetBuyerId(), orderId, request.Amount, request.IdempotencyKey, ct);
                    return Results.Ok(new { refundId = refund.Id, refund = RefundDto.From(refund) });
                }
                catch (Exception ex)
                {
                    return PaymentProblems.ToResult(ex);
                }
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("OrderPaymentEndpoints");
    }
}
