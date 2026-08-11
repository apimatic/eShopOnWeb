using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a captured payment, in full or in part, after fulfilment. The caller supplies an
/// idempotency key (body or <c>Idempotency-Key</c> header); repeating it never refunds twice, while
/// distinct keys make legitimate separate partial refunds. Shopper-scoped to the order's owner.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, HttpContext http, IPaymentService paymentService, CancellationToken ct) =>
            {
                var buyerId = CurrentUser.BuyerId(user);

                var idempotencyKey = request.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(idempotencyKey) && http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                {
                    idempotencyKey = header.ToString();
                }
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    throw new PaymentException("A refund requires an idempotency key (request body 'idempotencyKey' or the 'Idempotency-Key' header).");
                }

                var (payment, refund) = await paymentService.RefundAsync(buyerId, orderId, request.Amount, idempotencyKey.Trim(), ct);

                var response = new RefundOrderResponse
                {
                    RefundId = refund.PayPalRefundId,
                    Status = refund.Status,
                    Amount = refund.Amount,
                    Currency = payment.CurrencyCode,
                    Payment = PaymentView.From(payment)
                };
                return Results.Created($"api/orders/{orderId}/refunds/{refund.PayPalRefundId}", response);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}
