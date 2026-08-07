using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Issues a full refund for an order's PayPal payment. Idempotent in effect: a double-click never
/// produces a double refund.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderEndpoint.RefundRouteRequest, ClaimsPrincipal>
{
    private const string Currency = "USD";

    private readonly IRepository<Order> _orderRepository;
    private readonly IPayPalPaymentGateway _payPal;
    private readonly KeyedAsyncLock _paymentLock;
    private readonly ILogger<RefundOrderEndpoint> _logger;

    public RefundOrderEndpoint(
        IRepository<Order> orderRepository,
        IPayPalPaymentGateway payPal,
        KeyedAsyncLock paymentLock,
        ILogger<RefundOrderEndpoint> logger)
    {
        _orderRepository = orderRepository;
        _payPal = payPal;
        _paymentLock = paymentLock;
        _logger = logger;
    }

    /// <summary>Carries the order id bound from the route (this endpoint has no request body).</summary>
    public class RefundRouteRequest : BaseRequest
    {
        public int OrderId { get; set; }
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user) => await HandleAsync(new RefundRouteRequest { OrderId = orderId }, user))
            .Produces<RefundOrderResponse>()
            .ProducesProblem(StatusCodes.Status402PaymentRequired)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundRouteRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();

        using var _ = await _paymentLock.LockAsync($"order-{request.OrderId}");

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order == null || order.BuyerId != buyerId)
        {
            return Results.NotFound(new { message = $"Order {request.OrderId} was not found." });
        }

        if (order.PaymentStatus == OrderPaymentStatus.Refunded)
        {
            return Results.Ok(BuildResponse(request, order));
        }
        if (order.PaymentStatus != OrderPaymentStatus.Paid || string.IsNullOrEmpty(order.PayPalCaptureId))
        {
            return Results.Json(
                new { message = $"Order {order.Id} cannot be refunded because it is not paid (status: {order.PaymentStatus})." },
                statusCode: StatusCodes.Status409Conflict);
        }

        // As with pay, the per-order lock plus persisted Refunded state prevent a double refund; the
        // PayPal-Request-Id is a fresh globally-unique value so it never collides with a prior run.
        var idempotencyKey = Guid.NewGuid().ToString("N");
        var result = await _payPal.RefundCaptureAsync(order.PayPalCaptureId!, idempotencyKey);

        if (!result.Succeeded)
        {
            _logger.LogWarning("Refund for order {OrderId} failed: {Reason}", order.Id, result.FailureReason);
            return Results.Json(
                new { message = "Refund was not successful.", reason = result.FailureReason, status = result.Status },
                statusCode: StatusCodes.Status402PaymentRequired);
        }

        order.MarkAsRefunded(result.RefundId!);
        await _orderRepository.UpdateAsync(order);
        _logger.LogInformation("Order {OrderId} refunded (refund {RefundId}).", order.Id, result.RefundId);

        return Results.Ok(BuildResponse(request, order));
    }

    private static RefundOrderResponse BuildResponse(RefundRouteRequest request, Order order)
        => new(request.CorrelationId())
        {
            OrderId = order.Id,
            PaymentStatus = order.PaymentStatus.ToString(),
            PayPalRefundId = order.PayPalRefundId,
            AmountRefunded = order.Total(),
            Currency = Currency
        };
}
