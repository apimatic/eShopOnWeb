using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IRepository<Order>>
{
    private readonly IPayPalService _paypal;

    public RefundOrderEndpoint(IPayPalService paypal) => _paypal = paypal;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IRepository<Order> orderRepo) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, orderRepo);
            })
            .Produces<RefundOrderResponse>(201)
            .Produces(400)
            .Produces(404)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IRepository<Order> orderRepo)
    {
        var spec = new OrderWithRefundsSpec(request.OrderId);
        var order = await orderRepo.FirstOrDefaultAsync(spec);
        if (order == null)
            return Results.NotFound();

        if (order.Status != OrderStatus.Fulfilled)
            return Results.BadRequest(new { error = "Only fulfilled orders can be refunded." });

        if (string.IsNullOrEmpty(order.PayPalCaptureId))
            return Results.BadRequest(new { error = "Order has no capture record." });

        if (string.IsNullOrEmpty(request.IdempotencyKey))
            return Results.BadRequest(new { error = "IdempotencyKey is required." });

        // Idempotency: check if this key was already used
        var existingRefund = order.Refunds.FirstOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);
        if (existingRefund != null)
        {
            return Results.Ok(new RefundOrderResponse(request.CorrelationId())
            {
                RefundId = existingRefund.RefundId,
                Amount = existingRefund.Amount,
                Status = existingRefund.Status
            });
        }

        // Guard: cannot refund more than what was captured minus what was already refunded
        if (request.Amount.HasValue)
        {
            if (request.Amount.Value <= 0)
                return Results.BadRequest(new { error = "Refund amount must be positive." });

            var remaining = order.CapturedAmount - order.TotalRefunded;
            if (request.Amount.Value > remaining)
                return Results.BadRequest(new { error = $"Refund amount {request.Amount.Value:0.00} exceeds remaining refundable amount {remaining:0.00}." });
        }
        else
        {
            // Full refund: check remaining
            if (order.TotalRefunded > 0 && order.TotalRefunded >= order.CapturedAmount)
                return Results.BadRequest(new { error = "Order has already been fully refunded." });
        }

        try
        {
            var result = await _paypal.RefundAsync(
                order.PayPalCaptureId,
                request.Amount,
                request.IdempotencyKey,
                CancellationToken.None);

            var refundAmount = request.Amount ?? (order.CapturedAmount - order.TotalRefunded);
            if (result.Amount > 0) refundAmount = result.Amount;

            var refundRecord = new OrderRefund(order.Id, result.RefundId, request.IdempotencyKey, refundAmount, result.Status);
            order.AddRefund(refundRecord);
            await orderRepo.UpdateAsync(order);

            return Results.Created($"api/orders/{order.Id}/refunds/{result.RefundId}",
                new RefundOrderResponse(request.CorrelationId())
                {
                    RefundId = result.RefundId,
                    Amount = refundAmount,
                    Status = result.Status
                });
        }
        catch (PayPalException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = "";
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public string? RefundId { get; set; }
    public decimal Amount { get; set; }
    public string? Status { get; set; }
}
