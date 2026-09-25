using System.Security.Claims;
using System.Threading;
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

public record RefundOrderRequest(string BuyerId, int OrderId, RefundBody Body, CancellationToken Ct);

/// <summary>POST /api/orders/{orderId}/refunds — refund the captured payment, full or partial, under an idempotency key.</summary>
public class OrdersRefundEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId, RefundBody body, ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
                await HandleAsync(new RefundOrderRequest(user.BuyerId(), orderId, body, ct), service))
            .Produces<RefundApiResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        if (request.Body is null || string.IsNullOrWhiteSpace(request.Body.IdempotencyKey))
        {
            throw new PaymentOperationException("A refund requires an idempotencyKey.", PaymentErrorKind.Validation);
        }

        var (payment, refund) = await service.RefundAsync(
            request.BuyerId, request.OrderId, request.Body.Amount, request.Body.IdempotencyKey, request.Body.Note, request.Ct);

        var response = new RefundApiResponse(refund.RefundId, refund.Status, refund.Amount, refund.CurrencyCode, payment);
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.RefundId}", response);
    }
}
