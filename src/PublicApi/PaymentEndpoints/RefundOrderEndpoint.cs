using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Refunds a fulfilled order, in full or in part. Carries a caller-supplied idempotency key so a
/// repeat never refunds twice. POST /api/orders/{orderId}/refunds
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, int, RefundOrderRequest, ClaimsPrincipal>
{
    private readonly IOrderPaymentService _service;

    public RefundOrderEndpoint(IOrderPaymentService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user) => await HandleAsync(orderId, request, user))
            .Produces<RefundOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new PaymentException("A refund requires an idempotencyKey.");

        var refundId = await _service.RefundAsync(buyerId, orderId, request.Amount, request.IdempotencyKey);

        return Results.Ok(new RefundOrderResponse { RefundId = refundId, OrderId = orderId });
    }
}
