using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: refunds a captured payment, fully or partially.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, FinixPaymentService payments, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, request, payments, ct);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        int orderId,
        RefundOrderRequest request,
        FinixPaymentService payments,
        CancellationToken ct = default)
    {
        var response = new RefundOrderResponse(request.CorrelationId());
        var refund = await payments.RefundAsync(orderId, request.AmountCents, request.IdempotencyKey, ct);
        var order = await payments.GetOrderAsync(orderId, ct);

        response.RefundId = refund.FinixRefundTransferId ?? refund.ClaimKey;
        response.OrderId = refund.OrderId;
        response.AmountCents = refund.AmountCents;
        response.State = refund.State;
        response.CreatedAt = refund.CreatedAt;

        if (order is not null)
        {
            response.TotalRefundedCents = order.RefundedAmountCents;
            response.RemainingRefundableCents = order.RemainingRefundableCents;
        }

        return Results.Ok(response);
    }
}
