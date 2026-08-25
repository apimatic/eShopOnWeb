using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IRepository<Order>>
{
    private readonly IPaymentService _paymentService;
    private readonly string _currency;

    public RefundOrderEndpoint(IPaymentService paymentService, IConfiguration config)
    {
        _paymentService = paymentService;
        _currency = config["PayPal:Currency"] ?? "USD";
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, [FromBody] RefundOrderRequest request, IRepository<Order> orderRepo, HttpContext httpContext) =>
            {
                request.OrderId = orderId;
                request.BuyerId = httpContext.User.Identity!.Name!;
                return await HandleAsync(request, orderRepo);
            })
            .Produces<RefundOrderResponse>()
            .ProducesProblem(400)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IRepository<Order> orderRepo)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest("Idempotency key is required.");

        var order = await orderRepo.GetByIdAsync(request.OrderId);
        if (order is null)
            return Results.NotFound();
        if (order.BuyerId != request.BuyerId)
            return Results.Forbid();
        if (order.PaymentStatus != OrderPaymentStatus.Captured &&
            order.PaymentStatus != OrderPaymentStatus.PartiallyRefunded)
            return Results.BadRequest($"Order is in '{order.PaymentStatus}' state and cannot be refunded.");
        if (string.IsNullOrEmpty(order.CaptureId))
            return Results.BadRequest("Order has no capture ID.");

        var refundAmount = request.Amount ?? (order.CapturedAmount - order.TotalRefunded);
        if (refundAmount <= 0)
            return Results.BadRequest("Refund amount must be positive.");

        var refundId = await _paymentService.RefundPaymentAsync(
            order.CaptureId, request.Amount, _currency, request.IdempotencyKey);

        var refund = order.AddRefund(refundId, refundAmount, request.IdempotencyKey);
        await orderRepo.UpdateAsync(order);

        return Results.Ok(new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refundId,
            Amount = refundAmount
        });
    }
}
