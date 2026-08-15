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

/// <summary>Route id + body for a refund.</summary>
public record RefundOrderCommand(int OrderId, RefundRequest Body);

/// <summary>
/// Shopper action. Refunds the captured payment for the caller's order, in full or in part. The
/// caller-supplied idempotency key makes a repeat a no-op. Returns the refund's id as a top-level field.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderCommand, IPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public RefundOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundRequest request, IPaymentService paymentService) =>
                await HandleAsync(new RefundOrderCommand(orderId, request ?? new RefundRequest()), paymentService))
            .Produces<RefundResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderCommand command, IPaymentService paymentService)
    {
        var buyerId = EndpointCaller.RequireBuyerId(_http);

        if (string.IsNullOrWhiteSpace(command.Body.IdempotencyKey))
        {
            throw new PaymentException("An idempotencyKey is required for a refund.");
        }

        var payment = await paymentService.RefundOrderAsync(
            command.OrderId, buyerId, command.Body.Amount, command.Body.IdempotencyKey);

        var refund = payment.FindRefundByIdempotencyKey(command.Body.IdempotencyKey)!;

        var response = new RefundResponse
        {
            RefundId = refund.RefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            Currency = payment.Currency,
            OrderStatus = PaymentMapping.DeriveOrderStatus(payment).ToString(),
            TotalRefunded = payment.TotalRefunded(),
            RefundableRemaining = payment.RefundableRemaining()
        };
        return Results.Ok(response);
    }
}
