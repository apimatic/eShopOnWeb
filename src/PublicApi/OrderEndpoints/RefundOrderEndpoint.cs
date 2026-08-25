using System;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IRepository<PaymentRecord>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   RefundOrderRequest request,
                   IRepository<PaymentRecord> paymentRepo,
                   IPayPalService payPal,
                   IConfiguration config,
                   CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    return Results.BadRequest(new { error = "IdempotencyKey is required." });

                var paySpec = new PaymentRecordByOrderIdSpec(orderId);
                var payment = await paymentRepo.FirstOrDefaultAsync(paySpec, ct);
                if (payment == null)
                    return Results.NotFound(new { error = "Payment record not found." });

                if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
                    return Results.Conflict(new { error = $"Order cannot be refunded in state '{payment.Status}'." });

                if (string.IsNullOrEmpty(payment.CaptureId))
                    return Results.Conflict(new { error = "No capture on record to refund." });

                // Idempotency: same key already processed
                if (payment.HasRefundWithKey(request.IdempotencyKey))
                {
                    var existing = payment.GetRefundByKey(request.IdempotencyKey)!;
                    return Results.Ok(new RefundOrderResponse
                    {
                        RefundId = existing.RefundId ?? "",
                        Status = existing.RefundStatus ?? "",
                        Amount = existing.Amount,
                        Currency = existing.Currency
                    });
                }

                // Validate partial refund doesn't exceed captured
                if (request.Amount.HasValue)
                {
                    var capturedAmt = decimal.TryParse(payment.CapturedAmount, out var cap) ? cap : 0m;
                    var alreadyRefunded = payment.RefundedTotal();
                    if (request.Amount.Value <= 0)
                        return Results.BadRequest(new { error = "Refund amount must be positive." });
                    if (alreadyRefunded + request.Amount.Value > capturedAmt)
                        return Results.UnprocessableEntity(new { error = $"Refund amount exceeds refundable balance. Captured: {capturedAmt}, already refunded: {alreadyRefunded}." });
                }

                var currency = config["PayPal:Currency"] ?? "USD";

                PayPalRefundResult refundResult;
                try
                {
                    refundResult = await payPal.RefundCaptureAsync(
                        payment.CaptureId, request.Amount, currency, request.IdempotencyKey, ct);
                }
                catch (PayPalException ex) when (ex.StatusCode == 409)
                {
                    // PayPal idempotent 409 — treat as success with unknown refund ID
                    var refund2 = payment.AddRefund(null, "COMPLETED", request.Amount?.ToString("F2"), currency, request.IdempotencyKey);
                    await paymentRepo.UpdateAsync(payment, ct);
                    return Results.Ok(new RefundOrderResponse
                    {
                        RefundId = refund2.RefundId ?? "",
                        Status = refund2.RefundStatus ?? "",
                        Amount = refund2.Amount,
                        Currency = refund2.Currency
                    });
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode ?? 502, title: "Refund failed.");
                }

                var addedRefund = payment.AddRefund(refundResult.RefundId, refundResult.RefundStatus, refundResult.Amount, refundResult.Currency, request.IdempotencyKey);
                await paymentRepo.UpdateAsync(payment, ct);

                return Results.Ok(new RefundOrderResponse
                {
                    RefundId = addedRefund.RefundId ?? "",
                    Status = addedRefund.RefundStatus ?? "",
                    Amount = addedRefund.Amount,
                    Currency = addedRefund.Currency
                });
            })
            .Produces<RefundOrderResponse>()
            .Produces(400)
            .Produces(404)
            .Produces(409)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IRepository<PaymentRecord> service)
        => throw new NotImplementedException();
}
