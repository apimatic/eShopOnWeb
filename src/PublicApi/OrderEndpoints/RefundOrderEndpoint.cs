using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint
{
    private readonly IRepository<Order> _orderRepo;
    private readonly IRepository<OrderRefund> _refundRepo;
    private readonly IPayPalGateway _paypal;

    public RefundOrderEndpoint(IRepository<Order> orderRepo, IRepository<OrderRefund> refundRepo, IPayPalGateway paypal)
    {
        _orderRepo = orderRepo;
        _refundRepo = refundRepo;
        _paypal = paypal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, HttpContext ctx) =>
            {
                return await HandleAsync(orderId, request, ctx.RequestAborted);
            })
            .Produces<RefundOrderResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .WithTags("OrderEndpoints");
    }

    private async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, System.Threading.CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest("IdempotencyKey is required.");

        var order = await _orderRepo.GetByIdAsync(orderId, ct);
        if (order == null) return Results.NotFound();

        if (order.PaymentStatus != PaymentStatus.Fulfilled &&
            order.PaymentStatus != PaymentStatus.PartiallyRefunded)
            return Results.Problem($"Order cannot be refunded in its current state: {order.PaymentStatus}", statusCode: 409);

        // Idempotency: return existing refund if key already used
        var existingSpec = new OrderRefundByKeySpec(orderId, request.IdempotencyKey);
        var existing = await _refundRepo.FirstOrDefaultAsync(existingSpec, ct);
        if (existing != null)
            return Results.Ok(new RefundOrderResponse
            {
                RefundId = existing.PayPalRefundId,
                Amount = existing.Amount
            });

        // Partial refund guard
        var captured = order.CapturedAmount ?? 0m;
        var alreadyRefunded = order.TotalRefundedAmount;
        var available = captured - alreadyRefunded;

        if (available <= 0m)
            return Results.Problem("Order has already been fully refunded.", statusCode: 409);

        decimal? refundAmount = request.Amount;
        if (refundAmount.HasValue && refundAmount.Value > available)
            return Results.Problem($"Refund amount {refundAmount:F2} exceeds available amount {available:F2}.", statusCode: 400);

        try
        {
            var result = await _paypal.RefundAsync(
                order.PayPalCaptureId!,
                refundAmount,
                order.Currency,
                request.IdempotencyKey,
                ct);

            var refund = new OrderRefund(orderId, result.RefundId, request.IdempotencyKey, result.Amount, order.Currency ?? "USD");
            await _refundRepo.AddAsync(refund, ct);

            order.AddRefundAmount(result.Amount);
            await _orderRepo.UpdateAsync(order, ct);

            return Results.Ok(new RefundOrderResponse
            {
                RefundId = result.RefundId,
                Amount = result.Amount
            });
        }
        catch (PayPalException ex)
        {
            return Results.Problem($"Refund failed: {ex.Message}", statusCode: 502);
        }
    }
}

public class RefundOrderRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
