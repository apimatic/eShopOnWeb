using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/refunds — refunds the captured payment for the caller's own order, fully or
/// partially. Carries a caller-supplied idempotency key. Returns the refund id as a top-level field.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ClaimsPrincipal>
{
    private readonly IOrderPaymentService _service;

    public RefundOrderEndpoint(IOrderPaymentService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user);
            })
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = CallerIdentity.GetBuyerId(user);

        var payment = await _service.RefundAsync(buyerId, request.OrderId, request.Amount, request.IdempotencyKey);
        var refund = payment.FindRefundByKey(request.IdempotencyKey)!;

        var response = new RefundResponse
        {
            RefundId = refund.RefundId,
            OrderId = request.OrderId,
            Amount = refund.Amount,
            Status = refund.Status,
            TotalRefunded = payment.TotalRefunded(),
            RefundableRemaining = payment.RefundableRemaining(),
        };

        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.RefundId}", response);
    }
}
