using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a captured payment for the shopper's own order, in full or in part. Idempotent by the
/// caller-supplied key.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext http, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                request.BuyerId = http.User.Identity?.Name;
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotencyKey is required for a refund." });
        }

        var payment = await service.RefundAsync(request.OrderId, request.BuyerId, request.Amount, request.IdempotencyKey);

        var refund = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund?.PayPalRefundId ?? string.Empty,
            RefundAmount = refund?.Amount ?? 0m,
            RefundStatus = refund?.Status ?? string.Empty,
            Payment = PaymentStateDto.From(payment)
        };

        return Results.Ok(response);
    }
}
