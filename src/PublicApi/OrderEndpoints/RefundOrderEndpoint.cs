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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Shopper: refund a captured payment, full or partial.</summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                RefundOrderRequest request,
                IRepository<Order> orderRepo,
                IRepository<OrderPayment> paymentRepo,
                IPayPalPaymentService payPalService,
                PayPalSettings settings,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var buyerId = OrderHelpers.GetBuyerId(user);
                var order = await orderRepo.GetBySpecAsync(new OrderByIdWithPaymentSpec(orderId), ct);

                if (order == null || order.BuyerId != buyerId)
                    return Results.NotFound(new { error = "Order not found." });

                var payment = order.Payment;
                if (payment == null ||
                    (payment.PaymentStatus != PaymentStatuses.Captured &&
                     payment.PaymentStatus != PaymentStatuses.RefundedPartial))
                    return Results.Conflict(new { error = "Order has not been captured or is already fully refunded." });

                var maxRefundable = (payment.CapturedAmount ?? 0m) - (payment.TotalRefundedAmount ?? 0m);
                if (request.Amount.HasValue && request.Amount.Value > maxRefundable)
                    return Results.UnprocessableEntity(new { error = $"Refund amount exceeds the refundable balance of {maxRefundable:F2}." });

                var currency = settings.Currency;
                RefundResult refundResult;
                try
                {
                    refundResult = await payPalService.RefundAsync(
                        payment.CaptureId!,
                        request.IdempotencyKey,
                        request.Amount,
                        currency,
                        ct);
                }
                catch (PayPalException ex) when (ex.IsClientError)
                {
                    return Results.UnprocessableEntity(new { error = ex.Message });
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(ex.Message, statusCode: 502);
                }

                payment.AddRefund(refundResult.RefundId, refundResult.RefundedAmount);
                await paymentRepo.UpdateAsync(payment, ct);

                return Results.Ok(new RefundOrderResponse(
                    refundResult.RefundId,
                    orderId,
                    refundResult.RefundedAmount,
                    payment.TotalRefundedAmount ?? 0m,
                    payment.PaymentStatus));
            })
            .Produces<RefundOrderResponse>()
            .Produces(404)
            .Produces(409)
            .Produces(422)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IRepository<Order> repo)
        => throw new System.NotImplementedException();
}

public record RefundOrderRequest(decimal? Amount, string IdempotencyKey);
public record RefundOrderResponse(string RefundId, int OrderId, decimal RefundedAmount, decimal TotalRefunded, string PaymentStatus);
