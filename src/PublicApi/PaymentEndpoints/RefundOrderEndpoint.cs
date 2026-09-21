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
/// POST /api/orders/{orderId}/refunds — refund a captured payment, in full or in part. The
/// caller-supplied idempotency key makes a repeated request return the same refund. Shopper-scoped
/// to the caller's own order. Returns the refund id.
/// </summary>
public class RefundOrderEndpoint : PaymentEndpointBase, IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService service) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest(new { message = "An idempotencyKey is required for a refund." });

        var result = await service.RefundAsync(BuyerId, request.OrderId, request.Amount, request.IdempotencyKey, RequestAborted);
        return Results.Created($"api/orders/{request.OrderId}/refunds/{result.RefundId}",
            new RefundOrderResponse { RefundId = result.RefundId, Order = result.Order });
    }
}
