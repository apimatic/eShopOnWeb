using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class CheckoutService : ICheckoutService
{
    private static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);
    private static readonly TimeSpan MaxReauthorizationAge = TimeSpan.FromDays(29);
    private static readonly Address DefaultShipTo = new("123 Main St", "Seattle", "WA", "US", "98101");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalog;
    private readonly IRepository<OrderPayment> _payments;
    private readonly IRepository<SavedPaymentMethod> _savedCards;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalSettings _payPalSettings;

    public CheckoutService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalog,
        IRepository<OrderPayment> payments,
        IRepository<SavedPaymentMethod> savedCards,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        IPayPalSettings payPalSettings)
    {
        _orders = orders;
        _catalog = catalog;
        _payments = payments;
        _savedCards = savedCards;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _payPalSettings = payPalSettings;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> lines,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("The caller is not authenticated.", 401);
        }

        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("At least one catalog item is required.", 400);
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException("Quantity must be greater than zero.", 400);
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalog.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.", 400);
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipTo, orderItems);
        await _orders.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<(Order Order, OrderPayment Payment)> PayAsync(
        string buyerId,
        int orderId,
        PayOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var existing = await _payments.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);

        if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded
            && existing is not null)
        {
            return (order, existing);
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be paid.", 409);
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentException($"Order {orderId} cannot be paid in its current state ({order.Status}).", 409);
        }

        var currency = RequireCurrency();
        var amount = PayPalMoney.Format(order.Total(), currency);
        var paymentSource = await BuildPaymentSourceAsync(buyerId, request, order, cancellationToken);
        var invoiceId = $"ESHOP-{order.PaymentIdempotencyKey}";
        var payPalCreateRequestId = $"eshop-create-{order.PaymentIdempotencyKey}";
        var payPalAuthorizeRequestId = $"eshop-pay-{order.PaymentIdempotencyKey}";

        var createResult = await _payPal.CreateOrderForAuthorizationAsync(
            new PayPalCreateOrderRequest
            {
                Currency = currency,
                Amount = amount,
                InvoiceId = invoiceId,
                CustomId = order.Id.ToString(CultureInfo.InvariantCulture),
                Description = $"eShopOnWeb order {order.Id}",
                PaymentSource = paymentSource
            },
            payPalCreateRequestId,
            cancellationToken);

        var authorization = createResult.Authorization;
        if (authorization is null)
        {
            var authorized = await _payPal.AuthorizeOrderAsync(
                createResult.Id,
                paymentSource,
                payPalAuthorizeRequestId,
                cancellationToken);
            authorization = authorized.Authorization;
        }

        if (authorization is null)
        {
            throw new PaymentException(
                $"PayPal did not return an authorization for order {order.Id} (PayPal status: {createResult.Status}).",
                502,
                "PAYPAL_NO_AUTHORIZATION");
        }

        if (!string.Equals(authorization.Amount, amount, StringComparison.Ordinal) &&
            PayPalMoney.Parse(authorization.Amount) != PayPalMoney.Parse(amount))
        {
            throw new PaymentException(
                $"PayPal authorized {authorization.Amount} {authorization.Currency} but the order total is {amount} {currency}.",
                502,
                "AMOUNT_MISMATCH");
        }

        var payment = existing ?? new OrderPayment(order.Id, currency, PayPalMoney.Parse(amount));
        payment.RecordAuthorization(
            createResult.Id,
            authorization.Id,
            authorization.Status,
            PayPalMoney.Parse(authorization.Amount) == 0m ? PayPalMoney.Parse(amount) : PayPalMoney.Parse(authorization.Amount),
            authorization.ExpirationTime,
            authorization.CreateTime ?? DateTimeOffset.UtcNow,
            invoiceId);

        order.MarkAuthorized();
        await _orders.UpdateAsync(order, cancellationToken);
        if (existing is null)
        {
            await _payments.AddAsync(payment, cancellationToken);
        }
        else
        {
            await _payments.UpdateAsync(payment, cancellationToken);
        }

        return (order, payment);
    }

    public async Task<(Order Order, OrderPayment Payment)> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        var payment = await RequirePaymentAsync(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            return (order, payment);
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be fulfilled.", 409);
        }

        if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
        {
            throw new PaymentException("An order must be authorized before it can be fulfilled.", 409);
        }

        var authorization = await _payPal.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        payment.RecordReauthorization(authorization.Id, authorization.Status, authorization.ExpirationTime);

        if (string.Equals(authorization.Status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"The PayPal authorization is {authorization.Status} and cannot be captured. Ask the shopper to pay again.",
                409,
                "AUTHORIZATION_UNUSABLE");
        }

        if (NeedsReauthorization(payment, authorization))
        {
            if (!CanReauthorize(payment, authorization))
            {
                throw new PaymentException(
                    "The PayPal authorization is past the 29-day window and cannot be renewed. Ask the shopper to pay again so a new hold can be placed, then fulfil the new authorization.",
                    409,
                    "AUTHORIZATION_EXPIRED");
            }

            try
            {
                var renewed = await _payPal.ReauthorizeAsync(
                    authorization.Id,
                    payment.Currency,
                    PayPalMoney.Format(payment.AuthorizedAmount, payment.Currency),
                    $"eshop-reauth-{order.PaymentIdempotencyKey}",
                    cancellationToken);
                payment.RecordReauthorization(renewed.Id, renewed.Status, renewed.ExpirationTime);
                authorization = renewed;
            }
            catch (PaymentException ex) when (ex.StatusCode is 409 or 400 or 404)
            {
                throw new PaymentException(
                    "PayPal could not renew the authorization. Ask the shopper to pay again so a new hold can be placed, then retry fulfilment.",
                    409,
                    ex.ErrorCode ?? "AUTHORIZATION_EXPIRED");
            }
        }

        var capture = await _payPal.CaptureAuthorizationAsync(
            authorization.Id,
            $"eshop-capture-{order.PaymentIdempotencyKey}",
            cancellationToken);

        if (string.Equals(capture.Status, "DECLINED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(capture.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException($"PayPal did not capture the payment (status: {capture.Status}).", 409, capture.Status);
        }

        payment.RecordCapture(
            capture.Id,
            capture.Status,
            PayPalMoney.Parse(capture.Amount),
            string.IsNullOrEmpty(capture.PayPalFee) ? null : PayPalMoney.Parse(capture.PayPalFee),
            string.IsNullOrEmpty(capture.NetAmount) ? null : PayPalMoney.Parse(capture.NetAmount));

        order.MarkFulfilled();
        await _orders.UpdateAsync(order, cancellationToken);
        await _payments.UpdateAsync(payment, cancellationToken);
        return (order, payment);
    }

    public async Task<(Order Order, OrderPayment? Payment)> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        var payment = await _payments.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return (order, payment);
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new PaymentException("A fulfilled order cannot be cancelled; issue a refund instead.", 409);
        }

        if (payment?.AuthorizationId is not null &&
            !string.Equals(payment.AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(payment.AuthorizationStatus, "CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _payPal.VoidAuthorizationAsync(payment.AuthorizationId, $"eshop-void-{order.PaymentIdempotencyKey}", cancellationToken);
                payment.RecordVoid("VOIDED");
                await _payments.UpdateAsync(payment, cancellationToken);
            }
            catch (PaymentException ex) when (ex.StatusCode == 404)
            {
                payment.RecordVoid("VOIDED");
                await _payments.UpdateAsync(payment, cancellationToken);
            }
        }

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);
        return (order, payment);
    }

    public async Task<(Order Order, OrderPayment Payment, PaymentRefund Refund)> RefundAsync(
        string buyerId,
        int orderId,
        RefundOrderRequest request,
        bool callerIsAdministrator,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentException("A refund idempotency key is required.", 400);
        }

        var order = callerIsAdministrator
            ? await GetOrderAsync(orderId, cancellationToken)
            : await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        var payment = await RequirePaymentAsync(orderId, cancellationToken);

        var existingRefund = payment.FindRefundByIdempotencyKey(request.IdempotencyKey);
        if (existingRefund is not null)
        {
            return (order, payment, existingRefund);
        }

        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
        {
            throw new PaymentException("Only a fulfilled order can be refunded.", 409);
        }

        if (string.IsNullOrEmpty(payment.CaptureId) || payment.CapturedAmount is null)
        {
            throw new PaymentException("This order has no captured PayPal payment to refund.", 409);
        }

        if (order.Status == OrderStatus.Refunded || payment.RemainingRefundableAmount <= 0m)
        {
            throw new PaymentException("This order has already been refunded in full.", 409);
        }

        decimal refundAmount;
        string? payPalAmount;
        if (request.Amount is null)
        {
            refundAmount = payment.RemainingRefundableAmount;
            payPalAmount = null;
        }
        else
        {
            refundAmount = decimal.Round(request.Amount.Value, 2, MidpointRounding.AwayFromZero);
            if (refundAmount <= 0m)
            {
                throw new PaymentException("Refund amount must be greater than zero.", 400);
            }

            if (refundAmount > payment.RemainingRefundableAmount)
            {
                throw new PaymentException(
                    $"Refund of {refundAmount} exceeds the remaining refundable amount {payment.RemainingRefundableAmount}.",
                    400);
            }

            payPalAmount = PayPalMoney.Format(refundAmount, payment.Currency);
        }

        var paypalRefund = await _payPal.RefundCaptureAsync(
            payment.CaptureId,
            payment.Currency,
            payPalAmount,
            $"{payment.CaptureId}:{request.IdempotencyKey}",
            cancellationToken);

        var recordedAmount = PayPalMoney.Parse(paypalRefund.Amount);
        if (recordedAmount <= 0m)
        {
            recordedAmount = refundAmount;
        }

        var refund = payment.AddRefund(
            paypalRefund.Id,
            paypalRefund.Status,
            recordedAmount,
            paypalRefund.Currency ?? payment.Currency,
            request.IdempotencyKey);

        var remaining = payment.RemainingRefundableAmount;
        order.MarkRefunded(partially: remaining > 0m);
        await _orders.UpdateAsync(order, cancellationToken);
        await _payments.UpdateAsync(payment, cancellationToken);
        return (order, payment, refund);
    }

    public async Task<IReadOnlyList<(Order Order, OrderPayment? Payment)>> ListMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _payments.ListAsync(
            new OrderPaymentsByOrderIdsSpec(orders.Select(o => o.Id)),
            cancellationToken);
        var byOrderId = payments.ToDictionary(p => p.OrderId);
        return orders
            .OrderByDescending(o => o.Id)
            .Select(o => (o, byOrderId.TryGetValue(o.Id, out var payment) ? payment : null))
            .ToList();
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentDetails card,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("The caller is not authenticated.", 401);
        }

        var cardPayload = BuildCardPayload(card, includeVerification: false);
        var vaulted = await _payPal.VaultCardAsync(
            new PayPalVaultCardRequest
            {
                MerchantCustomerId = ToMerchantCustomerId(buyerId),
                Card = cardPayload
            },
            Guid.NewGuid().ToString("N"),
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.Brand,
            vaulted.LastDigits ?? LastDigitsFrom(card.Number),
            vaulted.Expiry ?? NormalizeExpiry(card.Expiry),
            vaulted.CardholderName ?? card.Name,
            vaulted.CustomerId);

        await _savedCards.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListSavedCardsAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _savedCards.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _savedCards.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId),
            cancellationToken);
        if (saved is null)
        {
            throw new PaymentException("Saved card was not found.", 404);
        }

        try
        {
            await _payPal.DeletePaymentTokenAsync(saved.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            // Already removed at PayPal; still drop the local record.
        }

        await _savedCards.DeleteAsync(saved, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException("`to` must be greater than or equal to `from`.", 400);
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var localPayments = await _payments.ListAsync(new AllOrderPaymentsSpec(), cancellationToken);

        var matches = new List<ReconciliationMatch>();
        var matchedPayPal = new HashSet<PayPalReportedTransaction>();
        var matchedLocal = new HashSet<int>();

        foreach (var txn in paypalTransactions)
        {
            var local = localPayments.FirstOrDefault(p => Matches(p, txn));
            if (local is null)
            {
                continue;
            }

            matches.Add(new ReconciliationMatch
            {
                PayPalTransaction = txn,
                EshopPayment = local,
                OrderId = local.OrderId
            });
            matchedPayPal.Add(txn);
            matchedLocal.Add(local.Id);
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matches = matches,
            PayPalOnly = paypalTransactions.Where(t => !matchedPayPal.Contains(t)).ToList(),
            EshopOnly = localPayments.Where(p => !matchedLocal.Contains(p.Id) && PaymentInRange(p, from, to)).ToList()
        };
    }

    private async Task<object> BuildPaymentSourceAsync(
        string buyerId,
        PayOrderRequest request,
        Order order,
        CancellationToken cancellationToken)
    {
        var hasCard = request.Card is not null && !string.IsNullOrWhiteSpace(request.Card.Number);
        var hasSaved = request.PaymentMethodId is > 0;
        if (hasCard == hasSaved)
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId, not both.", 400);
        }

        if (hasSaved)
        {
            var saved = await _savedCards.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(request.PaymentMethodId!.Value, buyerId),
                cancellationToken);
            if (saved is null)
            {
                throw new PaymentException("Saved card was not found.", 404);
            }

            return new
            {
                card = new
                {
                    vault_id = saved.PayPalPaymentTokenId,
                    stored_credential = new
                    {
                        payment_initiator = "CUSTOMER",
                        payment_type = "UNSCHEDULED",
                        usage = "SUBSEQUENT"
                    }
                }
            };
        }

        var billing = request.Card!.BillingAddress;
        var ship = order.ShipToAddress;
        var card = BuildCardPayload(new CardPaymentDetails
        {
            Name = request.Card.Name,
            Number = request.Card.Number,
            Expiry = request.Card.Expiry,
            SecurityCode = request.Card.SecurityCode,
            BillingAddress = new CardBillingAddress
            {
                AddressLine1 = billing?.AddressLine1 ?? ship.Street,
                AddressLine2 = billing?.AddressLine2,
                AdminArea2 = billing?.AdminArea2 ?? ship.City,
                AdminArea1 = billing?.AdminArea1 ?? ship.State,
                PostalCode = billing?.PostalCode ?? ship.ZipCode,
                CountryCode = billing?.CountryCode ?? NormalizeCountry(ship.Country)
            }
        }, includeVerification: true);

        return new { card };
    }

    private static object BuildCardPayload(CardPaymentDetails card, bool includeVerification)
    {
        var number = NormalizePan(card.Number);
        if (number.Length is < 13 or > 19)
        {
            throw new PaymentException("A valid card number is required.", 400);
        }

        var expiry = NormalizeExpiry(card.Expiry);
        if (expiry is null)
        {
            throw new PaymentException("Card expiry must be in YYYY-MM format.", 400);
        }

        var cvc = card.SecurityCode?.Trim() ?? string.Empty;
        if (!Regex.IsMatch(cvc, "^[0-9]{3,4}$"))
        {
            throw new PaymentException("A valid card security code is required.", 400);
        }

        var country = string.IsNullOrWhiteSpace(card.BillingAddress?.CountryCode)
            ? "US"
            : card.BillingAddress!.CountryCode!.Trim().ToUpperInvariant();
        if (country.Length != 2)
        {
            throw new PaymentException("Billing address countryCode must be a 2-letter ISO country code.", 400);
        }

        var billing = new Dictionary<string, object?>
        {
            ["country_code"] = country
        };
        AddIfPresent(billing, "address_line_1", card.BillingAddress?.AddressLine1);
        AddIfPresent(billing, "address_line_2", card.BillingAddress?.AddressLine2);
        AddIfPresent(billing, "admin_area_2", card.BillingAddress?.AdminArea2);
        AddIfPresent(billing, "admin_area_1", card.BillingAddress?.AdminArea1);
        AddIfPresent(billing, "postal_code", card.BillingAddress?.PostalCode);

        var payload = new Dictionary<string, object?>
        {
            ["name"] = string.IsNullOrWhiteSpace(card.Name) ? "Test Cardholder" : card.Name.Trim(),
            ["number"] = number,
            ["expiry"] = expiry,
            ["security_code"] = cvc,
            ["billing_address"] = billing
        };

        if (includeVerification)
        {
            payload["attributes"] = new Dictionary<string, object?>
            {
                ["verification"] = new Dictionary<string, object?>
                {
                    ["method"] = "SCA_WHEN_REQUIRED"
                }
            };
        }

        return payload;
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentException("Order was not found.", 404);
        }

        return order;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentException("Order was not found.", 404);
        }

        return order;
    }

    private async Task<OrderPayment> RequirePaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _payments.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null)
        {
            throw new PaymentException("This order has no PayPal payment yet.", 409);
        }

        return payment;
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_payPalSettings.Currency))
        {
            throw new PaymentException("PayPal:Currency is not configured.", 500, "PAYPAL_NOT_CONFIGURED");
        }

        return _payPalSettings.Currency.Trim().ToUpperInvariant();
    }

    private static bool NeedsReauthorization(OrderPayment payment, PayPalAuthorizationDetails authorization)
    {
        var now = DateTimeOffset.UtcNow;
        if (authorization.ExpirationTime is { } expiration && expiration <= now.AddMinutes(5))
        {
            return true;
        }

        var created = authorization.CreateTime ?? payment.OriginalAuthorizedAt;
        return created is { } createdAt && now - createdAt >= HonorPeriod;
    }

    private static bool CanReauthorize(OrderPayment payment, PayPalAuthorizationDetails authorization)
    {
        var origin = payment.OriginalAuthorizedAt ?? authorization.CreateTime;
        if (origin is null)
        {
            return true;
        }

        return DateTimeOffset.UtcNow - origin.Value < MaxReauthorizationAge;
    }

    private static bool Matches(OrderPayment payment, PayPalReportedTransaction txn)
    {
        var candidates = new[]
        {
            txn.TransactionId,
            txn.PayPalReferenceId,
            txn.InvoiceId,
            txn.CustomField
        }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

        var local = new List<string>();
        if (!string.IsNullOrEmpty(payment.PayPalOrderId)) local.Add(payment.PayPalOrderId);
        if (!string.IsNullOrEmpty(payment.InvoiceId)) local.Add(payment.InvoiceId);
        if (!string.IsNullOrEmpty(payment.AuthorizationId)) local.Add(payment.AuthorizationId);
        if (!string.IsNullOrEmpty(payment.CaptureId)) local.Add(payment.CaptureId);
        local.Add(payment.OrderId.ToString(CultureInfo.InvariantCulture));
        local.AddRange(payment.Refunds.Select(r => r.PayPalRefundId));

        return candidates.Any(c => local.Any(l => string.Equals(c, l, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool PaymentInRange(OrderPayment payment, DateTimeOffset from, DateTimeOffset to)
    {
        var stamp = payment.OriginalAuthorizedAt;
        if (stamp is null)
        {
            return true;
        }

        return stamp >= from && stamp <= to;
    }

    private static void AddIfPresent(Dictionary<string, object?> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value.Trim();
        }
    }

    private static string ToMerchantCustomerId(string buyerId)
    {
        var sanitized = new string(buyerId.Where(c => char.IsLetterOrDigit(c) || "-_.^*$@#".Contains(c)).ToArray());
        if (string.IsNullOrEmpty(sanitized))
        {
            sanitized = "buyer";
        }

        return sanitized.Length <= 64 ? sanitized : sanitized[..64];
    }

    private static string NormalizePan(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return string.Empty;
        }

        return Regex.Replace(number, @"\D", string.Empty);
    }

    private static string? LastDigitsFrom(string? number)
    {
        var pan = NormalizePan(number);
        return pan.Length >= 4 ? pan[^4..] : pan;
    }

    private static string? NormalizeExpiry(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
        {
            return null;
        }

        expiry = expiry.Trim();
        if (Regex.IsMatch(expiry, @"^\d{4}-\d{2}$"))
        {
            return expiry;
        }

        var slash = Regex.Match(expiry, @"^(\d{1,2})/(\d{2}|\d{4})$");
        if (slash.Success)
        {
            var month = int.Parse(slash.Groups[1].Value, CultureInfo.InvariantCulture);
            var yearPart = slash.Groups[2].Value;
            var year = yearPart.Length == 2 ? 2000 + int.Parse(yearPart, CultureInfo.InvariantCulture) : int.Parse(yearPart, CultureInfo.InvariantCulture);
            return $"{year:D4}-{month:D2}";
        }

        return null;
    }

    private static string NormalizeCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return "US";
        }

        country = country.Trim();
        if (country.Length == 2)
        {
            return country.ToUpperInvariant();
        }

        return country.Equals("United States", StringComparison.OrdinalIgnoreCase) ? "US" : country[..Math.Min(2, country.Length)].ToUpperInvariant();
    }
}

internal static class PayPalMoney
{
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "HUF", "TWD"
    };

    public static string Format(decimal amount, string currency)
    {
        var decimals = ZeroDecimalCurrencies.Contains(currency) ? 0 : 2;
        var rounded = decimal.Round(amount, decimals, MidpointRounding.AwayFromZero);
        return rounded.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    public static decimal Parse(string? value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : 0m;
    }
}
