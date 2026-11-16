using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: refunds a captured payment in full or in part.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IFinixPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, RefundOrderRequest request, IFinixPaymentService paymentService) =>
                {
                    request.OrderId = orderId;
                    return await HandleAsync(request, paymentService);
                })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IFinixPaymentService paymentService)
    {
        var order = await paymentService.RefundOrderAsync(request);
        var idempotencyKey = (request.IdempotencyKey ?? string.Empty).Trim();
        var refund = order.Refunds.LastOrDefault(r => r.IdempotencyKey == idempotencyKey)
                     ?? order.Refunds.LastOrDefault();
        return Results.Ok(new
        {
            refundId = refund?.RefundId ?? 0,
            orderId = order.OrderId,
            amountCents = refund?.AmountCents ?? 0,
            status = refund?.Status,
            finixTransferId = refund?.FinixTransferId,
            order
        });
    }
}
