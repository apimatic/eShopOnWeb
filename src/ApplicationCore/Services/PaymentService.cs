using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // PayPal authorizations can be reauthorized only from day 4 through day 29 after the
    // original authorization; afterwards a new authorization (a new payment) is required.
    private static readonly TimeSpan ReauthorizationWindow = TimeSpan.FromDays(29);

    // Serializes payment state transitions per order so a double-click can never
    // authorize, capture, void or refund twice.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IPaymentGateway _gateway;
    private readonly PayPalSettings _settings;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IRepository<CatalogItem> itemRepository,
        IPaymentGateway gateway,
        PayPalSettings settings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _itemRepository = itemRepository;
        _gateway = gateway;
        _settings = settings;
    }

    public async Task<OrderSummaryDto> CreateOrderAsync(string buyerId, IReadOnlyList<(int CatalogItemId, int Quantity)> items, Address shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new PaymentConflictException("An order must contain at least one item.");
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new PaymentConflictException("Item quantities must be positive.");
        }

        var spec = new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(spec, ct);

        var orderItems = new List<OrderItem>();
        foreach (var requested in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == requested.CatalogItemId);
            if (catalogItem is null)
            {
                throw new EntityNotFoundException($"Catalog item {requested.CatalogItemId} was not found.");
            }
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri ?? string.Empty);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, requested.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        return ToOrderSummary(order, null);
    }

    public async Task<PaymentDto> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct = default)
    {
        if (card is null && savedPaymentMethodId is null)
        {
            throw new PaymentConflictException("Provide either card details or a savedPaymentMethodId.");
        }

        var gate = await EnterOrderLockAsync(orderId, ct);
        try
        {
            var order = await GetBuyerOrderAsync(buyerId, orderId, ct);
            var existing = await GetPaymentAsync(orderId, ct);

            if (order.Status == OrderStatus.PaymentAuthorized && existing is not null)
            {
                return ToPaymentDto(existing); // idempotent replay of a successful pay
            }
            if (order.Status != OrderStatus.PendingPayment)
            {
                throw new PaymentConflictException($"Order {orderId} cannot be paid while in status {order.Status}.");
            }

            string? vaultTokenId = null;
            if (savedPaymentMethodId is not null)
            {
                var savedCard = await _savedCardRepository.GetByIdAsync(savedPaymentMethodId.Value, ct);
                if (savedCard is null || savedCard.BuyerId != buyerId)
                {
                    throw new EntityNotFoundException($"Saved payment method {savedPaymentMethodId} was not found.");
                }
                vaultTokenId = savedCard.VaultTokenId;
            }

            var payment = existing ?? new Payment(order.Id, buyerId, order.Total(), _settings.Currency);
            var command = new AuthorizePaymentCommand
            {
                Amount = order.Total(),
                Currency = _settings.Currency,
                OrderReference = $"eshop-order-{order.Id}",
                CreateOrderIdempotencyKey = $"eshop-order-{order.Id}-create-{Guid.NewGuid():N}",
                AuthorizeIdempotencyKey = $"eshop-order-{order.Id}-authorize-{Guid.NewGuid():N}",
                Card = card,
                VaultTokenId = vaultTokenId
            };

            AuthorizationResult authorization;
            try
            {
                authorization = await _gateway.AuthorizeOrderAsync(command, ct);
            }
            catch
            {
                payment.MarkFailed();
                await SavePaymentAsync(payment, existing is null, ct);
                throw;
            }

            payment.MarkAuthorized(authorization.PayPalOrderId, authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt);
            await SavePaymentAsync(payment, existing is null, ct);

            order.MarkPaymentAuthorized();
            await _orderRepository.UpdateAsync(order, ct);

            return ToPaymentDto(payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaymentDto> FulfilOrderAsync(int orderId, CancellationToken ct = default)
    {
        var gate = await EnterOrderLockAsync(orderId, ct);
        try
        {
            var order = await GetOrderWithItemsAsync(orderId, ct);
            if (order is null)
            {
                throw new EntityNotFoundException($"Order {orderId} was not found.");
            }

            var payment = await GetPaymentAsync(orderId, ct);
            if (payment is null)
            {
                throw new PaymentConflictException($"Order {orderId} has no payment; it cannot be fulfilled before it is paid.");
            }
            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return ToPaymentDto(payment); // already captured: idempotent replay
            }
            if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            {
                throw new PaymentConflictException($"Order {orderId} cannot be fulfilled while its payment is {payment.Status}.");
            }

            var authorization = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, ct);
            await EnsureCapturableAsync(payment, authorization, ct);

            var capture = await _gateway.CaptureAuthorizationAsync(
                payment.AuthorizationId, $"eshop-payment-{payment.Id}-capture", ct);

            payment.MarkCaptured(capture.CaptureId, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount);
            await _paymentRepository.UpdateAsync(payment, ct);

            order.MarkFulfilled();
            await _orderRepository.UpdateAsync(order, ct);

            return ToPaymentDto(payment);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task EnsureCapturableAsync(Payment payment, AuthorizationState authorization, CancellationToken ct)
    {
        switch (authorization.Status)
        {
            case "CREATED":
            case "PARTIALLY_CAPTURED":
                break;
            case "PENDING":
                throw new PaymentConflictException(
                    $"The PayPal authorization for order {payment.OrderId} is still pending review. Retry fulfilment later.");
            case "CAPTURED":
                throw new PaymentConflictException(
                    $"The PayPal authorization for order {payment.OrderId} was already captured outside this system. Reconcile the payment before fulfilling.");
            default:
                throw new PaymentConflictException(
                    $"The PayPal authorization for order {payment.OrderId} is {authorization.Status} and cannot be captured. Cancel the order and ask the shopper to pay again.");
        }

        var expired = authorization.ExpiresAt is not null && authorization.ExpiresAt <= DateTimeOffset.UtcNow;
        if (!expired)
        {
            return;
        }

        if (payment.AuthorizedAt is not null && DateTimeOffset.UtcNow - payment.AuthorizedAt > ReauthorizationWindow)
        {
            throw new PaymentConflictException(
                $"The PayPal authorization for order {payment.OrderId} expired and is too old to renew " +
                "(PayPal allows renewal only within 29 days of the original authorization). " +
                "Cancel this order and ask the shopper to place it again.");
        }

        var renewed = await _gateway.ReauthorizeAsync(
            payment.AuthorizationId!, payment.AuthorizedAmount, payment.Currency,
            $"eshop-payment-{payment.Id}-reauthorize-{Guid.NewGuid():N}", ct);
        payment.MarkAuthorizationRenewed(renewed.Status, renewed.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, ct);
    }

    public async Task<OrderSummaryDto> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var gate = await EnterOrderLockAsync(orderId, ct);
        try
        {
            var order = await GetOrderWithItemsAsync(orderId, ct);
            if (order is null)
            {
                throw new EntityNotFoundException($"Order {orderId} was not found.");
            }
            if (order.Status == OrderStatus.Cancelled)
            {
                var existingPayment = await GetPaymentAsync(orderId, ct);
                return ToOrderSummary(order, existingPayment); // idempotent replay
            }

            var payment = await GetPaymentAsync(orderId, ct);
            if (payment?.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
            {
                await _gateway.VoidAuthorizationAsync(payment.AuthorizationId, $"eshop-payment-{payment.Id}-void", ct);
                payment.MarkVoided();
                await _paymentRepository.UpdateAsync(payment, ct);
            }

            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, ct);

            return ToOrderSummary(order, payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundDto> RefundOrderAsync(string buyerId, bool isAdmin, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var gate = await EnterOrderLockAsync(orderId, ct);
        try
        {
            var order = await GetOrderWithItemsAsync(orderId, ct);
            if (order is null || (!isAdmin && order.BuyerId != buyerId))
            {
                throw new EntityNotFoundException($"Order {orderId} was not found.");
            }

            var payment = await GetPaymentAsync(orderId, ct);
            if (payment is null || payment.CaptureId is null)
            {
                throw new PaymentConflictException($"Order {orderId} has no captured payment to refund.");
            }

            var replay = payment.FindRefundByIdempotencyKey(idempotencyKey);
            if (replay is not null)
            {
                return ToRefundDto(replay, payment); // same key: never refund twice
            }

            var refundAmount = amount ?? payment.RemainingRefundableAmount;
            if (refundAmount <= 0m || refundAmount > payment.RemainingRefundableAmount)
            {
                throw new PaymentConflictException(
                    $"Refund of {refundAmount} {payment.Currency} exceeds the remaining refundable amount " +
                    $"of {payment.RemainingRefundableAmount} {payment.Currency} for order {orderId}.");
            }

            var result = await _gateway.RefundCaptureAsync(payment.CaptureId, refundAmount, payment.Currency, idempotencyKey, ct);

            var refund = payment.AddRefund(result.RefundId, refundAmount, result.Status, idempotencyKey);
            await _paymentRepository.UpdateAsync(payment, ct);

            return ToRefundDto(refund, payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<OrderSummaryDto>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerIdSpec(buyerId), ct);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => ToOrderSummary(o, paymentsByOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    public async Task<SavedCardDto> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var existingCards = await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
        var payPalCustomerId = existingCards.FirstOrDefault(c => c.PayPalCustomerId is not null)?.PayPalCustomerId;

        var result = await _gateway.SaveCardAsync(new SaveCardCommand
        {
            BuyerId = buyerId,
            PayPalCustomerId = payPalCustomerId,
            IdempotencyKey = $"eshop-vault-{buyerId}-{Guid.NewGuid():N}",
            Card = card
        }, ct);

        var saved = new SavedPaymentMethod(buyerId, result.PayPalCustomerId, result.VaultTokenId,
            result.Brand, result.LastDigits, result.Expiry, result.CardholderName);
        saved = await _savedCardRepository.AddAsync(saved, ct);

        return ToSavedCardDto(saved);
    }

    public async Task<IReadOnlyList<SavedCardDto>> GetSavedCardsAsync(string buyerId, CancellationToken ct = default)
    {
        var cards = await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
        return cards.Select(ToSavedCardDto).ToList();
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        var savedCard = await _savedCardRepository.GetByIdAsync(paymentMethodId, ct);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            throw new EntityNotFoundException($"Saved payment method {paymentMethodId} was not found.");
        }

        try
        {
            await _gateway.DeleteSavedCardAsync(savedCard.VaultTokenId, ct);
        }
        catch (PaymentGatewayException ex) when (ex.ProviderStatusCode == 404)
        {
            // Already gone at PayPal; removing the local record is the desired end state.
        }

        await _savedCardRepository.DeleteAsync(savedCard, ct);
    }

    public async Task<ReconciliationDto> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to <= from)
        {
            throw new PaymentConflictException("The 'to' timestamp must be after the 'from' timestamp.");
        }

        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsCreatedBetweenSpec(from, to), ct);

        var report = new ReconciliationDto { From = from, To = to };
        var matchedPaymentIds = new HashSet<int>();

        foreach (var tx in transactions)
        {
            var match = payments.FirstOrDefault(p =>
                p.AuthorizationId == tx.TransactionId ||
                p.CaptureId == tx.TransactionId ||
                p.Refunds.Any(r => r.PayPalRefundId == tx.TransactionId) ||
                (tx.ReferenceIdType == "ODR" && p.PayPalOrderId == tx.ReferenceId));

            if (match is not null)
            {
                matchedPaymentIds.Add(match.Id);
                report.MatchedCount++;
            }

            report.Transactions.Add(new ReconciliationEntryDto
            {
                PayPalTransactionId = tx.TransactionId,
                PayPalReferenceId = tx.ReferenceId,
                PayPalReferenceIdType = tx.ReferenceIdType,
                EventCode = tx.EventCode,
                Status = tx.Status,
                Amount = tx.Amount,
                Currency = tx.Currency,
                Fee = tx.Fee,
                InitiatedAt = tx.InitiatedAt,
                UpdatedAt = tx.UpdatedAt,
                MatchedOrderId = match?.OrderId,
                MatchedPaymentId = match?.Id
            });
        }

        report.TotalPayPalTransactions = report.Transactions.Count;
        report.PaymentsMissingFromPayPal = payments
            .Where(p => !matchedPaymentIds.Contains(p.Id))
            .Select(p => new UnmatchedPaymentDto
            {
                PaymentId = p.Id,
                OrderId = p.OrderId,
                Status = p.Status.ToString(),
                PayPalOrderId = p.PayPalOrderId,
                AuthorizationId = p.AuthorizationId,
                CaptureId = p.CaptureId,
                AuthorizedAmount = p.AuthorizedAmount,
                CapturedAmount = p.CapturedAmount,
                Currency = p.Currency
            })
            .ToList();

        return report;
    }

    private async Task<Order?> GetOrderWithItemsAsync(int orderId, CancellationToken ct) =>
        await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);

    private async Task<Order> GetBuyerOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await GetOrderWithItemsAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    private async Task<Payment?> GetPaymentAsync(int orderId, CancellationToken ct) =>
        await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);

    private async Task SavePaymentAsync(Payment payment, bool isNew, CancellationToken ct)
    {
        if (isNew)
        {
            await _paymentRepository.AddAsync(payment, ct);
        }
        else
        {
            await _paymentRepository.UpdateAsync(payment, ct);
        }
    }

    private static async Task<SemaphoreSlim> EnterOrderLockAsync(int orderId, CancellationToken ct)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return gate;
    }

    private OrderSummaryDto ToOrderSummary(Order order, Payment? payment) => new()
    {
        OrderId = order.Id,
        BuyerId = order.BuyerId,
        OrderDate = order.OrderDate,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Currency = payment?.Currency ?? _settings.Currency,
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Payment = payment is null ? null : ToPaymentDto(payment)
    };

    private static PaymentDto ToPaymentDto(Payment payment) => new()
    {
        PaymentId = payment.Id,
        OrderId = payment.OrderId,
        Status = payment.Status.ToString(),
        Currency = payment.Currency,
        AuthorizedAmount = payment.AuthorizedAmount,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        RefundedAmount = payment.RefundedAmount,
        Refunds = payment.Refunds.Select(r => ToRefundDto(r, payment)).ToList()
    };

    private static RefundDto ToRefundDto(PaymentRefund refund, Payment payment) => new()
    {
        RefundId = refund.Id,
        PayPalRefundId = refund.PayPalRefundId,
        PaymentId = payment.Id,
        OrderId = payment.OrderId,
        Amount = refund.Amount,
        Currency = payment.Currency,
        Status = refund.Status,
        IdempotencyKey = refund.IdempotencyKey,
        TotalRefunded = payment.RefundedAmount,
        RemainingRefundable = payment.RemainingRefundableAmount
    };

    private static SavedCardDto ToSavedCardDto(SavedPaymentMethod saved) => new()
    {
        PaymentMethodId = saved.Id,
        Brand = saved.CardBrand,
        LastDigits = saved.LastDigits,
        Expiry = saved.Expiry,
        CardholderName = saved.CardholderName,
        CreatedAt = saved.CreatedAt
    };
}
