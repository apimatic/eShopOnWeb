using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, ICheckoutService checkout, ClaimsPrincipal user, HttpContext http) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(buyerId))
                {
                    return Results.Unauthorized();
                }

                var idempotencyKey = request.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(idempotencyKey)
                    && http.Request.Headers.TryGetValue("Idempotency-Key", out var headerKey))
                {
                    idempotencyKey = headerKey.ToString();
                }

                var refund = await checkout.RefundAsync(buyerId, orderId, request.Amount, idempotencyKey ?? string.Empty);
                return Results.Ok(new RefundOrderResponse
                {
                    RefundId = refund.Id,
                    PayPalRefundId = refund.PayPalRefundId,
                    Status = refund.Status,
                    Amount = refund.Amount,
                    Currency = refund.Currency
                });
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, ICheckoutService checkout) =>
        throw new System.NotSupportedException("Use the route handler.");
}

public class RefundOrderRequest
{
    public decimal? Amount { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
