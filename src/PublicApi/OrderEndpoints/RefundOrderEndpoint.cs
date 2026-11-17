using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a captured payment, in full or in part. The caller supplies an idempotency key.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public RefundOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, cancellationToken);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var refund = await _orderPaymentService.RefundOrderAsync(
            user.Identity!.Name!,
            request.OrderId,
            new RefundOrderCommand(request.Amount, request.IdempotencyKey),
            cancellationToken);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.RefundId,
            OrderId = request.OrderId,
            FinixRefundId = refund.FinixRefundId,
            Amount = refund.Amount,
            AmountMinor = refund.AmountMinor,
            Status = refund.Status,
            IdempotencyKey = refund.IdempotencyKey,
            FailureCode = refund.FailureCode,
            FailureMessage = refund.FailureMessage,
            CreatedAt = refund.CreatedAt
        };

        return Results.Ok(response);
    }
}
