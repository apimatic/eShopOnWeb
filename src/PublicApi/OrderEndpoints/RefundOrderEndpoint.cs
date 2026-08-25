using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IRepository<Order> orderRepo,
                   IRepository<PaymentRecord> paymentRepo,
                   IPayPalService payPalService,
                   PayPalSettings settings) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, orderRepo, paymentRepo, payPalService, settings);
            })
            .Produces<RefundOrderResponse>(200)
            .Produces(400)
            .Produces(404)
            .Produces(409)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IRepository<Order> repo)
        => throw new System.NotSupportedException();

    private static async Task<IResult> HandleAsync(
        RefundOrderRequest request,
        IRepository<Order> orderRepo,
        IRepository<PaymentRecord> paymentRepo,
        IPayPalService payPalService,
        PayPalSettings settings)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest(new { error = "IdempotencyKey is required." });

        var order = await orderRepo.GetByIdAsync(request.OrderId);
        if (order == null)
            return Results.NotFound(new { error = "Order not found." });

        if (order.PaymentStatus != OrderPaymentStatus.Fulfilled &&
            order.PaymentStatus != OrderPaymentStatus.PartiallyRefunded)
            return Results.Conflict(new { error = $"Order cannot be refunded in status '{order.PaymentStatus}'." });

        var paymentRecordSpec = new PaymentRecordByOrderIdSpecWithRefunds(request.OrderId);
        var paymentRecord = (await paymentRepo.ListAsync(paymentRecordSpec)).FirstOrDefault();
        if (paymentRecord?.CaptureId == null)
            return Results.Problem("Payment record missing capture.", statusCode: 500);

        // Idempotency: return existing refund if key already used
        var existing = paymentRecord.FindRefundByIdempotencyKey(request.IdempotencyKey);
        if (existing != null)
            return Results.Ok(new RefundOrderResponse { RefundId = existing.PayPalRefundId, Amount = existing.Amount });

        decimal? refundAmount = request.Amount > 0 ? request.Amount : null;

        if (refundAmount.HasValue && !paymentRecord.CanRefund(refundAmount.Value))
            return Results.BadRequest(new { error = "Refund amount exceeds remaining capturable amount." });

        try
        {
            var refundResult = await payPalService.RefundAsync(
                paymentRecord.CaptureId, refundAmount, settings.Currency, request.IdempotencyKey);

            var actualAmount = refundAmount ?? refundResult.Amount;
            var refundRecord = paymentRecord.AddRefund(refundResult.RefundId, actualAmount, request.IdempotencyKey);
            await paymentRepo.UpdateAsync(paymentRecord);

            if (order.PaymentStatus != paymentRecord.Status.ToOrderStatus())
            {
                order.SetPaymentStatus(paymentRecord.Status.ToOrderStatus());
                await orderRepo.UpdateAsync(order);
            }

            return Results.Ok(new RefundOrderResponse { RefundId = refundResult.RefundId, Amount = actualAmount });
        }
        catch (PayPalProviderException ex)
        {
            return Results.Problem(ex.Message, statusCode: 502);
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.Problem("PayPal returned an unreadable response.", statusCode: 502);
        }
    }
}
