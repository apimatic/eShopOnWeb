using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/refunds — refund a captured payment, in full or in part. Carries a
/// caller-supplied idempotency key: repeating a request under the same key never refunds twice, while
/// two distinct partial refunds of the same capture remain legitimate. A partly-refunded order can
/// never be refunded beyond what was captured. Returns the refund id as a top-level field.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                RefundOrderRequest request,
                ClaimsPrincipal user,
                IRepository<Order> orderRepository,
                IPaymentProcessor processor,
                CancellationToken ct) =>
            {
                var buyerId = PaymentMapping.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    return Results.BadRequest(new { message = "An idempotencyKey is required for refunds." });
                }

                var order = await orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), ct);
                if (order is null || order.BuyerId != buyerId)
                {
                    return Results.NotFound(new { message = $"Order {orderId} was not found." });
                }

                if (order.Payment?.CaptureId is not { } captureId ||
                    order.PaymentStatus is not (OrderPaymentStatus.Paid or OrderPaymentStatus.PartiallyRefunded))
                {
                    return Results.Conflict(new { message = $"Order {orderId} has no captured payment to refund (current state: {order.PaymentStatus})." });
                }

                // Idempotent: a repeat under the same key returns the original refund without refunding again.
                var existing = order.Payment.FindRefundByKey(request.IdempotencyKey);
                if (existing is not null)
                {
                    return Results.Ok(new RefundOrderResponse(request.CorrelationId())
                    {
                        RefundId = existing.RefundId,
                        Amount = existing.Amount,
                        Status = existing.Status,
                        PaymentStatus = order.PaymentStatus.ToString(),
                        RemainingRefundable = order.Payment.RemainingRefundable()
                    });
                }

                var remaining = order.Payment.RemainingRefundable();
                if (remaining <= 0m)
                {
                    return Results.Conflict(new { message = "The captured payment has already been fully refunded." });
                }

                if (request.Amount is { } requested)
                {
                    if (requested <= 0m)
                    {
                        return Results.BadRequest(new { message = "Refund amount must be greater than zero." });
                    }
                    if (requested > remaining)
                    {
                        return Results.UnprocessableEntity(new { message = $"Refund amount {requested} exceeds the remaining refundable amount {remaining}." });
                    }
                }

                var result = await processor.RefundAsync(captureId, request.Amount, request.IdempotencyKey, ct);

                var recordedAmount = result.Amount > 0m ? result.Amount : (request.Amount ?? remaining);
                order.RecordRefund(result.RefundId, recordedAmount, result.Status ?? "UNKNOWN", request.IdempotencyKey, DateTimeOffset.UtcNow);
                await orderRepository.UpdateAsync(order, ct);

                var response = new RefundOrderResponse(request.CorrelationId())
                {
                    RefundId = result.RefundId,
                    Amount = recordedAmount,
                    Status = result.Status,
                    PaymentStatus = order.PaymentStatus.ToString(),
                    RemainingRefundable = order.Payment.RemainingRefundable()
                };

                return Results.Created($"api/orders/{order.Id}/refunds/{result.RefundId}", response);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }
}
