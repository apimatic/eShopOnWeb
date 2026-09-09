using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public record RefundOrderRequest(decimal? Amount, string? IdempotencyKey, string? NoteToPayer);

public class RefundOrderContext
{
    public int OrderId { get; init; }
    public string BuyerId { get; init; } = string.Empty;
    public RefundOrderRequest Request { get; init; } = new(null, null, null);
    public CancellationToken Ct { get; init; }
}

/// <summary>
/// Refunds a captured payment, in full or in part. The caller-supplied idempotency key makes a repeat
/// harmless (returns the original refund); distinct keys allow distinct partial refunds. Never refunds
/// beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderContext>
{
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IPayPalPaymentGateway _gateway;

    public RefundOrderEndpoint(IRepository<OrderPayment> paymentRepository, IPayPalPaymentGateway gateway)
    {
        _paymentRepository = paymentRepository;
        _gateway = gateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, CancellationToken ct) =>
                await HandleAsync(new RefundOrderContext
                {
                    OrderId = orderId,
                    BuyerId = user.GetBuyerId(),
                    Request = request,
                    Ct = ct
                }))
            .Produces<RefundOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderContext context)
    {
        var request = context.Request;
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest(new { message = "An idempotencyKey is required for refunds." });

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(context.OrderId));
        if (payment is null || payment.BuyerId != context.BuyerId)
            return Results.NotFound(new { message = $"Order {context.OrderId} was not found." });

        if (!payment.IsFulfilled || payment.CaptureId is null)
            return Results.Conflict(new { message = $"Order {context.OrderId} has no captured payment to refund." });

        // Idempotent: a repeat under the same key returns the original refund rather than refunding twice.
        var existing = payment.FindRefundByIdempotencyKey(request.IdempotencyKey!);
        if (existing is not null)
            return Results.Ok(new RefundOrderResponse
            {
                RefundId = existing.RefundId,
                Status = existing.Status,
                Amount = existing.Amount,
                Order = OrderPaymentDto.From(payment)
            });

        if (request.Amount is <= 0)
            return Results.BadRequest(new { message = "Refund amount must be greater than zero." });

        var amount = request.Amount ?? payment.RefundableRemaining;
        if (amount <= 0)
            return Results.Conflict(new { message = "There is nothing left to refund on this order." });
        if (amount > payment.RefundableRemaining)
            return Results.Conflict(new
            {
                message = $"Refund of {amount} exceeds the refundable remaining ({payment.RefundableRemaining})."
            });

        try
        {
            var result = await _gateway.RefundAsync(
                new RefundCommand(payment.CaptureId, amount, request.IdempotencyKey!, request.NoteToPayer),
                context.Ct);

            var refund = payment.RecordRefund(result.RefundId, result.Amount, request.IdempotencyKey!, result.Status);
            await _paymentRepository.UpdateAsync(payment);

            return Results.Ok(new RefundOrderResponse
            {
                RefundId = refund.RefundId,
                Status = refund.Status,
                Amount = refund.Amount,
                Order = OrderPaymentDto.From(payment)
            });
        }
        catch (PaymentGatewayException ex)
        {
            return PaymentResults.FromGatewayException(ex);
        }
    }
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public OrderPaymentDto Order { get; set; } = new();
}
