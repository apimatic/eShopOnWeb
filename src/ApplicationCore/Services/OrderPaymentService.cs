using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPal;
    private readonly PayPalSettings _settings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPal,
        PayPalSettings settings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _settings = settings;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines,
        ShippingAddressInput? address, CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentException("Every order line must have a quantity of at least one.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var shipTo = ToAddress(address);
        var order = new Order(buyerId, shipTo, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        // Merchant reconciliation key: stable to the order yet unique across runs so PayPal's reporting
        // never collides with a prior run that reused the same in-memory order id.
        var customId = $"ESHOP-{order.Id}-{Guid.NewGuid():N}";
        var payment = new Payment(order.Id, buyerId, _settings.ResolveCurrency(), order.Total(), customId);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        return order.Id;
    }

    public async Task<PaymentView> AuthorizeAsync(string buyerId, int orderId, PayInstruction instruction,
        CancellationToken cancellationToken)
    {
        var payment = await GetOwnedPaymentAsync(orderId, buyerId, cancellationToken);

        // Idempotent in effect: a double-click never places a second hold.
        if (payment.Status == PaymentStatus.Authorized)
        {
            return PaymentView.From(payment);
        }
        if (payment.Status != PaymentStatus.AwaitingPayment)
        {
            throw new PaymentException($"Order {orderId} cannot be paid because its payment is already {payment.Status}.");
        }

        int? savedCardId = null;
        PayPalCardInstrument instrument;
        if (instruction.SavedPaymentMethodId is int savedId)
        {
            var saved = await _savedCardRepository.GetByIdAsync(savedId, cancellationToken);
            if (saved is null || saved.BuyerId != buyerId)
            {
                throw new PaymentNotFoundException($"Saved card {savedId} was not found for this shopper.");
            }
            instrument = PayPalCardInstrument.FromVault(saved.PayPalVaultId);
            savedCardId = saved.Id;
        }
        else if (instruction.Card is not null)
        {
            instrument = PayPalCardInstrument.FromRawCard(instruction.Card);
        }
        else
        {
            throw new PaymentException("Provide either card details or the id of a saved card to pay with.");
        }

        var auth = await _payPal.AuthorizeAsync(payment.Amount, instrument, payment.AuthorizeRequestId,
            payment.CustomId, cancellationToken);

        payment.MarkAuthorized(auth.PayPalOrderId, auth.AuthorizationId, auth.Status, auth.ExpiresAt, savedCardId);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return PaymentView.From(payment);
    }

    public async Task<PaymentView> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await GetPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Fulfilled)
        {
            return PaymentView.From(payment); // idempotent: already captured
        }
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentException($"Order {orderId} cannot be fulfilled because its payment is {payment.Status}.");
        }

        await EnsureAuthorizationIsFreshAsync(payment, cancellationToken);

        var capture = await _payPal.CaptureAsync(payment.AuthorizationId!, payment.Amount, payment.CustomId,
            payment.CaptureRequestId, cancellationToken);

        payment.MarkFulfilled(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return PaymentView.From(payment);
    }

    /// <summary>
    /// A hold that has gone stale before fulfilment is renewed rather than failing the fulfilment outright.
    /// A hold that can no longer be renewed is reported in terms an operator can act on.
    /// </summary>
    private async Task EnsureAuthorizationIsFreshAsync(Payment payment, CancellationToken cancellationToken)
    {
        var current = await _payPal.GetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
        payment.UpdateAuthorization(current.AuthorizationId, current.Status, current.ExpiresAt);

        var stale = !string.Equals(current.Status, "CREATED", StringComparison.OrdinalIgnoreCase)
                    || (current.ExpiresAt is DateTimeOffset exp && exp <= DateTimeOffset.UtcNow);

        if (!stale)
        {
            return;
        }

        // A terminal state that is not an expiry cannot be captured or renewed.
        if (string.Equals(current.Status, "VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(current.Status, "DENIED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(current.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"Order {payment.OrderId} cannot be fulfilled: its authorization is {current.Status}. " +
                "Collect a new payment from the shopper.");
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount,
                payment.AuthorizeRequestId + "-reauth", cancellationToken);
            payment.UpdateAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(
                $"The payment authorization for order {payment.OrderId} has expired and can no longer be renewed. " +
                $"A new payment must be collected from the shopper. (PayPal: {ex.PayPalErrorName}; debug id {ex.DebugId})", ex);
        }
    }

    public async Task<PaymentView> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await GetPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return PaymentView.From(payment); // idempotent
        }
        if (payment.Status is PaymentStatus.Fulfilled or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentException(
                $"Order {orderId} has already been fulfilled and cannot be cancelled. Issue a refund instead.");
        }

        // Release any held funds so no money ever moved.
        if (payment.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
        {
            await _payPal.VoidAsync(payment.AuthorizationId, payment.VoidRequestId, cancellationToken);
        }

        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return PaymentView.From(payment);
    }

    public async Task<RefundReceipt> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("A refund requires an idempotency key.");
        }

        var payment = await GetOwnedPaymentAsync(orderId, buyerId, cancellationToken);

        if (payment.Status is not (PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded)
            || payment.CaptureId is null)
        {
            throw new PaymentException(
                $"Order {orderId} cannot be refunded because its payment is {payment.Status}. Only fulfilled orders can be refunded.");
        }

        // Repeating a request under the same key must not refund twice.
        var existing = payment.Refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
        if (existing is not null)
        {
            return ToReceipt(payment, existing);
        }

        var remaining = payment.RefundableRemaining;
        if (remaining <= 0m)
        {
            throw new PaymentException($"Order {orderId} has already been fully refunded.");
        }

        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
        {
            throw new PaymentException("A refund amount must be greater than zero.");
        }
        // A partly-refunded order must never become refundable beyond what was captured.
        if (refundAmount > remaining)
        {
            throw new PaymentException(
                $"Refund of {refundAmount:0.00} exceeds the {remaining:0.00} still refundable on order {orderId}.");
        }

        var refundId = Guid.NewGuid();
        var result = await _payPal.RefundAsync(payment.CaptureId!, refundAmount, payment.CustomId,
            idempotencyKey, cancellationToken);

        var refund = payment.AddRefund(refundId, refundAmount, idempotencyKey, result.RefundId, result.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return ToReceipt(payment, refund);
    }

    private static RefundReceipt ToReceipt(Payment payment, PaymentRefund refund) => new(
        refund.Id, refund.Status, refund.Amount, refund.PayPalRefundId,
        payment.CapturedAmount ?? 0m, payment.TotalRefunded, payment.RefundableRemaining);

    public async Task<IReadOnlyList<PaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var payments = await _paymentRepository.ListAsync(new CustomerPaymentsSpecification(buyerId), cancellationToken);
        return payments.Select(PaymentView.From).ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new PaymentException("The reconciliation 'to' date must be on or after the 'from' date.");
        }

        var transactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);

        // eShop's side of the ledger: payments whose money actually moved (captured), within the window.
        var payments = await _paymentRepository.ListAsync(new PaymentsCreatedBetweenSpecification(from, to), cancellationToken);
        var eShopByCustomId = payments
            .Where(p => p.CaptureId is not null)
            .ToDictionary(p => p.CustomId, StringComparer.Ordinal);

        var entries = new List<ReconciliationEntry>();
        var matchedCustomIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var t in transactions)
        {
            Payment? match = null;
            if (!string.IsNullOrEmpty(t.CustomField))
            {
                eShopByCustomId.TryGetValue(t.CustomField!, out match);
            }

            if (match is not null)
            {
                matchedCustomIds.Add(match.CustomId);
                entries.Add(new ReconciliationEntry("MATCHED", match.OrderId, match.CustomId,
                    t.TransactionId, t.EventCode, t.Status, t.Amount, t.FeeAmount, t.CurrencyCode ?? match.CurrencyCode,
                    t.InitiationDate, match.Status.ToString(), match.CapturedAmount));
            }
            else
            {
                entries.Add(new ReconciliationEntry("PAYPAL_ONLY", null, t.CustomField,
                    t.TransactionId, t.EventCode, t.Status, t.Amount, t.FeeAmount, t.CurrencyCode,
                    t.InitiationDate, null, null));
            }
        }

        foreach (var p in eShopByCustomId.Values.Where(p => !matchedCustomIds.Contains(p.CustomId)))
        {
            // eShop captured this but PayPal's reporting has no matching row (yet, given the reporting lag,
            // or a genuine gap). The capture id shown is PayPal's own id for the capture.
            entries.Add(new ReconciliationEntry("ESHOP_ONLY", p.OrderId, p.CustomId,
                p.CaptureId, null, null, null, null, p.CurrencyCode, null,
                p.Status.ToString(), p.CapturedAmount));
        }

        var payPalOnly = entries.Count(e => e.Match == "PAYPAL_ONLY");
        var eShopOnly = entries.Count(e => e.Match == "ESHOP_ONLY");
        var matched = entries.Count(e => e.Match == "MATCHED");

        return new ReconciliationReport(from, to, transactions.Count, eShopByCustomId.Count,
            matched, payPalOnly, eShopOnly, entries);
    }

    private async Task<Payment> GetPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        if (payment is null)
        {
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }
        return payment;
    }

    private async Task<Payment> GetOwnedPaymentAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var payment = await GetPaymentAsync(orderId, cancellationToken);
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not leak the existence of another shopper's order.
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }
        return payment;
    }

    private static Address ToAddress(ShippingAddressInput? a)
    {
        if (a is null)
        {
            return new Address("N/A", "N/A", "N/A", "US", "00000");
        }
        return new Address(a.Street, a.City, a.State, a.Country, a.ZipCode);
    }
}
