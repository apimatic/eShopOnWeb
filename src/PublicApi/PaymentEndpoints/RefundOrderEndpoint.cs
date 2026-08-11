using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Refunds a captured order, in full or in part, under a caller-supplied idempotency key. Scoped to the
/// owning shopper. Repeating a request under the same key never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                CreateRefundRequest request,
                ClaimsPrincipal user,
                IOrderPaymentService service,
                CancellationToken ct) =>
            {
                var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    return Results.BadRequest(new { message = "An idempotencyKey is required for refunds." });
                }

                var outcome = await service.RefundAsync(buyerId, orderId, request.Amount, request.IdempotencyKey, ct);

                var response = new CreateRefundResponse
                {
                    RefundId = outcome.PayPalRefundId,
                    Status = outcome.Status,
                    Amount = outcome.Amount,
                    Currency = outcome.Currency
                };
                return Results.Created($"api/orders/{orderId}/refunds/{outcome.PayPalRefundId}", response);
            })
            .Produces<CreateRefundResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}
