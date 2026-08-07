using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays for an order with PayPal, either with card details for a one-off payment or with a saved card.
/// Idempotent in effect: the order's persisted payment state plus a per-order lock mean a double-click
/// never charges twice (an already-paid order short-circuits before reaching PayPal).
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal>
{
    private const string Currency = "USD";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalPaymentGateway _payPal;
    private readonly KeyedAsyncLock _paymentLock;
    private readonly ILogger<PayOrderEndpoint> _logger;

    public PayOrderEndpoint(
        IRepository<Order> orderRepository,
        IRepository<Buyer> buyerRepository,
        IPayPalPaymentGateway payPal,
        KeyedAsyncLock paymentLock,
        ILogger<PayOrderEndpoint> logger)
    {
        _orderRepository = orderRepository;
        _buyerRepository = buyerRepository;
        _payPal = payPal;
        _paymentLock = paymentLock;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user);
            })
            .Produces<PayOrderResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status402PaymentRequired)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();

        var hasCard = request.Card != null;
        var hasSaved = request.SavedPaymentMethodId.HasValue;
        if (hasCard == hasSaved)
        {
            return Results.BadRequest(new { message = "Provide either card details or a saved payment method id, but not both." });
        }

        // Serialise all pay/refund activity for this order so concurrent double-clicks can't race.
        using var _ = await _paymentLock.LockAsync($"order-{request.OrderId}");

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order == null || order.BuyerId != buyerId)
        {
            return Results.NotFound(new { message = $"Order {request.OrderId} was not found." });
        }

        // Idempotent short-circuits.
        if (order.PaymentStatus == OrderPaymentStatus.Paid)
        {
            return Results.Ok(BuildResponse(request, order, brand: null, last4: null));
        }
        if (order.PaymentStatus == OrderPaymentStatus.Refunded)
        {
            return Results.Json(new { message = $"Order {order.Id} has been refunded and cannot be paid." },
                statusCode: StatusCodes.Status409Conflict);
        }

        var amount = order.Total();
        if (amount <= 0m)
        {
            return Results.BadRequest(new { message = "Order total must be greater than zero to take a payment." });
        }

        // Double-charge protection comes from the per-order lock above plus the persisted terminal state
        // (an already-Paid order short-circuits before reaching PayPal). The PayPal-Request-Id is a fresh
        // globally-unique value per attempt so it can never collide with a prior run's request ids
        // (order ids reset each in-memory run, but PayPal remembers request ids for hours).
        var idempotencyKey = Guid.NewGuid().ToString("N");

        PayPalPaymentResult result;
        if (hasSaved)
        {
            var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId));
            var paymentMethod = buyer?.FindPaymentMethod(request.SavedPaymentMethodId!.Value);
            if (paymentMethod == null)
            {
                return Results.NotFound(new { message = $"Saved payment method {request.SavedPaymentMethodId} was not found." });
            }

            result = await _payPal.ChargeVaultedCardAsync(amount, Currency, paymentMethod.VaultId, idempotencyKey);
        }
        else
        {
            result = await _payPal.ChargeCardAsync(amount, Currency, request.Card!.ToCardDetails(), idempotencyKey);
        }

        if (!result.Succeeded)
        {
            order.MarkPaymentFailed();
            await _orderRepository.UpdateAsync(order);
            _logger.LogWarning("Payment for order {OrderId} failed: {Reason}", order.Id, result.FailureReason);
            return Results.Json(
                new { message = "Payment was not successful.", reason = result.FailureReason, status = result.Status },
                statusCode: StatusCodes.Status402PaymentRequired);
        }

        order.MarkAsPaid(result.PayPalOrderId!, result.CaptureId!);
        await _orderRepository.UpdateAsync(order);
        _logger.LogInformation("Order {OrderId} paid (capture {CaptureId}).", order.Id, result.CaptureId);

        return Results.Ok(BuildResponse(request, order, result.Brand, result.Last4));
    }

    private static PayOrderResponse BuildResponse(PayOrderRequest request, Order order, string? brand, string? last4)
        => new(request.CorrelationId())
        {
            OrderId = order.Id,
            PaymentStatus = order.PaymentStatus.ToString(),
            PayPalCaptureId = order.PayPalCaptureId,
            CardBrand = brand,
            CardLast4 = last4,
            AmountPaid = order.Total(),
            Currency = Currency
        };
}
