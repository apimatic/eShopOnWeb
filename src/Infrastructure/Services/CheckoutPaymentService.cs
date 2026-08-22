using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class CheckoutPaymentService : ICheckoutPaymentService
{
    private static readonly Address DefaultShipTo = new("123 Main St.", "Kent", "OH", "USA", "44240");
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<ShopperPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalClient _payPalClient;
    private readonly OrderOperationGate _gate;
    private readonly PayPalOptions _options;

    public CheckoutPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<ShopperPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalClient payPalClient,
        OrderOperationGate gate,
        IOptions<PayPalOptions> options)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPalClient = payPalClient;
        _gate = gate;
        _options = options.Value;
    }

    public async Task<Order> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
        {
            throw new PaymentValidationException("An order must contain at least one catalog item.");
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentValidationException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            if (!byId.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new PaymentNotFoundException($"Catalog item {line.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress ?? DefaultShipTo, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> AuthorizePaymentAsync(
        int orderId,
        string buyerId,
        CardDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        ValidatePaySource(card, paymentMethodId);

        return await _gate.RunAsync(orderId, async () =>
        {
            var order = await GetOrderForBuyerAsync(orderId, buyerId, cancellationToken);

            if (order.Status == OrderStatus.Authorized ||
                order.Status == OrderStatus.Fulfilled ||
                order.Status == OrderStatus.Refunded ||
                order.Status == OrderStatus.PartiallyRefunded)
            {
                return order;
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new PaymentConflictException("This order has been cancelled and cannot be paid.");
            }

            string? vaultId = null;
            if (paymentMethodId.HasValue)
            {
                var method = await GetOwnedPaymentMethodAsync(buyerId, paymentMethodId.Value, cancellationToken);
                vaultId = method.PayPalPaymentTokenId;
            }
            else if (card != null)
            {
                card = NormalizeCard(card);
            }

            var currency = RequireCurrency();
            var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
            var requestId = order.NextPayRequestId();
            var customId = $"{order.Id}:{order.PaymentNonce:N}";
            var invoiceId = $"ESHOP-{order.PaymentNonce:N}-A{order.PaymentAttempt}";

            var created = await _payPalClient.CreateAuthorizedOrderAsync(
                requestId,
                amount,
                currency,
                customId,
                invoiceId,
                card,
                vaultId,
                cancellationToken);

            var authorized = created;
            if (string.IsNullOrWhiteSpace(created.AuthorizationId) &&
                string.Equals(created.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                authorized = await _payPalClient.AuthorizeOrderAsync(
                    created.Id,
                    requestId + "-auth",
                    cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(authorized.AuthorizationId))
            {
                throw new PayPalGatewayException(
                    $"PayPal did not create an authorization for order {order.Id}. PayPal order status: {authorized.Status}.");
            }

            var authorizedAmount = authorized.AuthorizedAmount ?? amount;
            if (authorizedAmount != amount)
            {
                throw new PayPalGatewayException(
                    $"PayPal authorized {PayPalConfiguration.FormatMoney(authorizedAmount)} {authorized.Currency} but the order total is {PayPalConfiguration.FormatMoney(amount)} {currency}.");
            }

            order.RecordAuthorization(
                authorized.Id,
                authorized.AuthorizationId,
                authorized.AuthorizationStatus ?? "CREATED",
                authorizedAmount,
                authorized.Currency ?? currency,
                authorized.AuthorizationExpiration);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
            {
                return order;
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new PaymentConflictException("A cancelled order cannot be fulfilled.");
            }

            if (order.Status != OrderStatus.Authorized || string.IsNullOrWhiteSpace(order.AuthorizationId))
            {
                throw new PaymentConflictException("This order has not been paid. Authorize payment before fulfilment.");
            }

            var currency = order.Currency ?? RequireCurrency();
            var amount = order.AuthorizedAmount ?? decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);

            var authorization = await EnsureFreshAuthorizationAsync(order, amount, currency, cancellationToken);

            var capture = await _payPalClient.CaptureAuthorizationAsync(
                authorization.Id,
                $"eshop-cap-{order.PaymentNonce:N}",
                amount,
                currency,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(capture.AuthorizationId) &&
                !string.Equals(capture.AuthorizationId, authorization.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new PayPalGatewayException(
                    "PayPal returned a capture that does not belong to this order's authorization. Retry fulfilment.");
            }

            var capturedAmount = capture.Amount ?? amount;
            if (capturedAmount != amount)
            {
                throw new PayPalGatewayException(
                    $"PayPal captured {PayPalConfiguration.FormatMoney(capturedAmount)} but the order total is {PayPalConfiguration.FormatMoney(amount)} {currency}.");
            }

            if (!IsSuccessfulCapture(capture.Status))
            {
                throw new PayPalGatewayException(
                    $"PayPal could not capture authorization {authorization.Id}. Capture status: {capture.Status}.");
            }

            order.RecordCapture(
                capture.Id,
                capture.Status,
                capturedAmount,
                capture.PayPalFee,
                capture.NetAmount,
                capture.Currency ?? currency);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });

    public Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return order;
            }

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
            {
                throw new PaymentConflictException("This order has already been fulfilled. Issue a refund instead of cancelling.");
            }

            if (!string.IsNullOrWhiteSpace(order.AuthorizationId) &&
                order.Status == OrderStatus.Authorized)
            {
                try
                {
                    await _payPalClient.VoidAuthorizationAsync(
                        order.AuthorizationId,
                        $"eshop-void-{order.PaymentNonce:N}",
                        cancellationToken);
                }
                catch (PayPalGatewayException ex) when (
                    string.Equals(ex.PayPalIssue, "AUTHORIZATION_ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ex.PayPalName, "RESOURCE_ALREADY_EXISTS", StringComparison.OrdinalIgnoreCase) ||
                    ex.StatusCode == 422 && (ex.PayPalIssue ?? string.Empty).Contains("VOID", StringComparison.OrdinalIgnoreCase))
                {
                    // Already released on PayPal; continue to mark cancelled locally.
                }
            }

            order.RecordCancellation();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });

    public Task<OrderRefund> RefundOrderAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentValidationException("Refunds require an idempotency key.");
        }

        return _gate.RunAsync(orderId, async () =>
        {
            var order = isAdministrator
                ? await GetOrderAsync(orderId, cancellationToken)
                : await GetOrderForBuyerAsync(orderId, buyerId, cancellationToken);

            var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing != null)
            {
                return existing;
            }

            if (string.IsNullOrWhiteSpace(order.CaptureId) ||
                order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
            {
                if (order.Status == OrderStatus.Refunded)
                {
                    throw new PaymentValidationException("This order has already been fully refunded.");
                }

                throw new PaymentConflictException("Refunds are only available after the order has been fulfilled.");
            }

            var remaining = order.RemainingRefundableAmount();
            var refundAmount = amount.HasValue
                ? decimal.Round(amount.Value, 2, MidpointRounding.AwayFromZero)
                : remaining;

            if (refundAmount <= 0m)
            {
                throw new PaymentValidationException("This order has already been fully refunded.");
            }

            if (refundAmount > remaining)
            {
                throw new PaymentValidationException(
                    $"Refund of {PayPalConfiguration.FormatMoney(refundAmount)} exceeds the remaining refundable amount of {PayPalConfiguration.FormatMoney(remaining)}.");
            }

            var currency = order.Currency ?? RequireCurrency();
            var paypalRefund = await _payPalClient.RefundCaptureAsync(
                order.CaptureId,
                BuildRefundRequestId(order.PaymentNonce, idempotencyKey),
                refundAmount,
                currency,
                cancellationToken);

            var refund = order.RecordRefund(
                paypalRefund.Id,
                idempotencyKey,
                paypalRefund.Amount ?? refundAmount,
                paypalRefund.Currency ?? currency,
                paypalRefund.Status);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return refund;
        });
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<ShopperPaymentMethod> SavePaymentMethodAsync(
        string buyerId,
        CardDetails card,
        CancellationToken cancellationToken = default)
    {
        card = NormalizeCard(card);
        var customerId = ToPayPalCustomerId(buyerId);
        var requestId = $"eshop-vault-{customerId}-{Guid.NewGuid():N}";

        var token = await _payPalClient.CreatePaymentTokenAsync(
            requestId,
            customerId,
            buyerId,
            card,
            cancellationToken);

        var last4 = token.LastDigits;
        if (string.IsNullOrWhiteSpace(last4) && card.Number.Length >= 4)
        {
            last4 = card.Number[^4..];
        }

        var saved = new ShopperPaymentMethod(
            buyerId,
            token.Id,
            token.CustomerId ?? customerId,
            token.Brand,
            last4,
            token.Expiry ?? card.Expiry,
            token.Name ?? card.Name);

        return await _paymentMethodRepository.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperPaymentMethod>> ListPaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _paymentMethodRepository.ListAsync(new ShopperPaymentMethodsSpecification(buyerId), cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var method = await GetOwnedPaymentMethodAsync(buyerId, paymentMethodId, cancellationToken);
        await _payPalClient.DeletePaymentTokenAsync(method.PayPalPaymentTokenId, cancellationToken);
        await _paymentMethodRepository.DeleteAsync(method, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentValidationException("'to' must be on or after 'from'.");
        }

        var paypalTransactions = await _payPalClient.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentsSpecification(), cancellationToken);

        var matches = new List<ReconciliationMatch>();
        var matchedTransactionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypalTransactions)
        {
            var order = FindMatchingOrder(orders, txn);
            if (order == null)
            {
                continue;
            }

            matchedOrderIds.Add(order.Id);
            var key = TransactionKey(txn);
            if (key != null)
            {
                matchedTransactionKeys.Add(key);
            }

            matches.Add(new ReconciliationMatch
            {
                OrderId = order.Id,
                PayPalTransactionId = txn.TransactionId,
                PayPalReferenceId = txn.PayPalReferenceId,
                CustomField = txn.CustomField,
                InvoiceId = txn.InvoiceId,
                TransactionEventCode = txn.TransactionEventCode,
                TransactionStatus = txn.TransactionStatus,
                Amount = txn.Amount,
                Currency = txn.Currency,
                MatchedOn = DescribeMatch(order, txn)
            });
        }

        var paypalOnly = paypalTransactions
            .Where(t =>
            {
                var key = TransactionKey(t);
                return key == null || !matchedTransactionKeys.Contains(key);
            })
            .Select(t => new PayPalOnlyTransaction
            {
                PayPalTransactionId = t.TransactionId,
                PayPalReferenceId = t.PayPalReferenceId,
                CustomField = t.CustomField,
                InvoiceId = t.InvoiceId,
                TransactionEventCode = t.TransactionEventCode,
                TransactionStatus = t.TransactionStatus,
                Amount = t.Amount,
                Currency = t.Currency,
                InitiationDate = t.InitiationDate
            })
            .ToList();

        var eshopOnly = orders
            .Where(o => !matchedOrderIds.Contains(o.Id) && PaymentTouchesRange(o, from, to))
            .Select(o => new EshopOnlyPayment
            {
                OrderId = o.Id,
                Status = o.Status.ToString(),
                PayPalOrderId = o.PayPalOrderId,
                AuthorizationId = o.AuthorizationId,
                CaptureId = o.CaptureId,
                RefundIds = o.Refunds.Select(r => r.PayPalRefundId).ToList()
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matches = matches,
            PayPalOnly = paypalOnly,
            EshopOnly = eshopOnly
        };
    }

    private async Task<PayPalAuthorizationResult> EnsureFreshAuthorizationAsync(
        Order order,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        var authorization = await _payPalClient.GetAuthorizationAsync(order.AuthorizationId!, cancellationToken);
        order.ReplaceAuthorization(authorization.Id, authorization.Status, authorization.ExpirationTime);

        var stale = IsStale(authorization);
        if (!stale)
        {
            return authorization;
        }

        try
        {
            var renewed = await _payPalClient.ReauthorizeAsync(
                authorization.Id,
                $"eshop-reauth-{order.PaymentNonce:N}",
                amount,
                currency,
                cancellationToken);

            order.ReplaceAuthorization(renewed.Id, renewed.Status, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed;
        }
        catch (PayPalGatewayException ex) when (IsUnrenewable(ex))
        {
            throw new AuthorizationUnrenewableException(
                "The payment hold on this order has expired and PayPal will not renew it. " +
                "Ask the shopper to place and pay a new order, or cancel this order. " +
                $"PayPal reported: {ex.PayPalIssue ?? ex.PayPalName ?? ex.Message}");
        }
    }

    private static bool IsStale(PayPalAuthorizationResult authorization)
    {
        if (string.Equals(authorization.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authorization.Status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (authorization.ExpirationTime.HasValue && authorization.ExpirationTime.Value <= DateTimeOffset.UtcNow)
        {
            return true;
        }

        if (authorization.CreateTime.HasValue &&
            authorization.CreateTime.Value.AddDays(3) <= DateTimeOffset.UtcNow)
        {
            return true;
        }

        return false;
    }

    private static bool IsUnrenewable(PayPalGatewayException ex)
    {
        var issue = ex.PayPalIssue ?? string.Empty;
        return ex.StatusCode == 422 ||
               issue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) ||
               issue.Contains("REAUTHORIZ", StringComparison.OrdinalIgnoreCase) ||
               issue.Contains("AUTHORIZATION", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSuccessfulCapture(string status) =>
        string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase);

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<Order> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<ShopperPaymentMethod> GetOwnedPaymentMethodAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken)
    {
        var method = await _paymentMethodRepository.FirstOrDefaultAsync(
            new ShopperPaymentMethodsSpecification(buyerId, paymentMethodId),
            cancellationToken);
        if (method == null)
        {
            throw new PaymentNotFoundException($"Payment method {paymentMethodId} was not found.");
        }

        return method;
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_options.Currency))
        {
            throw new PaymentValidationException("PayPal:Currency is not configured.");
        }

        return _options.Currency;
    }

    private static void ValidatePaySource(CardDetails? card, int? paymentMethodId)
    {
        var hasCard = card != null && !string.IsNullOrWhiteSpace(card.Number);
        var hasSaved = paymentMethodId.HasValue;
        if (hasCard == hasSaved)
        {
            throw new PaymentValidationException("Provide either card details or a saved paymentMethodId, not both.");
        }
    }

    private static CardDetails NormalizeCard(CardDetails card)
    {
        var number = new string(card.Number.Where(char.IsDigit).ToArray());
        if (number.Length is < 13 or > 19)
        {
            throw new PaymentValidationException("Card number must contain 13 to 19 digits.");
        }

        if (string.IsNullOrWhiteSpace(card.Expiry) || card.Expiry.Length != 7)
        {
            throw new PaymentValidationException("Card expiry must be in YYYY-MM format.");
        }

        if (string.IsNullOrWhiteSpace(card.SecurityCode) || card.SecurityCode.Length is < 3 or > 4)
        {
            throw new PaymentValidationException("Card security code must be 3 or 4 digits.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentValidationException("Cardholder name is required.");
        }

        var country = string.IsNullOrWhiteSpace(card.CountryCode) ? "US" : card.CountryCode.Trim().ToUpperInvariant();

        return new CardDetails
        {
            Number = number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name.Trim(),
            AddressLine1 = string.IsNullOrWhiteSpace(card.AddressLine1) ? "2211 N First St" : card.AddressLine1,
            AddressLine2 = card.AddressLine2,
            AdminArea2 = string.IsNullOrWhiteSpace(card.AdminArea2) ? "San Jose" : card.AdminArea2,
            AdminArea1 = string.IsNullOrWhiteSpace(card.AdminArea1) ? "CA" : card.AdminArea1,
            PostalCode = string.IsNullOrWhiteSpace(card.PostalCode) ? "95131" : card.PostalCode,
            CountryCode = country
        };
    }

    internal static string ToPayPalCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        return Convert.ToHexString(hash)[..22].ToLowerInvariant();
    }

    private static string BuildRefundRequestId(Guid paymentNonce, string idempotencyKey)
    {
        var value = $"eshop-rf-{paymentNonce:N}-{idempotencyKey}";
        return value.Length <= 108 ? value : value[..108];
    }

    private static Order? FindMatchingOrder(IReadOnlyList<Order> orders, PayPalReportedTransaction txn)
    {
        foreach (var order in orders)
        {
            if (IdsEqual(txn.TransactionId, order.CaptureId) ||
                IdsEqual(txn.TransactionId, order.AuthorizationId) ||
                IdsEqual(txn.PayPalReferenceId, order.CaptureId) ||
                IdsEqual(txn.PayPalReferenceId, order.AuthorizationId) ||
                IdsEqual(txn.PayPalReferenceId, order.PayPalOrderId) ||
                order.Refunds.Any(r => IdsEqual(txn.TransactionId, r.PayPalRefundId) || IdsEqual(txn.PayPalReferenceId, r.PayPalRefundId)))
            {
                return order;
            }

            if (!string.IsNullOrWhiteSpace(order.PaymentNonce.ToString("N")))
            {
                var nonce = order.PaymentNonce.ToString("N");
                if (!string.IsNullOrWhiteSpace(txn.CustomField) &&
                    txn.CustomField.Contains(nonce, StringComparison.OrdinalIgnoreCase))
                {
                    return order;
                }

                if (!string.IsNullOrWhiteSpace(txn.InvoiceId) &&
                    txn.InvoiceId.Contains(nonce, StringComparison.OrdinalIgnoreCase))
                {
                    return order;
                }
            }
        }

        return null;
    }

    private static string DescribeMatch(Order order, PayPalReportedTransaction txn)
    {
        if (IdsEqual(txn.TransactionId, order.CaptureId) || IdsEqual(txn.PayPalReferenceId, order.CaptureId))
        {
            return "captureId";
        }

        if (IdsEqual(txn.TransactionId, order.AuthorizationId) || IdsEqual(txn.PayPalReferenceId, order.AuthorizationId))
        {
            return "authorizationId";
        }

        if (order.Refunds.Any(r => IdsEqual(txn.TransactionId, r.PayPalRefundId) || IdsEqual(txn.PayPalReferenceId, r.PayPalRefundId)))
        {
            return "refundId";
        }

        if (!string.IsNullOrWhiteSpace(txn.CustomField))
        {
            return "customId";
        }

        return "invoiceId";
    }

    private static bool IdsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string? TransactionKey(PayPalReportedTransaction txn)
    {
        if (!string.IsNullOrWhiteSpace(txn.TransactionId))
        {
            return txn.TransactionId + "|" + (txn.TransactionEventCode ?? string.Empty);
        }

        return null;
    }

    private static bool PaymentTouchesRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        bool InRange(DateTimeOffset? value) => value.HasValue && value.Value >= from && value.Value <= to;
        return InRange(order.AuthorizedAt) ||
               InRange(order.FulfilledAt) ||
               InRange(order.CancelledAt) ||
               order.Refunds.Any(r => InRange(r.CreatedAt));
    }
}
