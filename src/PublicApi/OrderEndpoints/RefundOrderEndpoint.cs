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
/// Refunds a fulfilled order's capture, in full or in part. Idempotent by caller-supplied key:
/// repeating the same key never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
    {
        var response = new RefundOrderResponse(request.CorrelationId());

        var refund = await paymentService.RefundOrderAsync(request.OrderId, request.BuyerId, request.Amount, request.IdempotencyKey, CancellationToken.None);

        response.RefundId = refund.Id;
        response.OrderId = request.OrderId;
        response.PayPalRefundId = refund.PayPalRefundId;
        response.Status = refund.Status;
        response.Amount = refund.Amount;

        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}
