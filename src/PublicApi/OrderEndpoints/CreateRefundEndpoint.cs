using System.Linq;
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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateRefundEndpoint : IEndpoint<IResult, CreateRefundRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, CreateRefundRequest request, IRepository<Order> orderRepo,
                   IPayPalService paypal, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, orderRepo, paypal, ct);
            })
            .Produces<CreateRefundResponse>(201)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateRefundRequest request, IRepository<Order> orderRepo)
        => Results.StatusCode(500);

    private async Task<IResult> HandleAsync(CreateRefundRequest request, IRepository<Order> orderRepo,
        IPayPalService paypal, CancellationToken ct)
    {
        var spec = new OrderWithPaymentSpec(request.OrderId);
        var order = await orderRepo.FirstOrDefaultAsync(spec, ct);

        if (order is null) return Results.NotFound();
        if (order.Status != OrderStatus.Fulfilled &&
            order.Status != OrderStatus.PartiallyRefunded)
            return Results.BadRequest($"Order in status '{order.Status}' cannot be refunded.");
        if (order.Payment?.CaptureId is null)
            return Results.BadRequest("Order has no capture record.");

        var payment = order.Payment;

        // Idempotency: return existing refund if same idempotency key was used
        var existingRefund = payment.GetRefunds()
            .FirstOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);
        if (existingRefund is not null)
        {
            return Results.Ok(new CreateRefundResponse
            {
                RefundId = existingRefund.RefundId,
                RefundedAmount = existingRefund.Amount,
                Status = existingRefund.Status
            });
        }

        // Prevent over-refund
        var capturedAmount = payment.CapturedAmount ?? 0m;
        var alreadyRefunded = payment.TotalRefunded();
        var maxRefundable = capturedAmount - alreadyRefunded;

        if (request.Amount.HasValue && request.Amount.Value > maxRefundable)
            return Results.BadRequest(
                $"Refund of {request.Amount.Value} exceeds refundable amount of {maxRefundable:F2}.");

        if (!request.Amount.HasValue && maxRefundable <= 0)
            return Results.BadRequest("The full amount has already been refunded.");

        var currency = request.Currency ?? paypal.Currency;

        try
        {
            var result = await paypal.RefundCaptureAsync(
                captureId: payment.CaptureId,
                amount: request.Amount,
                currency: currency,
                idempotencyKey: request.IdempotencyKey,
                ct: ct);

            order.AddRefund(
                idempotencyKey: request.IdempotencyKey,
                refundId: result.RefundId,
                amount: result.RefundedAmount,
                currency: currency,
                status: result.RefundStatus);

            await orderRepo.UpdateAsync(order, ct);

            return Results.Created(
                $"api/orders/{order.Id}/refunds/{result.RefundId}",
                new CreateRefundResponse
                {
                    RefundId = result.RefundId,
                    RefundedAmount = result.RefundedAmount,
                    Status = result.RefundStatus
                });
        }
        catch (PayPalException ex) when (ex.StatusCode == 409)
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (PayPalException ex)
        {
            return Results.Problem(
                title: "Refund failed",
                detail: ex.Message,
                statusCode: ex.StatusCode);
        }
    }
}
