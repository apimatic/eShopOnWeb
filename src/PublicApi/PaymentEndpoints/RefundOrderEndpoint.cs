using System;
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
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Refunds the captured payment of the caller's own order, in full or in part. The
/// caller-supplied idempotency key makes a repeated request return the same refund, while two
/// distinct partial refunds remain separate.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Refund a captured order (full or partial)", Tags = new[] { "Orders" })]
        async (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
                await HandleAsync(orderId, request, user, service, ct))
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct)
    {
        var buyerId = PaymentProblem.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        if (request is null || string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest(new { message = "An 'idempotencyKey' is required for refunds." });

        try
        {
            var refund = await service.RefundAsync(buyerId, orderId, request.Amount, request.IdempotencyKey, ct);
            var response = new RefundOrderResponse(
                refund.PayPalRefundId ?? string.Empty,
                refund.Status,
                refund.Amount,
                refund.CurrencyCode);
            return Results.Created($"api/orders/{orderId}/refunds/{refund.PayPalRefundId}", response);
        }
        catch (Exception ex)
        {
            return PaymentProblem.ToResult(ex);
        }
    }
}
