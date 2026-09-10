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

public record RefundCommand(string BuyerId, int OrderId, RefundRequestDto Body, CancellationToken Ct);

/// <summary>
/// POST /api/orders/{orderId}/refunds — return after fulfilment: refund the captured payment, in full or
/// in part, under a caller-supplied idempotency key. Returns the refund id as a top-level field.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundCommand, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundRequestDto request, HttpContext http, IPaymentApplicationService service) =>
            {
                var buyerId = CallerContext.GetBuyerId(http);
                if (buyerId is null) return Results.Unauthorized();
                return await HandleAsync(new RefundCommand(buyerId, orderId, request, http.RequestAborted), service);
            })
            .Produces(StatusCodes.Status201Created)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(RefundCommand command, IPaymentApplicationService service)
    {
        if (command.Body is null || string.IsNullOrWhiteSpace(command.Body.IdempotencyKey))
            throw new PaymentValidationException("A refund requires an idempotencyKey.");

        var outcome = await service.RefundAsync(command.BuyerId, command.OrderId,
            command.Body.Amount, command.Body.IdempotencyKey, command.Ct);

        return Results.Created($"api/orders/{command.OrderId}/refunds/{outcome.Refund.RefundId}", new
        {
            refundId = outcome.Refund.RefundId,
            amount = outcome.Refund.Amount,
            status = outcome.Refund.Status,
            payment = PaymentMapper.ToDto(outcome.Payment)
        });
    }
}
