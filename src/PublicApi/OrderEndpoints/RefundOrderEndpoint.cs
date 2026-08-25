using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IRepository<Order>>
{
    private readonly PayPalService _payPal;

    public RefundOrderEndpoint(PayPalService payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   RefundOrderRequest request,
                   IRepository<Order> orderRepository,
                   IRepository<RefundRecord> refundRepository,
                   CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    return Results.BadRequest("IdempotencyKey is required.");

                var spec = new OrderByIdSpec(orderId);
                var order = await orderRepository.FirstOrDefaultAsync(spec, ct);

                if (order == null) return Results.NotFound();
                if (order.Status != OrderStatus.Fulfilled &&
                    order.Status != OrderStatus.PartiallyRefunded)
                    return Results.BadRequest($"Order is in status {order.Status} and cannot be refunded.");
                if (string.IsNullOrEmpty(order.PayPalCaptureId))
                    return Results.BadRequest("Order has no capture ID to refund.");

                // Idempotency check — same key returns the existing refund
                var existingRefundSpec = new RefundByIdempotencyKeySpec(orderId, request.IdempotencyKey);
                var existingRefund = await refundRepository.FirstOrDefaultAsync(existingRefundSpec, ct);
                if (existingRefund != null)
                {
                    return Results.Ok(new RefundOrderResponse
                    {
                        RefundId = existingRefund.PayPalRefundId,
                        Amount = existingRefund.Amount,
                        Status = "Completed"
                    });
                }

                // Guard: partial-refund cap
                if (request.Amount.HasValue)
                {
                    var capturedAmount = order.CapturedAmount ?? order.Total();
                    var remaining = capturedAmount - order.TotalRefunded;
                    if (request.Amount.Value > remaining)
                        return Results.BadRequest(
                            $"Refund amount {request.Amount.Value:F2} exceeds remaining refundable amount {remaining:F2}.");
                }

                var result = await _payPal.RefundAsync(
                    order.PayPalCaptureId,
                    request.Amount,
                    request.IdempotencyKey,
                    ct);

                var refundAmount = request.Amount ?? (order.CapturedAmount ?? order.Total()) - order.TotalRefunded;
                var record = new RefundRecord(orderId, request.IdempotencyKey, result.RefundId, refundAmount);
                await refundRepository.AddAsync(record, ct);

                order.AddRefund(refundAmount);
                await orderRepository.UpdateAsync(order, ct);

                return Results.Created($"api/orders/{orderId}/refunds/{result.RefundId}", new RefundOrderResponse
                {
                    RefundId = result.RefundId,
                    Amount = refundAmount,
                    Status = result.Status
                });
            })
            .Produces<RefundOrderResponse>(201)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IRepository<Order> dep)
        => Task.FromResult(Results.StatusCode(501));
}

public class RefundOrderRequest : BaseRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse() : base(System.Guid.NewGuid()) { }
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}
