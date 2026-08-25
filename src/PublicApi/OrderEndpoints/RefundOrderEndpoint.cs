using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
}

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, HttpContext ctx) =>
            {
                return await HandleAsync(request, ctx, orderId);
            })
            .Produces<RefundOrderResponse>(201)
            .Produces(400)
            .Produces(404)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, HttpContext ctx)
        => HandleAsync(request, ctx, 0);

    private async Task<IResult> HandleAsync(RefundOrderRequest request, HttpContext ctx, int orderId)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest("idempotencyKey is required.");

        var sp = ctx.RequestServices;
        var paymentRepo = sp.GetRequiredService<IRepository<Payment>>();
        var paypalService = sp.GetRequiredService<IPayPalService>();
        var ct = ctx.RequestAborted;

        var paymentSpec = new PaymentByOrderIdSpec(orderId);
        var payment = await paymentRepo.FirstOrDefaultAsync(paymentSpec, ct);
        if (payment is null) return Results.NotFound("Payment record not found.");

        if (payment.Status != PaymentStatus.Captured &&
            payment.Status != PaymentStatus.PartiallyRefunded)
        {
            return Results.BadRequest($"Only captured or partially refunded orders can be refunded. Current status: {payment.Status}");
        }

        if (payment.PayPalCaptureId is null)
            return Results.BadRequest("No capture ID found.");

        // Validate refund amount doesn't exceed what's refundable
        if (request.Amount.HasValue)
        {
            var remaining = payment.RemainingRefundable();
            if (request.Amount.Value > remaining)
                return Results.BadRequest($"Refund amount {request.Amount.Value} exceeds remaining refundable amount {remaining}.");
            if (request.Amount.Value <= 0)
                return Results.BadRequest("Refund amount must be positive.");
        }

        // Check for duplicate idempotency key
        foreach (var existingRefund in payment.Refunds)
        {
            if (existingRefund.IdempotencyKey == request.IdempotencyKey)
            {
                return Results.Created($"/api/orders/{orderId}/refunds/{existingRefund.PayPalRefundId}",
                    new RefundOrderResponse
                    {
                        RefundId = existingRefund.PayPalRefundId,
                        Amount = existingRefund.Amount,
                        Currency = existingRefund.Currency,
                        PaymentStatus = payment.Status.ToString()
                    });
            }
        }

        RefundResult refundResult;
        try
        {
            refundResult = await paypalService.RefundAsync(
                captureId: payment.PayPalCaptureId,
                amount: request.Amount,
                currency: payment.Currency,
                idempotencyKey: request.IdempotencyKey,
                ct: ct);
        }
        catch (PayPalException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode ?? 422);
        }

        payment.RecordRefund(refundResult.RefundId, request.IdempotencyKey, refundResult.Amount, refundResult.Currency);
        await paymentRepo.UpdateAsync(payment, ct);

        return Results.Created($"/api/orders/{orderId}/refunds/{refundResult.RefundId}",
            new RefundOrderResponse
            {
                RefundId = refundResult.RefundId,
                Amount = refundResult.Amount,
                Currency = refundResult.Currency,
                PaymentStatus = payment.Status.ToString()
            });
    }
}
