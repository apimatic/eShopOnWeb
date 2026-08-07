using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private const string Currency = "USD";

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedMethodRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<SavedPaymentMethod> savedMethodRepository,
        IPayPalClient payPalClient,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _savedMethodRepository = savedMethodRepository;
        _payPalClient = payPalClient;
        _logger = logger;
    }

    public async Task<Order> PayOrderWithCardAsync(
        int orderId, string buyerId, CardPaymentDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(card, nameof(card));
        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

        // Idempotent: if already paid, do not charge again.
        if (order.PaymentStatus == OrderPaymentStatus.Paid)
            return order;
        EnsurePayable(order);

        var result = await _payPalClient.CreateCardOrderAsync(
            order.Total(), Currency, card, PayIdempotencyKey(order), cancellationToken);

        return await MarkPaidAsync(order, result, cancellationToken);
    }

    public async Task<Order> PayOrderWithSavedMethodAsync(
        int orderId, string buyerId, int savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

        if (order.PaymentStatus == OrderPaymentStatus.Paid)
            return order;
        EnsurePayable(order);

        // The saved card must exist AND belong to this shopper, else it is treated as absent.
        var savedMethod = await _savedMethodRepository.GetByIdAsync(savedPaymentMethodId, cancellationToken);
        if (savedMethod is null || savedMethod.BuyerId != buyerId)
            throw new EntityNotFoundException($"Saved payment method {savedPaymentMethodId} was not found.");

        var result = await _payPalClient.CreateVaultedCardOrderAsync(
            order.Total(), Currency, savedMethod.PayPalVaultId, PayIdempotencyKey(order), cancellationToken);

        return await MarkPaidAsync(order, result, cancellationToken);
    }

    public async Task<Order> RefundOrderAsync(
        int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

        // Idempotent: a second refund request just returns the already-refunded order.
        if (order.PaymentStatus == OrderPaymentStatus.Refunded)
            return order;

        if (order.PaymentStatus != OrderPaymentStatus.Paid || string.IsNullOrEmpty(order.PaymentCaptureId))
            throw new OrderPaymentException($"Order {order.Id} cannot be refunded because it has not been paid.");

        var result = await _payPalClient.RefundCaptureAsync(
            order.PaymentCaptureId!, RefundIdempotencyKey(order), cancellationToken);

        order.SetRefunded(result.RefundId);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Order {order.Id} refunded (refund {result.RefundId}).");
        return order;
    }

    private async Task<Order> MarkPaidAsync(Order order, PayPalPaymentResult result, CancellationToken cancellationToken)
    {
        order.SetPaid(result.PayPalOrderId, result.CaptureId);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Order {order.Id} paid (PayPal order {result.PayPalOrderId}, capture {result.CaptureId}).");
        return order;
    }

    private async Task<Order> GetOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);

        // Same response whether the order is missing or owned by someone else, so we
        // never disclose another shopper's order.
        if (order is null || order.BuyerId != buyerId)
            throw new EntityNotFoundException($"Order {orderId} was not found.");

        return order;
    }

    private static void EnsurePayable(Order order)
    {
        if (order.PaymentStatus == OrderPaymentStatus.Refunded)
            throw new OrderPaymentException($"Order {order.Id} has been refunded and cannot be paid.");
    }

    // Deterministic idempotency keys. Concurrent double-clicks on the same order
    // compute the identical PayPal-Request-Id (same id + persisted OrderDate), so
    // PayPal de-duplicates them. The OrderDate component keeps keys globally unique
    // across app restarts even if the store reassigns the same order id.
    private static string PayIdempotencyKey(Order order) => DeterministicKey($"pay:{order.Id}:{order.OrderDate.UtcTicks}");
    private static string RefundIdempotencyKey(Order order) => DeterministicKey($"refund:{order.Id}:{order.OrderDate.UtcTicks}");

    /// <summary>Stable UUID derived from a seed, within PayPal-Request-Id length limits.</summary>
    private static string DeterministicKey(string seed)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash).ToString();
    }
}
