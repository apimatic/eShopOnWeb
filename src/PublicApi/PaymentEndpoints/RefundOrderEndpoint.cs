using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record RefundOrderCommand(int OrderId, RefundApiRequest Body);

/// <summary>
/// Returns money after fulfilment: refunds the captured payment in full or in part. Carries a
/// caller-supplied idempotency key so repeating a request under the same key never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderCommand, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundApiRequest body, IPaymentService paymentService) =>
                await HandleAsync(new RefundOrderCommand(orderId, body ?? new RefundApiRequest()), paymentService))
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(RefundOrderCommand request, IPaymentService paymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.BuyerId();

        var idempotencyKey = request.Body.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            // Fall back to a standard idempotency header if the body did not carry one.
            idempotencyKey = _httpContextAccessor.HttpContext?.Request.Headers["Idempotency-Key"].ToString();
        }
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { message = "A caller-supplied idempotencyKey is required for refunds." });
        }

        var refund = await paymentService.RefundAsync(buyerId!, request.OrderId, idempotencyKey!, request.Body.Amount);

        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", new RefundResponse
        {
            RefundId = refund.Id,
            Status = refund.Status.ToString(),
            Amount = refund.Amount,
            PayPalRefundId = refund.PayPalRefundId
        });
    }
}
