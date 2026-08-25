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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a fulfilled order's captured payment, in full or in part. A repeated request under the
/// same <see cref="RefundOrderRequest.IdempotencyKey"/> replays the original refund rather than
/// issuing a new one; two distinct keys against the same order remain independent partial refunds.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService paymentService,
                CancellationToken ct) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, paymentService, ct);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService paymentService, CancellationToken ct)
    {
        var response = new RefundOrderResponse(request.CorrelationId());

        var refund = await paymentService.RequestRefundAsync(request.BuyerId, request.OrderId, request.Amount,
            request.IdempotencyKey, ct);
        if (refund is null) return Results.NotFound();

        response.OrderId = request.OrderId;
        response.RefundId = refund.PayPalRefundId;
        response.Status = refund.Status;
        response.Amount = refund.Amount;
        return Results.Created($"api/my-orders/{request.OrderId}", response);
    }
}
