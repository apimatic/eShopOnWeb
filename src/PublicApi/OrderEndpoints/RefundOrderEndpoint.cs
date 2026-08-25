using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequestBody
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? Reason { get; set; }
}

public class RefundOrderRequest : BaseRequest
{
    public RefundOrderRequest(int orderId, RefundOrderRequestBody body, string buyerId)
    {
        OrderId = orderId;
        Body = body;
        BuyerId = buyerId;
    }

    public int OrderId { get; }
    public RefundOrderRequestBody Body { get; }
    public string BuyerId { get; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal TotalRefunded { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
}

/// <summary>
/// Refunds a captured payment, in full or in part. Repeating the same idempotency key never
/// refunds twice; a partly-refunded order can never be refunded beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, PaymentDependencies>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequestBody body, ClaimsPrincipal user,
             IRepository<Order> orderRepository, IRepository<Payment> paymentRepository, IRepository<Buyer> buyerRepository,
             IRepository<CatalogItem> catalogItemRepository, IPayPalClient payPalClient, IOptions<PayPalOptions> payPalOptions) =>
            {
                var request = new RefundOrderRequest(orderId, body, user.Identity!.Name!);
                var deps = new PaymentDependencies(orderRepository, paymentRepository, buyerRepository, catalogItemRepository, payPalClient, payPalOptions.Value);
                return await HandleAsync(request, deps);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, PaymentDependencies deps)
    {
        if (string.IsNullOrWhiteSpace(request.Body.IdempotencyKey))
        {
            return Results.BadRequest("idempotencyKey is required.");
        }

        var order = await deps.OrderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdAndBuyerSpec(request.OrderId, request.BuyerId));
        if (order == null)
        {
            return Results.NotFound();
        }

        var payment = await deps.PaymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(request.OrderId));
        if (payment == null || payment.PayPalCaptureId == null || payment.CapturedAmount == null)
        {
            return Results.Conflict($"Order {order.Id} has no captured payment to refund.");
        }

        // Idempotent by caller-supplied key: repeating the same key returns the original
        // refund instead of refunding again; a different key is a legitimate new (partial)
        // refund of the same capture.
        var existingRefund = payment.GetRefundByIdempotencyKey(request.Body.IdempotencyKey);
        if (existingRefund != null)
        {
            return Results.Ok(BuildResponse(request, payment, existingRefund, order));
        }

        if (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded)
        {
            return Results.Conflict($"Order {order.Id} has no captured payment to refund (status: {order.Status}).");
        }

        var remainingRefundable = payment.CapturedAmount.Value - payment.RefundedAmount;
        var amountToRefund = request.Body.Amount ?? remainingRefundable;

        if (amountToRefund <= 0 || amountToRefund > remainingRefundable)
        {
            return Results.UnprocessableEntity($"Refund amount must be greater than zero and at most the remaining refundable amount ({remainingRefundable} {payment.Currency}).");
        }

        PayPalRefundResult refundResult;
        try
        {
            refundResult = await deps.PayPalClient.RefundCaptureAsync(payment.PayPalCaptureId, amountToRefund, payment.Currency, request.Body.IdempotencyKey);
        }
        catch (PayPalApiException ex)
        {
            return Results.Problem(ex.Message, statusCode: 502, title: ex.ErrorName ?? "Refund failed");
        }

        var refund = payment.AddRefund(request.Body.IdempotencyKey, refundResult.RefundId, refundResult.Status, refundResult.Amount);
        order.MarkRefunded(payment.Status == PaymentStatus.Refunded);

        await deps.PaymentRepository.UpdateAsync(payment);
        await deps.OrderRepository.UpdateAsync(order);

        return Results.Ok(BuildResponse(request, payment, refund, order));
    }

    private static RefundOrderResponse BuildResponse(RefundOrderRequest request, Payment payment, Refund refund, Order order)
    {
        return new RefundOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            RefundId = refund.PayPalRefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            TotalRefunded = payment.RefundedAmount,
            OrderStatus = order.Status.ToString()
        };
    }
}
