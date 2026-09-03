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
/// Refunds a captured payment, in full or in part. The refund carries a caller-supplied idempotency
/// key (request body or the <c>Idempotency-Key</c> header) so a repeat under the same key does not
/// refund twice, while distinct partial refunds remain legitimate.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, HttpContext, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext http, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, http, service);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, HttpContext http, IOrderPaymentService service) =>
        PaymentApiHelpers.RunAsync(http, async buyerId =>
        {
            var idempotencyKey = !string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? request.IdempotencyKey!
                : http.Request.Headers["Idempotency-Key"].ToString();

            if (string.IsNullOrWhiteSpace(idempotencyKey))
                return Results.BadRequest("An idempotency key is required (request body 'idempotencyKey' or the 'Idempotency-Key' header).");

            var outcome = await service.RefundAsync(buyerId, request.OrderId, request.Amount, idempotencyKey, http.RequestAborted);

            var response = new RefundOrderResponse(request.CorrelationId())
            {
                RefundId = outcome.RefundId,
                OrderId = request.OrderId,
                Status = outcome.Status,
                Amount = outcome.Amount,
                TotalRefunded = outcome.TotalRefunded,
                PaymentStatus = outcome.PaymentStatus.ToString(),
                Currency = outcome.CurrencyCode
            };
            return Results.Created($"api/orders/{request.OrderId}/refunds/{outcome.RefundId}", response);
        });
}
