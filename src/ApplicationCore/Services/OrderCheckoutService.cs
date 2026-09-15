using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderCheckoutService : IOrderCheckoutService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<SavedPaymentMethod> _savedCards;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalOptions _options;
    private readonly IAppLogger<OrderCheckoutService> _logger;

    public OrderCheckoutService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<SavedPaymentMethod> savedCards,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        PayPalOptions options,
        IAppLogger<OrderCheckoutService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _savedCards = savedCards;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _options = options;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address? shipTo, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException(400, "An order must contain at least one catalog item.");
        }

        var quantities = new Dictionary<int, int>();
        foreach (var line in lines)
        {
            if (line.Quantity < 1)
            {
                throw new PaymentException(400, "Each item quantity must be at least 1.");
            }

            quantities[line.CatalogItemId] = quantities.TryGetValue(line.CatalogItemId, out var existing)
                ? existing + line.Quantity
                : line.Quantity;
        }

        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(quantities.Keys.ToArray()), cancellationToken);
        var byId = catalogItems.ToDictionary(i => i.Id);
        var missing = quantities.Keys.Where(id => !byId.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentException(400, $"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var items = quantities.Select(pair =>
        {
            var catalogItem = byId[pair.Key];
            var snapshot = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(snapshot, catalogItem.Price, pair.Value);
        }).ToList();

        var address = shipTo ?? new Address("123 Main St.", "Seattle", "WA", "United States", "98101");
        var currency = string.IsNullOrWhiteSpace(_options.Currency) ? "USD" : _options.Currency.Trim().ToUpperInvariant();
        var order = new Order(buyerId, address, items, currency);
        return await _orders.AddAsync(order, cancellationToken);
    }

    public async Task<Order> PayAsync(string buyerId, int orderId, CardPaymentInput? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        if (card is null && savedPaymentMethodId is null)
        {
            throw new PaymentException(400, "Provide card details or a saved paymentMethodId.");
        }

        if (card != null && savedPaymentMethodId is not null)
        {
            throw new PaymentException(400, "Provide either card details or a saved paymentMethodId, not both.");
        }

        using (await LockAsync($"order-{orderId}", cancellationToken))
        {
            var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

            if (order.PaymentStatus is OrderPaymentStatus.Authorized or OrderPaymentStatus.Fulfilled
                or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
            {
                return order;
            }

            if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            {
                throw new PaymentException(409, "This order was cancelled and cannot be paid.");
            }

            var amount = PayPalMoney.Format(order.Total(), order.Currency);
            if (PayPalMoney.Parse(amount) <= 0)
            {
                throw new PaymentException(400, "The order total must be greater than zero.");
            }

            order.EnsurePaymentAttemptKey();
            await _orders.UpdateAsync(order, cancellationToken);

            string? vaultId = null;
            PayPalCardSource? cardSource = null;
            if (savedPaymentMethodId is int methodId)
            {
                var saved = await _savedCards.GetByIdAsync(methodId, cancellationToken)
                    ?? throw new PaymentException(404, "Saved payment method not found.");
                if (!string.Equals(saved.BuyerId, buyerId, StringComparison.Ordinal))
                {
                    throw new PaymentException(404, "Saved payment method not found.");
                }

                vaultId = saved.PayPalPaymentTokenId;
            }
            else
            {
                cardSource = ToPayPalCard(card!);
            }

            try
            {
                var result = await _payPal.AuthorizePaymentAsync(new PayPalAuthorizeRequest
                {
                    Amount = amount,
                    Currency = order.Currency,
                    InvoiceId = order.PayPalInvoiceId,
                    CustomId = InvoiceId(order.Id),
                    CreateRequestId = $"eshop-create-{order.Id}-{order.PaymentAttemptKey}",
                    AuthorizeRequestId = $"eshop-authorize-{order.Id}-{order.PaymentAttemptKey}",
                    PayPalOrderId = order.PayPalOrderId,
                    Card = cardSource,
                    VaultId = vaultId
                }, cancellationToken);

                if (!PayPalMoney.AmountsEqual(PayPalMoney.Parse(result.Amount ?? amount), order.Total(), order.Currency))
                {
                    _logger.LogWarning("PayPal authorized {Authorized} {Currency} for order {OrderId} whose total is {Total}.",
                        result.Amount ?? amount, order.Currency, order.Id, amount);
                }

                order.RecordAuthorization(
                    result.PayPalOrderId,
                    result.AuthorizationId,
                    result.Status,
                    result.CreateTime,
                    result.ExpirationTime);
                await _orders.UpdateAsync(order, cancellationToken);
                return order;
            }
            catch (PayPalGatewayException ex)
            {
                throw MapGatewayException(ex, "authorize the payment");
            }
        }
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using (await LockAsync($"order-{orderId}", cancellationToken))
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);

            if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
            {
                return order;
            }

            if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            {
                throw new PaymentException(409, "A cancelled order cannot be fulfilled.");
            }

            if (order.PaymentStatus != OrderPaymentStatus.Authorized || string.IsNullOrEmpty(order.PayPalAuthorizationId))
            {
                throw new PaymentException(409, "The order has not been authorized. The shopper must pay before fulfilment.");
            }

            var amount = PayPalMoney.Format(order.Total(), order.Currency);
            var authorizationId = order.PayPalAuthorizationId;

            try
            {
                authorizationId = await EnsureFreshAuthorizationAsync(order, amount, cancellationToken);

                var capture = await CaptureWithRenewalAsync(order, authorizationId, amount, cancellationToken);
                order.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PaypalFee, capture.NetAmount);
                await _orders.UpdateAsync(order, cancellationToken);
                return order;
            }
            catch (PayPalGatewayException ex)
            {
                throw MapGatewayException(ex, "capture the payment");
            }
        }
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using (await LockAsync($"order-{orderId}", cancellationToken))
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);

            if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            {
                return order;
            }

            if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
            {
                throw new PaymentException(409, "A fulfilled order cannot be cancelled. Issue a refund instead.");
            }

            if (!string.IsNullOrEmpty(order.PayPalAuthorizationId) && order.PaymentStatus == OrderPaymentStatus.Authorized)
            {
                try
                {
                    await _payPal.VoidAuthorizationAsync(order.PayPalAuthorizationId, $"eshop-void-{order.Id}-{order.PaymentAttemptKey ?? "na"}", cancellationToken);
                    order.RecordCancellation("VOIDED");
                }
                catch (PayPalGatewayException ex) when (IsAlreadyVoided(ex))
                {
                    order.RecordCancellation("VOIDED");
                }
                catch (PayPalGatewayException ex)
                {
                    throw MapGatewayException(ex, "release the payment hold");
                }
            }
            else
            {
                order.RecordCancellation(null);
            }

            await _orders.UpdateAsync(order, cancellationToken);
            return order;
        }
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, string idempotencyKey, decimal? amount, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException(400, "A caller-supplied idempotencyKey is required for refunds.");
        }

        using (await LockAsync($"order-{orderId}", cancellationToken))
        {
            var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

            var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing != null)
            {
                return existing;
            }

            if (order.PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
            {
                throw new PaymentException(409, "Refunds can only be issued after the order has been fulfilled.");
            }

            if (string.IsNullOrEmpty(order.PayPalCaptureId))
            {
                throw new PaymentException(409, "This order has no captured PayPal payment to refund.");
            }

            var remaining = order.RemainingRefundableAmount;
            if (remaining <= 0)
            {
                throw new PaymentException(409, "This capture has already been refunded in full.");
            }

            decimal refundAmount;
            string? paypalAmount;
            if (amount is null)
            {
                refundAmount = remaining;
                paypalAmount = null;
            }
            else
            {
                if (amount.Value <= 0)
                {
                    throw new PaymentException(400, "Refund amount must be greater than zero.");
                }

                if (amount.Value > remaining)
                {
                    throw new PaymentException(409,
                        $"Cannot refund {PayPalMoney.Format(amount.Value, order.Currency)} {order.Currency}. Only {PayPalMoney.Format(remaining, order.Currency)} {order.Currency} remains refundable.");
                }

                refundAmount = amount.Value;
                paypalAmount = PayPalMoney.Format(refundAmount, order.Currency);
            }

            try
            {
                var result = await _payPal.RefundCaptureAsync(
                    order.PayPalCaptureId,
                    paypalAmount,
                    order.Currency,
                    $"eshop-refund-{order.Id}-{idempotencyKey}",
                    cancellationToken);

                var recordedAmount = result.Amount > 0 ? result.Amount : refundAmount;
                var refund = order.RecordRefund(result.RefundId, result.Status, recordedAmount, result.Currency, idempotencyKey);
                await _orders.UpdateAsync(order, cancellationToken);
                return refund;
            }
            catch (PayPalGatewayException ex)
            {
                throw MapGatewayException(ex, "refund the payment");
            }
        }
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentInput card, CancellationToken cancellationToken = default)
    {
        var existing = await _savedCards.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        var customerId = existing.FirstOrDefault(c => !string.IsNullOrEmpty(c.PayPalCustomerId))?.PayPalCustomerId;

        try
        {
            var vaulted = await _payPal.VaultCardAsync(new PayPalVaultCardRequest
            {
                Card = ToPayPalCard(card),
                RequestId = $"eshop-vault-{buyerId}-{Guid.NewGuid():N}",
                PayPalCustomerId = customerId
            }, cancellationToken);

            var saved = new SavedPaymentMethod(
                buyerId,
                vaulted.PaymentTokenId,
                vaulted.LastDigits,
                vaulted.Brand,
                vaulted.Expiry,
                vaulted.CardholderName,
                vaulted.CustomerId);

            return await _savedCards.AddAsync(saved, cancellationToken);
        }
        catch (PayPalGatewayException ex)
        {
            throw MapGatewayException(ex, "save the card");
        }
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _savedCards.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _savedCards.GetByIdAsync(paymentMethodId, cancellationToken);
        if (saved is null || !string.Equals(saved.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentException(404, "Saved payment method not found.");
        }

        try
        {
            await _payPal.DeleteVaultedCardAsync(saved.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PayPalGatewayException ex)
        {
            throw MapGatewayException(ex, "delete the saved card");
        }

        await _savedCards.DeleteAsync(saved, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException(400, "The reconciliation 'to' value must be on or after 'from'.");
        }

        IReadOnlyList<PayPalReportedTransaction> paypalTransactions;
        try
        {
            paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        }
        catch (PayPalGatewayException ex) when (IsReportingDataUnavailable(ex))
        {
            _logger.LogInformation("PayPal transaction reporting has no data for {From} to {To} yet (debug {DebugId}). Returning an empty PayPal side.",
                from, to, ex.DebugId ?? "none");
            paypalTransactions = Array.Empty<PayPalReportedTransaction>();
        }
        catch (PayPalGatewayException ex)
        {
            throw MapGatewayException(ex, "list PayPal transactions");
        }

        var orders = await _orders.ListAsync(new OrdersWithPaymentSpec(), cancellationToken);
        var ordersByInvoice = orders.ToDictionary(o => InvoiceId(o.Id), StringComparer.OrdinalIgnoreCase);
        var ordersById = orders.ToDictionary(o => o.Id.ToString(CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);

        var paypalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            AddId(paypalIds, order.PayPalOrderId);
            AddId(paypalIds, order.PayPalAuthorizationId);
            AddId(paypalIds, order.PayPalCaptureId);
            foreach (var refund in order.Refunds)
            {
                AddId(paypalIds, refund.PayPalRefundId);
            }
        }

        var matched = new List<ReconciliationMatch>();
        var paypalOnly = new List<PayPalReportedTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypalTransactions)
        {
            Order? matchedOrder = null;
            if (!string.IsNullOrEmpty(txn.InvoiceId) && ordersByInvoice.TryGetValue(txn.InvoiceId, out var byInvoice))
            {
                matchedOrder = byInvoice;
            }
            else if (!string.IsNullOrEmpty(txn.CustomField) && ordersByInvoice.TryGetValue(txn.CustomField, out var byCustom))
            {
                matchedOrder = byCustom;
            }
            else if (!string.IsNullOrEmpty(txn.InvoiceId) && txn.InvoiceId.StartsWith("ESHOP-", StringComparison.OrdinalIgnoreCase))
            {
                var token = txn.InvoiceId.Split('-');
                if (token.Length >= 2 && int.TryParse(token[1], out var parsedId) && orders.FirstOrDefault(o => o.Id == parsedId) is { } byParsed)
                {
                    matchedOrder = byParsed;
                }
            }
            else if (!string.IsNullOrEmpty(txn.CustomField) && ordersById.TryGetValue(txn.CustomField, out var byCustomId))
            {
                matchedOrder = byCustomId;
            }
            else if (!string.IsNullOrEmpty(txn.TransactionId) && paypalIds.Contains(txn.TransactionId))
            {
                matchedOrder = orders.FirstOrDefault(o => HasPayPalId(o, txn.TransactionId!));
            }
            else if (!string.IsNullOrEmpty(txn.PaypalReferenceId) && paypalIds.Contains(txn.PaypalReferenceId))
            {
                matchedOrder = orders.FirstOrDefault(o => HasPayPalId(o, txn.PaypalReferenceId!));
            }

            if (matchedOrder != null)
            {
                matched.Add(new ReconciliationMatch { OrderId = matchedOrder.Id, PayPalTransaction = txn });
                matchedOrderIds.Add(matchedOrder.Id);
            }
            else
            {
                paypalOnly.Add(txn);
            }
        }

        var eShopOnly = new List<EShopUnmatchedPayment>();
        foreach (var order in orders)
        {
            if (matchedOrderIds.Contains(order.Id)) continue;
            if (!HasPaymentActivityInRange(order, from, to)) continue;
            if (string.IsNullOrEmpty(order.PayPalOrderId) && string.IsNullOrEmpty(order.PayPalAuthorizationId) && string.IsNullOrEmpty(order.PayPalCaptureId))
            {
                continue;
            }

            eShopOnly.Add(new EShopUnmatchedPayment
            {
                OrderId = order.Id,
                PayPalOrderId = order.PayPalOrderId,
                AuthorizationId = order.PayPalAuthorizationId,
                CaptureId = order.PayPalCaptureId,
                RefundId = order.Refunds.LastOrDefault()?.PayPalRefundId,
                PaymentStatus = order.PaymentStatus.ToString(),
                Amount = order.CapturedAmount ?? order.Total(),
                Currency = order.Currency
            });
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            PayPalOnly = paypalOnly,
            EShopOnly = eShopOnly
        };
    }

    private async Task<string> EnsureFreshAuthorizationAsync(Order order, string amount, CancellationToken cancellationToken)
    {
        var authorizationId = order.PayPalAuthorizationId!;
        PayPalAuthorizationDetails details;
        try
        {
            details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PayPalGatewayException ex)
        {
            throw MapGatewayException(ex, "load the PayPal authorization");
        }

        order.SyncAuthorizationStatus(details.Status);
        var expired = IsExpired(details);
        var honorElapsed = HonorPeriodElapsed(details);

        if (!expired && !honorElapsed)
        {
            return authorizationId;
        }

        if (expired && !CanStillReauthorize(details))
        {
            throw new PaymentException(409,
                "The PayPal authorization for this order has expired and cannot be renewed. Ask the shopper to pay again; the original hold is no longer valid.");
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                authorizationId,
                amount,
                order.Currency,
                $"eshop-reauthorize-{order.Id}-{order.PaymentAttemptKey}",
                cancellationToken);

            order.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.CreateTime, renewed.ExpirationTime);
            await _orders.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (PayPalGatewayException ex) when (!expired && IsReauthorizeTooEarly(ex))
        {
            _logger.LogInformation("PayPal declined reauthorization for order {OrderId} as too early; capturing the original hold.", order.Id);
            return authorizationId;
        }
        catch (PayPalGatewayException ex) when (IsCannotReauthorize(ex))
        {
            throw new PaymentException(409,
                "The PayPal authorization for this order can no longer be renewed. Ask the shopper to pay again; PayPal has released or expired the hold.");
        }
        catch (PayPalGatewayException ex)
        {
            throw MapGatewayException(ex, "renew the payment hold");
        }
    }

    private async Task<PayPalCaptureResult> CaptureWithRenewalAsync(Order order, string authorizationId, string amount, CancellationToken cancellationToken)
    {
        try
        {
            return await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                order.Currency,
                $"{order.PayPalInvoiceId}-CAP",
                $"eshop-capture-{order.Id}-{order.PaymentAttemptKey}",
                cancellationToken);
        }
        catch (PayPalGatewayException ex) when (IsAuthorizationExpired(ex))
        {
            try
            {
                var renewed = await _payPal.ReauthorizeAsync(
                    authorizationId,
                    amount,
                    order.Currency,
                    $"eshop-reauthorize-{order.Id}-{order.PaymentAttemptKey}",
                    cancellationToken);

                order.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.CreateTime, renewed.ExpirationTime);
                await _orders.UpdateAsync(order, cancellationToken);

                return await _payPal.CaptureAuthorizationAsync(
                    renewed.AuthorizationId,
                    amount,
                    order.Currency,
                    $"{order.PayPalInvoiceId}-CAP",
                    $"eshop-capture-{order.Id}-{order.PaymentAttemptKey}",
                    cancellationToken);
            }
            catch (PayPalGatewayException renewEx) when (IsCannotReauthorize(renewEx) || IsAuthorizationExpired(renewEx))
            {
                throw new PaymentException(409,
                    "The PayPal authorization for this order has gone stale and cannot be renewed. Ask the shopper to pay again before fulfilment.");
            }
        }
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentException(404, "Order not found.");
        }

        return order;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentException(404, "Order not found.");
        }

        return order;
    }

    private static PayPalCardSource ToPayPalCard(CardPaymentInput card)
    {
        var number = NormalizeCardNumber(card.Number);
        if (number.Length is < 13 or > 19)
        {
            throw new PaymentException(400, "Card number is not valid.");
        }

        return new PayPalCardSource
        {
            Number = number,
            Expiry = NormalizeExpiry(card.Expiry),
            SecurityCode = string.IsNullOrWhiteSpace(card.SecurityCode) ? null : card.SecurityCode.Trim(),
            Name = string.IsNullOrWhiteSpace(card.Name) ? "Sandbox Shopper" : card.Name.Trim(),
            BillingAddress = card.BillingAddress is null
                ? new PayPalBillingAddress
                {
                    AddressLine1 = "2211 N First Street",
                    AdminArea1 = "CA",
                    AdminArea2 = "San Jose",
                    PostalCode = "95131",
                    CountryCode = "US"
                }
                : new PayPalBillingAddress
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = NormalizeCountryCode(card.BillingAddress.CountryCode)
                }
        };
    }

    private static string NormalizeCardNumber(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new PaymentException(400, "Card number is required.");
        }

        return Regex.Replace(number, @"\s+", string.Empty);
    }

    private static string NormalizeExpiry(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
        {
            throw new PaymentException(400, "Card expiry is required (YYYY-MM).");
        }

        var trimmed = expiry.Trim();
        if (Regex.IsMatch(trimmed, @"^\d{4}-\d{2}$"))
        {
            return trimmed;
        }

        var slash = Regex.Match(trimmed, @"^(\d{1,2})/(\d{2}|\d{4})$");
        if (slash.Success)
        {
            var month = int.Parse(slash.Groups[1].Value, CultureInfo.InvariantCulture);
            var yearPart = slash.Groups[2].Value;
            var year = yearPart.Length == 2 ? 2000 + int.Parse(yearPart, CultureInfo.InvariantCulture) : int.Parse(yearPart, CultureInfo.InvariantCulture);
            return $"{year:D4}-{month:D2}";
        }

        throw new PaymentException(400, "Card expiry must be YYYY-MM.");
    }

    private static string NormalizeCountryCode(string? country)
    {
        if (string.IsNullOrWhiteSpace(country)) return "US";
        if (country.Length == 2) return country.ToUpperInvariant();
        if (country.Equals("United States", StringComparison.OrdinalIgnoreCase)) return "US";
        return country.Length > 2 ? country[..2].ToUpperInvariant() : country.ToUpperInvariant();
    }

    private static string InvoiceId(int orderId) => $"ESHOP-{orderId}";

    private static bool IsExpired(PayPalAuthorizationDetails details)
    {
        if (string.Equals(details.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase)) return true;
        return details.ExpirationTime is { } expiry && DateTimeOffset.UtcNow >= expiry;
    }

    private static bool HonorPeriodElapsed(PayPalAuthorizationDetails details)
    {
        var created = details.CreateTime ?? DateTimeOffset.MinValue;
        return created != DateTimeOffset.MinValue && DateTimeOffset.UtcNow >= created.Add(HonorPeriod);
    }

    private static bool CanStillReauthorize(PayPalAuthorizationDetails details)
    {
        var created = details.CreateTime ?? DateTimeOffset.MinValue;
        if (created == DateTimeOffset.MinValue) return true;
        return DateTimeOffset.UtcNow < created.AddDays(29);
    }

    private static bool IsReportingDataUnavailable(PayPalGatewayException ex) =>
        MatchesIssue(ex, "DATA_NOT_AVAILABLE", "START_DATE_TOO_RECENT", "RESULTSET_TOO_LARGE")
        || ex.Message.Contains("start date is not available", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("data for the given start date", StringComparison.OrdinalIgnoreCase);

    private static bool IsAuthorizationExpired(PayPalGatewayException ex) =>
        MatchesIssue(ex, "AUTHORIZATION_EXPIRED", "AUTH_EXPIRED", "EXPIRED_AUTHORIZATION", "AUTHORIZATION_ALREADY_EXPIRED");

    private static bool IsCannotReauthorize(PayPalGatewayException ex) =>
        MatchesIssue(ex, "CANNOT_BE_REAUTHORIZED", "REAUTHORIZATION_NOT_ALLOWED", "AUTHORIZATION_VOIDED", "AUTHORIZATION_EXPIRED", "AUTH_EXPIRED");

    private static bool IsReauthorizeTooEarly(PayPalGatewayException ex) =>
        MatchesIssue(ex, "REAUTHORIZATION_TOO_SOON", "AUTHORIZATION_NOT_ELIGIBLE_FOR_REAUTHORIZATION", "NOT_AUTHORIZED");

    private static bool IsAlreadyVoided(PayPalGatewayException ex) =>
        MatchesIssue(ex, "AUTHORIZATION_VOIDED", "PREVIOUSLY_VOIDED")
        || ex.StatusCode == 422 && ex.Message.Contains("void", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesIssue(PayPalGatewayException ex, params string[] issues)
    {
        foreach (var issue in issues)
        {
            if (string.Equals(ex.Issue, issue, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static PaymentException MapGatewayException(PayPalGatewayException ex, string action)
    {
        if (ex.StatusCode == 401 || ex.StatusCode == 403)
        {
            return new PaymentException(502, $"PayPal refused to {action}: {ex.Message}");
        }

        var status = ex.StatusCode is >= 400 and < 500 ? 409 : 502;
        return new PaymentException(status, $"Unable to {action}: {ex.Message}");
    }

    private static bool HasPayPalId(Order order, string id)
    {
        return string.Equals(order.PayPalOrderId, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(order.PayPalAuthorizationId, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(order.PayPalCaptureId, id, StringComparison.OrdinalIgnoreCase)
            || order.Refunds.Any(r => string.Equals(r.PayPalRefundId, id, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddId(HashSet<string> set, string? id)
    {
        if (!string.IsNullOrWhiteSpace(id)) set.Add(id);
    }

    private static bool HasPaymentActivityInRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        return InRange(order.AuthorizedAt, from, to)
            || InRange(order.FulfilledAt, from, to)
            || InRange(order.CancelledAt, from, to)
            || order.Refunds.Any(r => InRange(r.CreatedAt, from, to))
            || InRange(order.OrderDate, from, to);
    }

    private static bool InRange(DateTimeOffset? value, DateTimeOffset from, DateTimeOffset to) =>
        value is { } v && v >= from && v <= to;

    private static async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken)
    {
        var semaphore = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public void Dispose() => _semaphore.Release();
    }
}
