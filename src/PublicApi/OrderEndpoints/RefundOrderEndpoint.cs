using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Enums;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPayPalService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request,
                   IRepository<Order> orderRepo,
                   IRepository<OrderPayment> paymentRepo,
                   IPayPalService paypal) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    return Results.BadRequest("'idempotencyKey' is required.");

                var order = await orderRepo.GetByIdAsync(orderId);
                if (order == null) return Results.NotFound($"Order {orderId} not found.");

                var payment = await paymentRepo.FirstOrDefaultAsync(
                    new OrderPaymentByOrderIdSpec(orderId));

                if (payment == null)
                    return Results.Problem("No payment record for this order.", statusCode: 404);

                // Idempotency: same key already processed
                var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);
                if (existing != null)
                    return Results.Ok(new RefundOrderResponse
                    {
                        RefundId = existing.Id.ToString(),
                        PayPalRefundId = existing.PayPalRefundId,
                        Amount = existing.Amount,
                        Status = "Completed"
                    });

                if (payment.Status != PaymentStatus.Captured &&
                    payment.Status != PaymentStatus.PartiallyRefunded)
                    return Results.Problem(
                        $"Order is in state '{payment.Status}' and cannot be refunded.",
                        statusCode: 409);

                if (request.Amount.HasValue && request.Amount.Value > payment.RemainingRefundable)
                    return Results.Problem(
                        $"Refund of {request.Amount} exceeds remaining refundable amount " +
                        $"of {payment.RemainingRefundable}.",
                        statusCode: 422);

                var refundResult = await paypal.RefundAsync(
                    payment.PayPalCaptureId!,
                    request.Amount,
                    payment.Currency,
                    request.IdempotencyKey,
                    request.Note);

                var refund = payment.AddRefund(
                    refundResult.RefundId,
                    refundResult.Amount,
                    request.IdempotencyKey);

                await paymentRepo.UpdateAsync(payment);

                return Results.Created($"api/orders/{orderId}/refunds/{refund.Id}", new RefundOrderResponse
                {
                    RefundId = refund.Id.ToString(),
                    PayPalRefundId = refundResult.RefundId,
                    Amount = refundResult.Amount,
                    Status = "Completed"
                });
            })
            .Produces<RefundOrderResponse>(201)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IPayPalService dependency)
        => throw new NotImplementedException();
}

public class RefundOrderRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string? Note { get; set; }
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = "";
    public string PayPalRefundId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Status { get; set; } = "";
}
