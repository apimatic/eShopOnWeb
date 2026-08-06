using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(IRepository<Order> orderRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPaymentGateway paymentGateway,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _savedCardRepository = savedCardRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<Result<Order>> PayWithCardAsync(string buyerId, int orderId, CardDetails card,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(card, nameof(card));

        var (order, failure) = await LoadPayableOrderAsync(buyerId, orderId, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        // Already paid → return the existing state without charging again (idempotent).
        if (order!.PaymentStatus == OrderPaymentStatus.Paid)
        {
            return Result<Order>.Success(order);
        }

        var capture = await _paymentGateway.ChargeCardAsync(order.Total(), order.Currency, card,
            IdempotencyKeyForPayment(order), cancellationToken);

        return await ApplyCaptureAsync(order, capture, cancellationToken);
    }

    public async Task<Result<Order>> PayWithSavedCardAsync(string buyerId, int orderId, int savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var (order, failure) = await LoadPayableOrderAsync(buyerId, orderId, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        if (order!.PaymentStatus == OrderPaymentStatus.Paid)
        {
            return Result<Order>.Success(order);
        }

        // The saved card must belong to this shopper; a wrong owner yields no match.
        var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpecification(savedPaymentMethodId, buyerId), cancellationToken);
        if (savedCard is null)
        {
            return Result<Order>.NotFound($"Saved card {savedPaymentMethodId} was not found.");
        }

        var capture = await _paymentGateway.ChargeVaultedCardAsync(order.Total(), order.Currency, savedCard.VaultId,
            IdempotencyKeyForPayment(order), cancellationToken);

        return await ApplyCaptureAsync(order, capture, cancellationToken);
    }

    public async Task<Result<Order>> RefundAsync(string buyerId, int orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            return Result<Order>.NotFound($"Order {orderId} was not found.");
        }

        // Already refunded → return the existing state without refunding again (idempotent).
        if (order.PaymentStatus == OrderPaymentStatus.Refunded)
        {
            return Result<Order>.Success(order);
        }

        if (order.PaymentStatus != OrderPaymentStatus.Paid || string.IsNullOrEmpty(order.PaymentCaptureId))
        {
            throw new PaymentStateConflictException($"Order {orderId} is not paid and cannot be refunded.");
        }

        var refund = await _paymentGateway.RefundAsync(order.PaymentCaptureId,
            IdempotencyKeyForRefund(order), cancellationToken);

        order.MarkRefunded(refund.RefundId);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Order {order.Id} refunded (refund {refund.RefundId}, status {refund.Status}).");

        return Result<Order>.Success(order);
    }

    private async Task<(Order? order, Result<Order>? failure)> LoadPayableOrderAsync(string buyerId, int orderId,
        CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            return (null, Result<Order>.NotFound($"Order {orderId} was not found."));
        }

        if (order.PaymentStatus == OrderPaymentStatus.Refunded)
        {
            throw new PaymentStateConflictException($"Order {orderId} has been refunded and cannot be paid.");
        }

        return (order, null);
    }

    private async Task<Result<Order>> ApplyCaptureAsync(Order order, PaymentCaptureResult capture,
        CancellationToken cancellationToken)
    {
        order.MarkPaid(capture.ProviderOrderId, capture.CaptureId);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Order {order.Id} paid (capture {capture.CaptureId}, status {capture.Status}).");
        return Result<Order>.Success(order);
    }

    // Stable per-order keys so a double-click replays the original PayPal request rather than
    // creating a second charge or refund.
    private static string IdempotencyKeyForPayment(Order order) =>
        string.Create(CultureInfo.InvariantCulture, $"eshop-pay-order-{order.Id}");

    private static string IdempotencyKeyForRefund(Order order) =>
        string.Create(CultureInfo.InvariantCulture, $"eshop-refund-order-{order.Id}");
}
