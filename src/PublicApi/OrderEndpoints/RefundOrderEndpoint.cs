using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Body for a refund: an optional partial amount (omit for full remaining) and an idempotency key.</summary>
public class RefundOrderRequest
{
    public decimal? Amount { get; set; }
    public string? IdempotencyKey { get; set; }
}

public record RefundOrderResponse(string RefundId, decimal Amount, string Currency, string Status);

/// <summary>
/// Refund a captured payment for the caller's order, in full or in part. The idempotency key makes a
/// repeat a no-op; a partly-refunded order can never be refunded beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Refund a captured payment (full or partial)", Tags = new[] { "OrderPaymentEndpoints" })]
            async (int orderId, RefundOrderRequest request, IPaymentService paymentService, HttpContext http, CancellationToken ct) =>
            {
                var buyerId = http.User.GetBuyerId();
                var refund = await paymentService.RefundAsync(orderId, buyerId, request.Amount, request.IdempotencyKey ?? string.Empty, ct);
                var response = new RefundOrderResponse(refund.PayPalRefundId, refund.Amount, refund.Currency, refund.Status);
                return Results.Ok(response);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }
}
