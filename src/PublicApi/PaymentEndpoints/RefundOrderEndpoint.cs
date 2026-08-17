using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;
using static Microsoft.eShopWeb.PublicApi.PaymentEndpoints.PaymentApiHelpers;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/refunds — returns captured money in full or in part. Carries a
/// caller-supplied idempotency key: repeating under the same key never refunds twice, while two distinct
/// partial refunds remain legitimate. A partly-refunded order can never be refunded beyond what was
/// captured. Shopper-scoped: acts only on the caller's own order.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest>
{
    private readonly IPaymentService _paymentService;

    public RefundOrderEndpoint(IPaymentService paymentService) => _paymentService = paymentService;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetUserName() ?? string.Empty;
                return await HandleAsync(request);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request)
    {
        var input = new RefundInput(request.Amount, request.IdempotencyKey ?? string.Empty, request.Reason);
        var result = await _paymentService.RefundAsync(request.OrderId, request.BuyerId, input);
        return ToHttp(result, refund => Results.Created($"api/orders/{request.OrderId}/refunds/{refund.RefundId}", new RefundOrderResponse
        {
            RefundId = refund.RefundId,
            Amount = refund.Amount,
            Status = refund.Status,
            Reason = refund.Reason
        }));
    }
}
