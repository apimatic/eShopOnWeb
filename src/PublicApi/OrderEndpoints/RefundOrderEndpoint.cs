using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest
{
    public int OrderId { get; set; }
    public decimal? Amount { get; set; }          // null = full refund of remaining balance
    public string IdempotencyKey { get; set; } = "";
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = "";
    public int OrderId { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
    public string PaymentStatus { get; set; } = "";
    public string Currency { get; set; } = "";
}

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IRepository<Order>>
{
    private readonly IRepository<OrderPayment> _paymentRepo;
    private readonly IPayPalClient _paypal;

    public RefundOrderEndpoint(IRepository<OrderPayment> paymentRepo, IPayPalClient paypal)
    {
        _paymentRepo = paymentRepo;
        _paypal = paypal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IRepository<Order> orderRepo,
                   HttpContext ctx, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                var buyerId = ctx.User.FindFirst(ClaimTypes.Name)?.Value;
                var isAdmin = ctx.User.IsInRole("Administrators");
                return await HandleAsync(request, orderRepo, buyerId, isAdmin, ct);
            })
            .Produces<RefundOrderResponse>(201)
            .Produces(400)
            .Produces(404)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IRepository<Order> repository)
        => HandleAsync(request, repository, null, true);

    private async Task<IResult> HandleAsync(RefundOrderRequest request, IRepository<Order> orderRepo,
        string? buyerId, bool isAdmin, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest(new { error = "idempotencyKey is required." });

        var order = await orderRepo.GetByIdAsync(request.OrderId, ct);
        if (order == null)
            return Results.NotFound();

        // Shoppers can only refund their own orders; admins can refund any
        if (!isAdmin && order.BuyerId != buyerId)
            return Results.NotFound();

        var spec = new OrderPaymentByOrderIdSpec(request.OrderId);
        var payment = await _paymentRepo.FirstOrDefaultAsync(spec, ct);
        if (payment == null)
            return Results.Problem("Payment record not found.");

        if (payment.Status is not (OrderPaymentStatus.Captured
            or OrderPaymentStatus.PartiallyRefunded))
            return Results.BadRequest(new
            {
                error = $"Refunds can only be issued against captured payments. Current status: '{payment.Status}'."
            });

        // Idempotency: check if this key was already used
        foreach (var existing in payment.Refunds)
        {
            if (existing.IdempotencyKey == request.IdempotencyKey)
                return Results.Ok(new RefundOrderResponse
                {
                    RefundId = existing.PayPalRefundId,
                    OrderId = order.Id,
                    RefundedAmount = existing.Amount,
                    TotalRefunded = payment.TotalRefunded,
                    RemainingRefundable = payment.RefundableAmount,
                    PaymentStatus = payment.Status.ToString(),
                    Currency = payment.Currency
                });
        }

        var refundAmount = request.Amount;
        if (refundAmount.HasValue && refundAmount.Value <= 0)
            return Results.BadRequest(new { error = "Refund amount must be positive." });

        if (refundAmount.HasValue && refundAmount.Value > payment.RefundableAmount)
            return Results.BadRequest(new
            {
                error = $"Refund amount {refundAmount.Value:F2} exceeds refundable balance {payment.RefundableAmount:F2}.",
                refundable = payment.RefundableAmount
            });

        if (!refundAmount.HasValue && payment.RefundableAmount <= 0)
            return Results.BadRequest(new { error = "No refundable balance remaining." });

        try
        {
            var result = await _paypal.RefundCaptureAsync(
                payment.CaptureId!, refundAmount, payment.Currency, request.IdempotencyKey, ct);

            var actualAmount = refundAmount ?? result.Amount;
            var refund = new PaymentRefund(payment.Id, request.IdempotencyKey, result.RefundId, actualAmount);
            payment.AddRefund(refund);
            await _paymentRepo.UpdateAsync(payment, ct);

            return Results.Created($"/api/orders/{order.Id}/refunds/{result.RefundId}",
                new RefundOrderResponse
                {
                    RefundId = result.RefundId,
                    OrderId = order.Id,
                    RefundedAmount = actualAmount,
                    TotalRefunded = payment.TotalRefunded,
                    RemainingRefundable = payment.RefundableAmount,
                    PaymentStatus = payment.Status.ToString(),
                    Currency = payment.Currency
                });
        }
        catch (PayPalException ex)
        {
            return Results.BadRequest(new { error = ex.Message, code = ex.PayPalErrorName });
        }
    }
}
