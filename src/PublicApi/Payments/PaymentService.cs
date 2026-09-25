using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<Payment> _payments;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<SavedPaymentMethod> _savedCards;
    private readonly IPayPalGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalOptions _options;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orders,
        IRepository<Payment> payments,
        IRepository<CatalogItem> catalogItems,
        IRepository<SavedPaymentMethod> savedCards,
        IPayPalGateway gateway,
        IUriComposer uriComposer,
        IOptions<PayPalOptions> options,
        ILogger<PaymentService> logger)
    {
        _orders = orders;
        _payments = payments;
        _catalogItems = catalogItems;
        _savedCards = savedCards;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items,
        ShipToAddressInput? shipToAddress, CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0)
        {
            throw new PaymentValidationException("At least one order item is required.");
        }

        var orderItems = new List<OrderItem>(items.Count);
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentValidationException($"Quantity for catalog item {line.CatalogItemId} must be a positive number.");
            }
            var catalogItem = await _catalogItems.GetByIdAsync(line.CatalogItemId, cancellationToken);
            if (catalogItem is null)
            {
                throw new PaymentValidationException($"Catalog item {line.CatalogItemId} does not exist.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            // Price comes from the catalog, never from the caller.
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, BuildShipToAddress(shipToAddress), orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        var total = order.Total();
        var invoiceId = BuildInvoiceId(order.Id);
        var payment = new Payment(order.Id, buyerId, _options.Currency, total, invoiceId);
        await _payments.AddAsync(payment, cancellationToken);

        _logger.LogInformation("Placed order {OrderId} for {BuyerId}: total {Total} {Currency}.",
            order.Id, buyerId, total, _options.Currency);
        return order.Id;
    }

    public async Task<PaymentView> PayAsync(string buyerId, int orderId, CardInput? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken)
    {
        var payment = await LoadOwnedPayment(orderId, buyerId, cancellationToken);

        // Idempotent in effect: a double-click never authorizes twice.
        if (payment.Status == PaymentStatus.Authorized)
        {
            return ToView(payment);
        }
        if (payment.Status is PaymentStatus.Fulfilled or PaymentStatus.Cancelled
            or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentConflictException(
                $"Order {orderId} cannot be authorized from status {payment.Status}.");
        }

        if (card is null && savedPaymentMethodId is null)
        {
            throw new PaymentValidationException("Provide either card details or a saved paymentMethodId.");
        }
        if (card is not null && savedPaymentMethodId is not null)
        {
            throw new PaymentValidationException("Provide either card details or a saved paymentMethodId, not both.");
        }

        string? vaultId = null;
        string? descriptor = null;
        CardDetails? oneOffCard = null;
        if (savedPaymentMethodId is { } paymentMethodId)
        {
            var saved = await _savedCards.GetByIdAsync(paymentMethodId, cancellationToken);
            if (saved is null || !string.Equals(saved.BuyerId, buyerId, StringComparison.Ordinal))
            {
                throw new PaymentNotFoundException($"Saved card {paymentMethodId} was not found.");
            }
            vaultId = saved.PayPalVaultId;
            descriptor = saved.Descriptor;
        }
        else
        {
            oneOffCard = ToCardDetails(card!);
        }

        var instruction = new AuthorizeInstruction(orderId, payment.Amount, payment.InvoiceId, oneOffCard, vaultId, descriptor);
        try
        {
            var result = await _gateway.AuthorizeAsync(instruction, cancellationToken);
            payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status,
                result.ExpiresAt, result.InstrumentDescriptor ?? descriptor);
            await _payments.UpdateAsync(payment, cancellationToken);
            return ToView(payment);
        }
        catch (PayPalIntegrationException ex)
        {
            // A definite decline (provider answered with a client error) fails the payment;
            // an unknown/transport outcome leaves it awaiting payment so it can be retried.
            if (ex is not PayerActionRequiredException && !ex.OutcomeUnknown &&
                ex.StatusCode is { } status && (int)status >= 400 && (int)status < 500)
            {
                payment.MarkFailed(ex.Message);
                await _payments.UpdateAsync(payment, cancellationToken);
            }
            throw;
        }
    }

    public async Task<PaymentView> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await LoadPayment(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Fulfilled)
        {
            return ToView(payment); // idempotent
        }
        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentConflictException($"Order {orderId} cannot be fulfilled from status {payment.Status}.");
        }
        if (string.IsNullOrEmpty(payment.AuthorizationId) || string.IsNullOrEmpty(payment.PayPalOrderId))
        {
            throw new PaymentConflictException($"Order {orderId} has no authorization to capture.");
        }

        var instruction = new CaptureInstruction(orderId, payment.InvoiceId, payment.PayPalOrderId!, payment.AuthorizationId!,
            payment.Amount, payment.AuthorizationExpiresAt);
        var result = await _gateway.CaptureAsync(instruction, cancellationToken);

        if (result.RenewedAuthorizationId is { } renewed)
        {
            payment.RenewAuthorization(renewed, "CAPTURED", result.RenewedExpiresAt);
        }
        payment.MarkFulfilled(result.CaptureId, result.Status, result.Gross, result.PayPalFee, result.NetAmount);
        await _payments.UpdateAsync(payment, cancellationToken);
        return ToView(payment);
    }

    public async Task<PaymentView> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await LoadPayment(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return ToView(payment); // idempotent
        }
        if (payment.Status is PaymentStatus.Fulfilled or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentConflictException($"Order {orderId} has been fulfilled; use a refund instead of cancel.");
        }

        if (payment.Status == PaymentStatus.Authorized && !string.IsNullOrEmpty(payment.AuthorizationId))
        {
            var status = await _gateway.VoidAsync(orderId, payment.InvoiceId, payment.AuthorizationId!, cancellationToken);
            payment.MarkCancelled(status);
        }
        else
        {
            // AwaitingPayment / Failed: nothing is held at PayPal, so just close the order out.
            payment.MarkCancelled("VOIDED");
        }

        await _payments.UpdateAsync(payment, cancellationToken);
        return ToView(payment);
    }

    public async Task<RefundView> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentValidationException("An idempotency key is required for refunds.");
        }

        var payment = await LoadOwnedPayment(orderId, buyerId, cancellationToken);

        if (payment.Status is not (PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentConflictException($"Order {orderId} cannot be refunded from status {payment.Status}.");
        }
        if (string.IsNullOrEmpty(payment.CaptureId))
        {
            throw new PaymentConflictException($"Order {orderId} has no capture to refund.");
        }

        // Idempotent: repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            return new RefundView(existing.PayPalRefundId ?? string.Empty, existing.Status, existing.Amount,
                payment.Status.ToString(), payment.RefundedAmount);
        }

        if (amount is { } requested)
        {
            if (requested <= 0m)
            {
                throw new PaymentValidationException("Refund amount must be a positive number.");
            }
            if (!payment.CanRefund(requested))
            {
                throw new PaymentConflictException(
                    $"A refund of {requested} exceeds the refundable remaining ({payment.RefundableRemaining}).");
            }
        }
        else if (payment.RefundableRemaining <= 0m)
        {
            throw new PaymentConflictException($"Order {orderId} has nothing left to refund.");
        }

        // A full refund sends an empty body only when nothing has been refunded yet; otherwise refund the remainder.
        var effectiveAmount = amount ?? (payment.RefundedAmount == 0m ? (decimal?)null : payment.RefundableRemaining);

        var instruction = new RefundInstruction(orderId, payment.CaptureId!, effectiveAmount, idempotencyKey, payment.InvoiceId);
        var result = await _gateway.RefundAsync(instruction, cancellationToken);

        payment.RecordRefund(new PaymentRefund(idempotencyKey, result.Amount, result.RefundId, result.Status));
        await _payments.UpdateAsync(payment, cancellationToken);

        return new RefundView(result.RefundId, result.Status, result.Amount, payment.Status.ToString(), payment.RefundedAmount);
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var payments = await _payments.ListAsync(new PaymentsByBuyerSpecification(buyerId), cancellationToken);
        var views = new List<OrderPaymentView>(payments.Count);

        foreach (var payment in payments)
        {
            var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(payment.OrderId), cancellationToken);
            var items = new List<OrderLineView>();
            var total = payment.Amount;
            var orderDate = payment.CreatedDate;
            if (order is not null)
            {
                total = order.Total();
                orderDate = order.OrderDate;
                foreach (var orderItem in order.OrderItems)
                {
                    items.Add(new OrderLineView(orderItem.ItemOrdered.CatalogItemId,
                        orderItem.ItemOrdered.ProductName, orderItem.UnitPrice, orderItem.Units));
                }
            }
            views.Add(new OrderPaymentView(payment.OrderId, orderDate, total, items, ToView(payment)));
        }

        return views.OrderByDescending(v => v.OrderDate).ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new PaymentValidationException("'to' must be on or after 'from'.");
        }

        var search = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var payments = await _payments.ListAsync(new ReconcilablePaymentsSpecification(), cancellationToken);

        var byInvoice = payments
            .Where(p => !string.IsNullOrEmpty(p.InvoiceId))
            .GroupBy(p => p.InvoiceId)
            .ToDictionary(g => g.Key, g => g.First());
        var byCapture = payments
            .Where(p => !string.IsNullOrEmpty(p.CaptureId))
            .GroupBy(p => p.CaptureId!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        var onlyInPayPal = new List<ReconciliationPayPalOnly>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in search.Transactions)
        {
            Payment? payment = null;
            if (!string.IsNullOrEmpty(txn.InvoiceId) && byInvoice.TryGetValue(txn.InvoiceId, out var byInv))
            {
                payment = byInv;
            }
            if (payment is null && !string.IsNullOrEmpty(txn.TransactionId) && byCapture.TryGetValue(txn.TransactionId!, out var byCap))
            {
                payment = byCap;
            }

            if (payment is not null)
            {
                matchedOrderIds.Add(payment.OrderId);
                matched.Add(new ReconciliationMatch(payment.OrderId, payment.Status.ToString(), payment.CaptureId,
                    payment.InvoiceId, payment.Amount, txn.TransactionId, txn.Amount, txn.Status));
            }
            else
            {
                onlyInPayPal.Add(new ReconciliationPayPalOnly(txn.TransactionId, txn.InvoiceId, txn.Amount,
                    txn.CurrencyCode, txn.Status, txn.InitiatedDate));
            }
        }

        var onlyInEShop = payments
            .Where(p => !matchedOrderIds.Contains(p.OrderId))
            .Select(p => new ReconciliationEShopOnly(p.OrderId, p.Status.ToString(), p.CaptureId, p.InvoiceId, p.Amount))
            .ToList();

        return new ReconciliationReport(from, to, search.CoveredAllPages, search.PagesRead, search.TotalPages,
            search.Transactions.Count, matched, onlyInPayPal, onlyInEShop);
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardInput card, CancellationToken cancellationToken)
    {
        var customerId = CustomerIdFactory.ForBuyer(buyerId);
        var result = await _gateway.VaultCardAsync(new VaultCardInstruction(ToCardDetails(card), customerId), cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, result.VaultId, result.Brand, result.LastDigits,
            result.Expiry, result.CardholderName ?? card.CardholderName);
        await _savedCards.AddAsync(saved, cancellationToken);

        _logger.LogInformation("Saved a card for {BuyerId}: paymentMethodId={PaymentMethodId} ({Descriptor}).",
            buyerId, saved.Id, saved.Descriptor);
        return ToSavedCardView(saved);
    }

    public async Task<IReadOnlyList<SavedCardView>> GetMyCardsAsync(string buyerId, CancellationToken cancellationToken)
    {
        var cards = await _savedCards.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        return cards.Select(ToSavedCardView).ToList();
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var saved = await _savedCards.GetByIdAsync(paymentMethodId, cancellationToken);
        if (saved is null || !string.Equals(saved.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return false; // not the caller's card (or not found) — do not leak existence
        }

        await _gateway.DeleteVaultedCardAsync(saved.PayPalVaultId, cancellationToken);
        await _savedCards.DeleteAsync(saved, cancellationToken);
        _logger.LogInformation("Deleted saved card {PaymentMethodId} for {BuyerId}.", paymentMethodId, buyerId);
        return true;
    }

    // ---- helpers ----

    private async Task<Payment> LoadPayment(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _payments.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        return payment ?? throw new PaymentNotFoundException($"Order {orderId} was not found.");
    }

    private async Task<Payment> LoadOwnedPayment(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var payment = await LoadPayment(orderId, cancellationToken);
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not reveal that the order exists for another shopper.
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }
        return payment;
    }

    private static Address BuildShipToAddress(ShipToAddressInput? input) => new(
        string.IsNullOrWhiteSpace(input?.Street) ? "Digital delivery" : input!.Street!,
        string.IsNullOrWhiteSpace(input?.City) ? "N/A" : input!.City!,
        // The EF in-memory provider keys owned types on all properties, so State must be non-null.
        string.IsNullOrWhiteSpace(input?.State) ? "N/A" : input!.State!,
        string.IsNullOrWhiteSpace(input?.Country) ? "US" : input!.Country!,
        string.IsNullOrWhiteSpace(input?.ZipCode) ? "00000" : input!.ZipCode!);

    private static string BuildInvoiceId(int orderId)
    {
        var invoiceId = $"ESHOP-{orderId}-{Guid.NewGuid():N}";
        return invoiceId.Length > 127 ? invoiceId[..127] : invoiceId;
    }

    private static CardDetails ToCardDetails(CardInput card) => new(
        card.Number, card.Expiry, card.SecurityCode, card.CardholderName,
        card.BillingAddress is null
            ? null
            : new BillingAddress(card.BillingAddress.CountryCode, card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2, card.BillingAddress.AdminArea1,
                card.BillingAddress.AdminArea2, card.BillingAddress.PostalCode));

    private static PaymentView ToView(Payment p) => new(
        p.OrderId, p.Status.ToString(), p.Amount, p.CurrencyCode, p.PayPalOrderId, p.AuthorizationId,
        p.AuthorizationStatus, p.AuthorizationExpiresAt, p.CaptureId, p.CaptureStatus, p.CapturedGross,
        p.PayPalFee, p.NetAmount, p.RefundedAmount, p.InstrumentDescriptor, p.FailureReason);

    private static SavedCardView ToSavedCardView(SavedPaymentMethod s) => new(
        s.Id, s.Brand, s.LastDigits, s.Expiry, s.CardholderName, s.Descriptor, s.CreatedDate);
}
