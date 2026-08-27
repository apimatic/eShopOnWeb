using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly Address DefaultShipTo = new("Not provided", "Not provided", "Not provided", "Not provided", "00000");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly PayPalSettings _payPalSettings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IRepository<CatalogItem> itemRepository,
        IPaymentGateway paymentGateway,
        PayPalSettings payPalSettings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _itemRepository = itemRepository;
        _paymentGateway = paymentGateway;
        _payPalSettings = payPalSettings;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items,
        Address? shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new OrderStateException("An order must contain at least one item.");
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new OrderStateException("Item quantities must be positive.");
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray()), ct);

        var missing = items.Select(i => i.CatalogItemId).Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new OrderStateException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipTo, orderItems);
        await _orderRepository.AddAsync(order, ct);
        return order;
    }

    public async Task<Payment?> PayOrderAsync(string buyerId, int orderId, CardDetails? card,
        int? savedPaymentMethodId, CancellationToken ct = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Fulfilled)
        {
            throw new OrderStateException($"Order {orderId} is {order.Status} and cannot be paid.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);

        // Idempotent in effect: a repeat pay on an already-held/captured order returns current state.
        if (payment is { Status: PaymentStatus.Authorized or PaymentStatus.Captured
                or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded })
        {
            return payment;
        }

        payment ??= new Payment(orderId, buyerId, order.Total(), _payPalSettings.Currency);

        GatewayAuthorization authorization;
        var requestId = payment.NextAuthorizationRequestId();
        try
        {
            if (savedPaymentMethodId.HasValue)
            {
                var method = await _paymentMethodRepository.GetByIdAsync(savedPaymentMethodId.Value, ct);
                if (method is null || method.BuyerId != buyerId)
                {
                    throw new PaymentGatewayException(PaymentFailureKind.NotFound,
                        $"Saved payment method {savedPaymentMethodId.Value} was not found.");
                }

                authorization = await _paymentGateway.AuthorizeVaultedCardPaymentAsync(
                    orderId, payment.Amount, payment.Currency, method.VaultTokenId, requestId, ct);
            }
            else
            {
                if (card is null)
                {
                    throw new PaymentGatewayException(PaymentFailureKind.Validation,
                        "Either card details or a savedPaymentMethodId must be supplied.");
                }

                authorization = await _paymentGateway.AuthorizeCardPaymentAsync(
                    orderId, payment.Amount, payment.Currency, card, requestId, ct);
            }
        }
        catch (PaymentGatewayException ex)
        {
            payment.RecordAuthorizationFailure(null, ex.Message);
            await SavePaymentAsync(payment, ct);
            throw;
        }

        payment.RecordAuthorization(authorization.PayPalOrderId, authorization.AuthorizationId,
            authorization.Status, authorization.ExpiresAt);
        order.MarkAuthorized();

        await SavePaymentAsync(payment, ct);
        await _orderRepository.UpdateAsync(order, ct);
        return payment;
    }

    public async Task<Payment?> FulfilOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null)
        {
            return null;
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);

        // Idempotent: fulfilling an already-fulfilled order returns the recorded capture.
        if (order.Status == OrderStatus.Fulfilled)
        {
            return payment;
        }
        if (order.Status != OrderStatus.Authorized || payment is null || payment.AuthorizationId is null)
        {
            throw new OrderStateException($"Order {orderId} is {order.Status}; it must be paid before it can be fulfilled.");
        }

        await EnsureCapturableAuthorizationAsync(order, payment, ct);

        var capture = await _paymentGateway.CaptureAuthorizationAsync(payment.AuthorizationId,
            payment.Amount, payment.Currency, $"eshop-order{orderId}-capture", ct);

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();

        await _paymentRepository.UpdateAsync(payment, ct);
        await _orderRepository.UpdateAsync(order, ct);
        return payment;
    }

    public async Task<(Order Order, Payment? Payment)?> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null)
        {
            return null;
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);

        if (order.Status == OrderStatus.Cancelled)
        {
            return (order, payment); // idempotent
        }

        order.Cancel(); // throws when already fulfilled — use a refund instead

        if (payment is { Status: PaymentStatus.Authorized, AuthorizationId: not null })
        {
            var status = await _paymentGateway.VoidAuthorizationAsync(payment.AuthorizationId,
                $"eshop-order{orderId}-void", ct);
            payment.RecordVoid(status);
            await _paymentRepository.UpdateAsync(payment, ct);
        }

        await _orderRepository.UpdateAsync(order, ct);
        return (order, payment);
    }

    public async Task<PaymentRefund?> RefundOrderAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, string? noteToPayer, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }
        if (order.Status != OrderStatus.Fulfilled)
        {
            throw new OrderStateException($"Order {orderId} is {order.Status}; only fulfilled orders can be refunded.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
        if (payment is null || payment.CaptureId is null)
        {
            throw new OrderStateException($"Order {orderId} has no captured payment to refund.");
        }

        // Caller-supplied idempotency key: a repeat under the same key returns the original refund.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var refundAmount = amount ?? payment.RefundableAmount();
        if (refundAmount <= 0 || refundAmount > payment.RefundableAmount())
        {
            throw new OrderStateException(
                $"Refund amount {refundAmount:0.00} {payment.Currency} exceeds the refundable balance {payment.RefundableAmount():0.00} {payment.Currency} for order {orderId}.");
        }

        var refund = await _paymentGateway.RefundCaptureAsync(payment.CaptureId, refundAmount,
            payment.Currency, idempotencyKey, noteToPayer, ct);

        var recorded = payment.RegisterRefund(refund.RefundId, refund.Amount ?? refundAmount, refund.Status, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, ct);
        return recorded;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
    }

    public async Task<IReadOnlyList<Payment>> GetPaymentsForOrdersAsync(IReadOnlyCollection<int> orderIds,
        CancellationToken ct = default)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<Payment>();
        }
        return await _paymentRepository.ListAsync(new PaymentsByOrderIdsSpec(orderIds), ct);
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var existing = await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
        var payPalCustomerId = existing.FirstOrDefault(m => m.PayPalCustomerId is not null)?.PayPalCustomerId;

        // Deterministic per shopper+card so a double-click dedupes at PayPal instead of double-vaulting.
        // The hash is used only as a request id and is never stored.
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{buyerId}|{card.Number}|{card.Expiry}")));
        var requestId = $"eshop-vault-{fingerprint[..24]}";

        var vaulted = await _paymentGateway.VaultCardAsync(buyerId, payPalCustomerId, card, requestId, ct);

        var duplicate = existing.FirstOrDefault(m => m.VaultTokenId == vaulted.VaultTokenId);
        if (duplicate is not null)
        {
            return duplicate;
        }

        var method = new SavedPaymentMethod(buyerId, vaulted.PayPalCustomerId ?? payPalCustomerId,
            vaulted.VaultTokenId, vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        await _paymentMethodRepository.AddAsync(method, ct);
        return method;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListCardsAsync(string buyerId, CancellationToken ct = default)
    {
        return await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        var method = await _paymentMethodRepository.GetByIdAsync(paymentMethodId, ct);
        if (method is null || method.BuyerId != buyerId)
        {
            return false;
        }

        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(method.VaultTokenId, ct);
        }
        catch (PaymentGatewayException ex) when (ex.Kind == PaymentFailureKind.NotFound)
        {
            // Already gone at PayPal — removing the local record completes the delete.
            _logger.LogInformation($"Vault token for saved payment method {paymentMethodId} was already absent at PayPal.");
        }

        await _paymentMethodRepository.DeleteAsync(method, ct);
        return true;
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        if (to < from)
        {
            throw new OrderStateException("The 'to' of the reconciliation range must not be before its 'from'.");
        }

        var transactions = await _paymentGateway.SearchTransactionsAsync(from, to, ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsCreatedInRangeSpec(from, to), ct);

        var entries = new List<ReconciliationEntry>();
        var unmatched = new List<ReconciliationEntry>();
        var matchedPaymentIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            var match = payments.FirstOrDefault(p => Matches(p, txn));
            var entry = new ReconciliationEntry(txn, match?.OrderId, match?.Id);
            entries.Add(entry);
            if (match is null)
            {
                unmatched.Add(entry);
            }
            else
            {
                matchedPaymentIds.Add(match.Id);
            }
        }

        var ordersWithoutTransaction = payments
            .Where(p => !matchedPaymentIds.Contains(p.Id))
            .Select(p => p.OrderId)
            .ToList();

        return new ReconciliationReport(from, to, entries, unmatched, ordersWithoutTransaction);
    }

    private static bool Matches(Payment payment, GatewayTransaction txn)
    {
        if (txn.ReferenceId is not null)
        {
            if (txn.ReferenceIdType == "ODR" && txn.ReferenceId == payment.PayPalOrderId) return true;
            if (txn.ReferenceId == payment.AuthorizationId) return true;
            if (txn.ReferenceId == payment.CaptureId) return true;
            if (payment.Refunds.Any(r => r.PayPalRefundId == txn.ReferenceId)) return true;
        }
        if (txn.InvoiceId is not null && txn.InvoiceId == $"order-{payment.OrderId}") return true;
        if (txn.CustomField is not null && txn.CustomField == payment.OrderId.ToString()) return true;
        if (txn.TransactionId is not null &&
            (txn.TransactionId == payment.AuthorizationId || txn.TransactionId == payment.CaptureId ||
             payment.Refunds.Any(r => r.PayPalRefundId == txn.TransactionId))) return true;
        return false;
    }

    private async Task EnsureCapturableAuthorizationAsync(Order order, Payment payment, CancellationToken ct)
    {
        GatewayAuthorizationState state;
        try
        {
            state = await _paymentGateway.GetAuthorizationAsync(payment.AuthorizationId!, ct);
        }
        catch (PaymentGatewayException ex) when (ex.Kind is PaymentFailureKind.NotFound or PaymentFailureKind.Conflict)
        {
            await MarkUnrenewableAsync(order, payment, $"PayPal no longer knows authorization {payment.AuthorizationId} ({ex.Message}).", ct);
            throw new OrderStateException(UnrenewableMessage(order.Id, ex.Message));
        }

        payment.UpdateAuthorizationState(state.Status, state.ExpiresAt);

        var active = state.Status is "CREATED" or "PENDING";
        var expired = state.ExpiresAt.HasValue && state.ExpiresAt.Value <= DateTimeOffset.UtcNow;

        if (active && !expired)
        {
            await _paymentRepository.UpdateAsync(payment, ct);
            return;
        }

        if (state.Status is "VOIDED" or "DENIED")
        {
            await MarkUnrenewableAsync(order, payment, $"Authorization {state.AuthorizationId} is {state.Status} at PayPal.", ct);
            throw new OrderStateException(UnrenewableMessage(order.Id, $"authorization is {state.Status}"));
        }

        // Stale (past its honor period): renew instead of failing the fulfilment.
        try
        {
            var renewed = await _paymentGateway.ReauthorizeAsync(state.AuthorizationId, payment.Amount,
                payment.Currency, $"eshop-order{order.Id}-reauth-{payment.AuthorizationAttempts + 1}", ct);
            payment.UpdateAuthorizationState(renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
        }
        catch (PaymentGatewayException ex) when (ex.Kind is PaymentFailureKind.AuthorizationNotRenewable
            or PaymentFailureKind.Validation or PaymentFailureKind.Conflict or PaymentFailureKind.NotFound)
        {
            await MarkUnrenewableAsync(order, payment, ex.Message, ct);
            throw new OrderStateException(UnrenewableMessage(order.Id, ex.Message));
        }
    }

    private static string UnrenewableMessage(int orderId, string reason) =>
        $"The PayPal authorization for order {orderId} went stale and can no longer be renewed ({reason}). " +
        $"The order was returned to awaiting-payment: ask the shopper to pay again via POST /api/orders/{orderId}/pay, then fulfil.";

    private async Task MarkUnrenewableAsync(Order order, Payment payment, string reason, CancellationToken ct)
    {
        payment.MarkRequiresNewAuthorization(reason);
        order.ReturnToAwaitingPayment();
        await _paymentRepository.UpdateAsync(payment, ct);
        await _orderRepository.UpdateAsync(order, ct);
    }

    private async Task SavePaymentAsync(Payment payment, CancellationToken ct)
    {
        if (payment.Id == 0)
        {
            await _paymentRepository.AddAsync(payment, ct);
        }
        else
        {
            await _paymentRepository.UpdateAsync(payment, ct);
        }
    }
}
