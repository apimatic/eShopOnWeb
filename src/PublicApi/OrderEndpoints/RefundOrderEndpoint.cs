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
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   RefundOrderRequest request,
                   IRepository<Order> orderRepo,
                   PayPalPaymentService paypal,
                   CancellationToken ct) =>
            {
                var spec = new OrderByIdSpec(orderId);
                var order = await orderRepo.FirstOrDefaultAsync(spec, ct);
                if (order == null) return Results.NotFound();

                if (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.Refunded)
                    return Results.Conflict(new { error = $"Order is in status {order.Status}, cannot refund." });

                if (string.IsNullOrEmpty(request.IdempotencyKey))
                    return Results.BadRequest(new { error = "idempotencyKey is required." });

                if (!decimal.TryParse(order.Payment!.CapturedAmountValue, out var captured))
                    return Results.Problem("Captured amount is not recorded.", statusCode: 500);

                decimal? refundAmount = request.Amount;
                if (refundAmount.HasValue)
                {
                    var alreadyRefunded = order.Payment.TotalRefunded;
                    if (refundAmount.Value <= 0)
                        return Results.BadRequest(new { error = "Refund amount must be positive." });
                    if (alreadyRefunded + refundAmount.Value > captured)
                        return Results.UnprocessableEntity(new
                        {
                            error = $"Refund of {refundAmount:F2} would exceed captured amount {captured:F2} (already refunded: {alreadyRefunded:F2})."
                        });
                }

                RefundResult result;
                try
                {
                    result = await paypal.RefundAsync(
                        captureId: order.Payment.CaptureId!,
                        amount: refundAmount,
                        idempotencyKey: request.IdempotencyKey,
                        ct: ct);
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: ex.HttpStatusCode);
                }

                var recordedAmount = refundAmount ?? captured - order.Payment.TotalRefunded;
                order.RecordRefund(recordedAmount);
                await orderRepo.UpdateAsync(order, ct);

                return Results.Ok(new RefundOrderResponse
                {
                    RefundId = result.RefundId,
                    OrderId = order.Id,
                    OrderStatus = order.Status.ToString(),
                    RefundedAmount = result.RefundedAmount,
                    Currency = result.Currency
                });
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IRepository<Order> service)
        => throw new System.NotSupportedException();
}

public class RefundOrderRequest : BaseRequest
{
    public decimal? Amount { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string? RefundedAmount { get; set; }
    public string? Currency { get; set; }
}
