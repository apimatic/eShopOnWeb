using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<PaymentService> _logger;
    private readonly string _currency;

    // Serializes money operations per order so a double-click can never authorize/capture twice.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _orderLocks = new();

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<SavedCard> savedCardRepository,
        IPayPalGateway gateway,
        IUriComposer uriComposer,
        IOptions<PayPalOptions> options,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
        _currency = options.Value.Currency;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items,
        ShippingAddressInput? shipTo, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
            throw new PaymentStateException("An order must contain at least one item.");
        if (items.Any(i => i.Quantity <= 0))
            throw new PaymentStateException("Every item quantity must be greater than zero.");

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw new PaymentStateException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = new Address(
            shipTo?.Street ?? "N/A", shipTo?.City ?? "N/A", shipTo?.State ?? "N/A",
            shipTo?.Country ?? "N/A", shipTo?.ZipCode ?? "00000");

        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        var amount = RoundMoney(order.Total());
        var payment = new Payment(order.Id, buyerId, amount, _currency);
        payment.AssignInvoiceId($"ESHOP-{order.Id}-{Guid.NewGuid():N}");
        await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation($"Order {order.Id} placed by {buyerId}: total {amount:0.00} {_currency}, awaiting payment.");
        return order.Id;
    }

    public async Task<PaymentView> AuthorizeAsync(int orderId, string buyerId, PayInput pay, CancellationToken ct)
    {
        ValidatePayInput(pay);
        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var payment = await LoadOwnedPaymentAsync(orderId, buyerId, ct);

            // Idempotent in effect: a repeat once the hold exists returns the existing state.
            if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
                or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return ToView(payment);
            }
            if (payment.Status == PaymentStatus.Cancelled)
                throw new PaymentStateException($"Order {orderId} was cancelled and can no longer be paid.");

            var (card, vaultId) = await ResolveInstrumentAsync(buyerId, pay, ct);

            var command = new PayPalAuthorizeCommand(
                Amount: payment.Amount,
                InvoiceId: payment.InvoiceId!,
                CustomId: orderId.ToString(CultureInfo.InvariantCulture),
                Description: $"eShopOnWeb order {orderId}",
                IdempotencyKey: $"{payment.InvoiceId}-authorize",
                Card: card,
                VaultId: vaultId);

            var result = await _gateway.AuthorizeAsync(command, ct);
            payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status,
                result.ExpiresAt, result.InstrumentDescription);
            await _paymentRepository.UpdateAsync(payment, ct);

            _logger.LogInformation(
                $"Order {orderId} authorized: PayPal order {result.PayPalOrderId}, authorization {result.AuthorizationId}, hold {payment.Amount:0.00} {_currency}.");
            return ToView(payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaymentView> FulfilAsync(int orderId, CancellationToken ct)
    {
        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var payment = await LoadPaymentAsync(orderId, ct);

            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
                or PaymentStatus.Refunded)
            {
                return ToView(payment); // already fulfilled — idempotent
            }
            if (payment.Status != PaymentStatus.Authorized)
                throw new PaymentStateException(
                    $"Order {orderId} cannot be fulfilled from state '{payment.Status}'. It must be authorized first.");

            var authId = payment.AuthorizationId!;
            var amount = payment.Amount;
            var renewed = false;

            // Renew proactively if the hold has already lapsed.
            if (payment.AuthorizationExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow)
            {
                authId = await RenewAuthorizationAsync(payment, authId, amount, ct);
                renewed = true;
            }

            PayPalCaptureResult capture;
            try
            {
                capture = await _gateway.CaptureAsync(authId, amount, $"{payment.InvoiceId}-capture", ct);
            }
            catch (PaymentGatewayException ex) when (ex.IsAuthorizationExpired && !renewed)
            {
                _logger.LogWarning($"Order {orderId}: authorization {authId} stale at capture; renewing.");
                authId = await RenewAuthorizationAsync(payment, authId, amount, ct);
                capture = await _gateway.CaptureAsync(authId, amount, $"{payment.InvoiceId}-capture-2", ct);
            }

            payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount,
                capture.PayPalFee, capture.NetAmount);
            await _paymentRepository.UpdateAsync(payment, ct);

            _logger.LogInformation(
                $"Order {orderId} fulfilled: capture {capture.CaptureId}, taken {capture.GrossAmount:0.00} {capture.Currency}, fee {capture.PayPalFee:0.00}, net {capture.NetAmount:0.00}.");
            return ToView(payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaymentView> CancelAsync(int orderId, CancellationToken ct)
    {
        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var payment = await LoadPaymentAsync(orderId, ct);

            if (payment.Status == PaymentStatus.Cancelled)
                return ToView(payment); // idempotent
            if (payment.Status != PaymentStatus.Authorized)
                throw new PaymentStateException(
                    $"Order {orderId} cannot be cancelled from state '{payment.Status}'. " +
                    "Only an authorized-but-not-captured order can be cancelled; a captured order must be refunded.");

            await _gateway.VoidAsync(payment.AuthorizationId!, $"{payment.InvoiceId}-void", ct);
            payment.MarkCancelled();
            await _paymentRepository.UpdateAsync(payment, ct);

            _logger.LogInformation($"Order {orderId} cancelled: authorization {payment.AuthorizationId} voided, funds released.");
            return ToView(payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundView> RefundAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var payment = await LoadOwnedPaymentAsync(orderId, buyerId, ct);

            if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
                throw new PaymentStateException(
                    $"Order {orderId} has no captured payment to refund (status '{payment.Status}').");

            // Idempotency: a repeat under the same key resolves to the same refund, no second refund.
            if (payment.TryGetExistingRefund(idempotencyKey, out var existing) && existing is not null)
            {
                _logger.LogInformation($"Order {orderId}: refund idempotency key reused; returning refund {existing.PayPalRefundId}.");
                return ToRefundView(existing, payment);
            }

            var remaining = payment.RefundableRemaining;
            var refundAmount = RoundMoney(amount ?? remaining);
            if (refundAmount <= 0m)
                throw new PaymentStateException("Refund amount must be greater than zero.");
            if (refundAmount > remaining + 0.001m)
                throw new PaymentStateException(
                    $"Refund of {refundAmount:0.00} {_currency} exceeds the refundable remaining " +
                    $"{remaining:0.00} {_currency} for order {orderId}.");

            var result = await _gateway.RefundAsync(payment.CaptureId!, refundAmount, idempotencyKey, ct);
            var refund = new PaymentRefund(idempotencyKey, result.RefundId, refundAmount, _currency, result.Status);
            payment.AddRefund(refund);
            await _paymentRepository.UpdateAsync(payment, ct);

            _logger.LogInformation(
                $"Order {orderId} refunded {refundAmount:0.00} {_currency}: refund {result.RefundId}. New status '{payment.Status}'.");
            return ToRefundView(refund, payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<PaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), ct);
        return payments.OrderByDescending(p => p.OrderId).Select(ToView).ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        if (to < from)
            throw new PaymentStateException("Reconciliation 'to' must not be earlier than 'from'.");

        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);
        var captured = await _paymentRepository.ListAsync(new CapturedPaymentsSpecification(), ct);

        var byInvoice = captured.Where(p => !string.IsNullOrEmpty(p.InvoiceId))
            .GroupBy(p => p.InvoiceId!).ToDictionary(g => g.Key, g => g.First());
        var byCapture = captured.Where(p => !string.IsNullOrEmpty(p.CaptureId))
            .GroupBy(p => p.CaptureId!).ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        var payPalOnly = new List<ReconciliationTransaction>();
        var matchedOrders = new HashSet<int>();

        foreach (var t in transactions)
        {
            Payment? p = null;
            if (!string.IsNullOrEmpty(t.InvoiceId) && byInvoice.TryGetValue(t.InvoiceId!, out var byInv))
                p = byInv;
            else if (!string.IsNullOrEmpty(t.TransactionId) && byCapture.TryGetValue(t.TransactionId!, out var byCap))
                p = byCap;

            var txn = ToReconTransaction(t);
            if (p is not null)
            {
                var agree = t.Amount.HasValue && p.CapturedAmount.HasValue
                    && Math.Abs(t.Amount.Value - p.CapturedAmount.Value) < 0.01m;
                matched.Add(new ReconciliationMatch(p.OrderId, txn, p.CapturedAmount, agree));
                matchedOrders.Add(p.OrderId);
            }
            else
            {
                payPalOnly.Add(txn);
            }
        }

        var eShopOnly = captured
            .Where(p => !matchedOrders.Contains(p.OrderId))
            .Select(p => new ReconciliationOrder(p.OrderId, p.InvoiceId, p.CaptureId, p.CapturedAmount,
                p.Currency, p.Status.ToString()))
            .ToList();

        return new ReconciliationReport(from, to, _currency, matched, payPalOnly, eShopOnly,
            transactions.Count, captured.Count);
    }

    // ---- helpers ----

    private async Task<string> RenewAuthorizationAsync(Payment payment, string authId, decimal amount,
        CancellationToken ct)
    {
        try
        {
            var newAuth = await _gateway.ReauthorizeAsync(authId, amount,
                $"{payment.InvoiceId}-reauth", ct);
            payment.RenewAuthorization(newAuth.AuthorizationId, newAuth.Status, newAuth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation($"Order {payment.OrderId}: authorization renewed to {newAuth.AuthorizationId}.");
            return newAuth.AuthorizationId;
        }
        catch (PaymentGatewayException ex)
        {
            throw new PaymentStateException(
                $"Order {payment.OrderId}: the authorization has expired and could not be renewed ({ex.Message}). " +
                $"Ask the shopper to pay again via POST /api/orders/{payment.OrderId}/pay before fulfilling.");
        }
    }

    private static void ValidatePayInput(PayInput pay)
    {
        var hasCard = pay.Card is not null;
        var hasSaved = pay.SavedCardId is not null;
        if (hasCard == hasSaved)
            throw new PaymentStateException("Provide exactly one of 'card' or 'savedCardId'.");
        if (hasCard)
        {
            var c = pay.Card!;
            if (string.IsNullOrWhiteSpace(c.Number) || string.IsNullOrWhiteSpace(c.Expiry)
                || string.IsNullOrWhiteSpace(c.SecurityCode))
                throw new PaymentStateException("Card number, expiry and security code are required.");
        }
    }

    private async Task<(PayPalCardDetails? card, string? vaultId)> ResolveInstrumentAsync(
        string buyerId, PayInput pay, CancellationToken ct)
    {
        if (pay.SavedCardId is { } savedId)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedCardByIdAndBuyerSpecification(savedId, buyerId), ct);
            if (saved is null)
                throw new PaymentNotFoundException($"Saved card {savedId} was not found for this shopper.");
            return (null, saved.VaultId);
        }

        var c = pay.Card!;
        var card = new PayPalCardDetails(c.Number, c.Expiry, c.SecurityCode, c.Name,
            c.CountryCode, c.AddressLine1, c.AddressLine2, c.AdminArea1, c.AdminArea2, c.PostalCode);
        return (card, null);
    }

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        return payment;
    }

    private async Task<Payment> LoadOwnedPaymentAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        return payment;
    }

    private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private PaymentView ToView(Payment p) => new(
        OrderId: p.OrderId,
        Status: p.Status.ToString(),
        Amount: p.Amount,
        Currency: p.Currency,
        InvoiceId: p.InvoiceId,
        InstrumentDescription: p.InstrumentDescription,
        PayPalOrderId: p.PayPalOrderId,
        AuthorizationId: p.AuthorizationId,
        AuthorizationStatus: p.AuthorizationStatus,
        AuthorizationExpiresAt: p.AuthorizationExpiresAt,
        CaptureId: p.CaptureId,
        CaptureStatus: p.CaptureStatus,
        CapturedAmount: p.CapturedAmount,
        PayPalFee: p.PayPalFee,
        NetAmount: p.NetAmount,
        RefundedAmount: p.RefundedAmount,
        RefundableRemaining: p.RefundableRemaining,
        Refunds: p.Refunds
            .Select(r => new RefundLine(r.PayPalRefundId, r.Amount, r.Status, r.CreatedAt))
            .ToList());

    private RefundView ToRefundView(PaymentRefund refund, Payment payment) => new(
        RefundId: refund.PayPalRefundId,
        OrderId: payment.OrderId,
        Amount: refund.Amount,
        Status: refund.Status,
        RefundedTotal: payment.RefundedAmount,
        RefundableRemaining: payment.RefundableRemaining);

    private static ReconciliationTransaction ToReconTransaction(PayPalTransaction t) => new(
        t.TransactionId, t.InvoiceId, t.Amount, t.Currency, t.Status, t.Date, t.Fee);
}
