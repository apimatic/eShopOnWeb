using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // PayPal replays the stored response for a repeated PayPal-Request-Id for several
    // hours. Order ids restart from 1 on each run (in-memory store), so keys include a
    // per-run component: stable within the run (double-click safe), unique across runs.
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..8];

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly string _currency;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        IOptions<PaymentSettings> paymentSettings)
    {
        _orderRepository = orderRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _currency = paymentSettings.Value.Currency;
    }

    public async Task<Order> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotent in effect: paying an already-authorized order returns its current state.
        if (order.Status == OrderStatus.PaymentAuthorized)
        {
            return order;
        }

        if ((card is null) == (savedPaymentMethodId is null))
        {
            throw new InvalidPaymentStateException("Provide either card details or a saved paymentMethodId, not both.");
        }

        string? vaultTokenId = null;
        if (savedPaymentMethodId is not null)
        {
            var savedCard = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpecification(savedPaymentMethodId.Value), cancellationToken);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                throw new PaymentResourceNotFoundException($"No saved payment method {savedPaymentMethodId} found for this shopper.");
            }
            vaultTokenId = savedCard.VaultTokenId;
        }

        var attempt = order.BeginPaymentAttempt();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        var currency = RequireCurrency();
        var idempotencyKey = $"eshop-order-{order.Id}-pay-{attempt}-{RunId}";
        // Invoice ids must be unique per merchant account and per transaction; order ids
        // repeat across runs (in-memory store), so include attempt and a random component.
        var invoiceId = $"ESHOP-{order.Id}-{attempt}-{Guid.NewGuid():N}";

        var gatewayOrder = await _paymentGateway.CreateOrderAsync(
            order.Total(), currency,
            customId: $"eshop-order-{order.Id}",
            invoiceId: invoiceId,
            card, vaultTokenId, idempotencyKey, cancellationToken);

        if (gatewayOrder.Status == "PAYER_ACTION_REQUIRED")
        {
            throw new PayerActionRequiredException(
                "PayPal requires the shopper to complete an authentication challenge in a browser for this card; this integration does not support approval round-trips.");
        }

        GatewayAuthorization authorization;
        string? cardBrand;
        string? cardLastDigits;

        if (gatewayOrder.Authorization is not null)
        {
            // The card was authorized as part of order creation.
            authorization = gatewayOrder.Authorization;
            cardBrand = gatewayOrder.CardBrand;
            cardLastDigits = gatewayOrder.CardLastDigits;
        }
        else
        {
            var authorizeResult = await _paymentGateway.AuthorizeOrderAsync(gatewayOrder.Id, $"{idempotencyKey}-authorize", cancellationToken);

            if (authorizeResult.OrderStatus == "PAYER_ACTION_REQUIRED")
            {
                throw new PayerActionRequiredException(
                    "PayPal requires the shopper to complete an authentication challenge in a browser for this card; this integration does not support approval round-trips.");
            }
            if (authorizeResult.Authorization is null)
            {
                throw new PaymentGatewayException(200, "UNEXPECTED_RESPONSE",
                    $"PayPal order {gatewayOrder.Id} authorized without returning an authorization resource.", null);
            }

            authorization = authorizeResult.Authorization;
            cardBrand = authorizeResult.CardBrand;
            cardLastDigits = authorizeResult.CardLastDigits;
        }

        if (authorization.Status == "DENIED")
        {
            order.MarkPaymentFailed("DENIED");
            await _orderRepository.UpdateAsync(order, cancellationToken);
            throw new PaymentDeclinedException($"PayPal declined the payment for order {order.Id}.");
        }

        order.MarkAuthorized(
            gatewayOrder.Id,
            authorization.Id,
            authorization.Status,
            authorization.Amount,
            authorization.ExpirationTime,
            cardBrand,
            cardLastDigits);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        // Idempotent in effect: fulfilling an already-fulfilled order returns its current state.
        if (order.Status == OrderStatus.Fulfilled)
        {
            return order;
        }
        if (order.Status != OrderStatus.PaymentAuthorized || order.PayPalAuthorizationId is null)
        {
            throw new InvalidPaymentStateException($"Order {order.Id} cannot be fulfilled while in state {order.Status}.");
        }

        var currency = RequireCurrency();
        var authorizationId = order.PayPalAuthorizationId;
        var authorization = await _paymentGateway.GetAuthorizationAsync(authorizationId, cancellationToken);

        var stale = authorization.ExpirationTime is not null && authorization.ExpirationTime <= DateTimeOffset.UtcNow;
        var capturable = (authorization.Status == "CREATED" || authorization.Status == "PENDING") && !stale;

        if (!capturable)
        {
            // The hold went stale before fulfilment: renew it rather than failing outright.
            GatewayAuthorization renewed;
            try
            {
                renewed = await _paymentGateway.ReauthorizeAsync(
                    authorizationId, order.Total(), currency,
                    $"eshop-order-{order.Id}-reauthorize-{order.PaymentAttempts}-{RunId}", cancellationToken);
            }
            catch (PaymentGatewayException ex)
            {
                throw new AuthorizationNotRenewableException(
                    $"The PayPal authorization {authorizationId} for order {order.Id} expired before fulfilment and can no longer be renewed " +
                    $"(PayPal: {ex.ErrorName}). Ask the shopper to pay again, or cancel the order.");
            }

            if (renewed.Status == "DENIED")
            {
                throw new AuthorizationNotRenewableException(
                    $"PayPal denied renewal of the expired authorization {authorizationId} for order {order.Id}. " +
                    "Ask the shopper to pay again, or cancel the order.");
            }

            order.MarkAuthorizationRenewed(renewed.Id, renewed.Status, renewed.Amount, renewed.ExpirationTime);
            authorizationId = renewed.Id;
        }

        // No invoice id on the capture: PayPal then reports the authorizing transaction's
        // invoice id, keeping the order's paper trail on a single invoice.
        var capture = await _paymentGateway.CaptureAuthorizationAsync(
            authorizationId, null, $"eshop-order-{order.Id}-capture-{RunId}", cancellationToken);

        if (capture.Status is "DECLINED" or "FAILED")
        {
            throw new PaymentDeclinedException(
                $"PayPal could not capture the authorized funds for order {order.Id} (capture status {capture.Status}).");
        }

        order.MarkCaptured(capture.Id, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        // Idempotent in effect: cancelling an already-cancelled order returns its current state.
        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }
        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new InvalidPaymentStateException(
                $"Order {order.Id} is already fulfilled and its payment was captured; issue a refund instead of cancelling.");
        }

        if (order.Status == OrderStatus.PaymentAuthorized && order.PayPalAuthorizationId is not null)
        {
            // Release the shopper's held funds; no money ever moves.
            await _paymentGateway.VoidAuthorizationAsync(
                order.PayPalAuthorizationId, $"eshop-order-{order.Id}-void-{RunId}", cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<RefundResult> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 108)
        {
            throw new ArgumentException("A non-empty idempotency key of at most 108 characters is required.", nameof(idempotencyKey));
        }

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotent by caller-supplied key: a repeated request returns the original refund.
        var existing = order.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return new RefundResult
            {
                Refund = existing,
                TotalRefunded = order.TotalRefunded(),
                RefundableAmount = order.RefundableAmount()
            };
        }

        if (order.Status != OrderStatus.Fulfilled || order.PayPalCaptureId is null)
        {
            throw new InvalidPaymentStateException($"Order {order.Id} cannot be refunded while in state {order.Status}.");
        }

        var currency = RequireCurrency();
        var refundAmount = amount ?? order.RefundableAmount();
        if (refundAmount <= 0m || refundAmount > order.RefundableAmount())
        {
            throw new RefundExceedsCapturedException(
                $"Refund of {refundAmount} {currency} exceeds the refundable remainder {order.RefundableAmount()} {currency} of order {order.Id}.");
        }

        var gatewayRefund = await _paymentGateway.RefundCaptureAsync(
            order.PayPalCaptureId, refundAmount, currency, noteToPayer, idempotencyKey, cancellationToken);

        var refund = order.AddRefund(gatewayRefund.Id, gatewayRefund.Status, gatewayRefund.Amount, gatewayRefund.Currency, idempotencyKey, noteToPayer);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return new RefundResult
        {
            Refund = refund,
            TotalRefunded = order.TotalRefunded(),
            RefundableAmount = order.RefundableAmount()
        };
    }

    public async Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        var customerId = GetVaultCustomerId(buyerId);
        // Unique key per save call: PayPal replays a stored response for a repeated key,
        // which breaks if the earlier token has since been deleted.
        var idempotencyKey = $"eshop-vault-{Guid.NewGuid():N}";

        var token = await _paymentGateway.CreateVaultTokenAsync(card, customerId, idempotencyKey, cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, token.Id, token.Brand, token.LastDigits, token.Expiry, token.CardholderName);
        await _paymentMethodRepository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var savedCard = await _paymentMethodRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(paymentMethodId), cancellationToken);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            throw new PaymentResourceNotFoundException($"No saved payment method {paymentMethodId} found for this shopper.");
        }

        try
        {
            await _paymentGateway.DeleteVaultTokenAsync(savedCard.VaultTokenId, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.HttpStatusCode == 404)
        {
            // Already gone from the vault; still remove the local reference.
        }

        await _paymentMethodRepository.DeleteAsync(savedCard, cancellationToken);
    }

    public async Task<ReconciliationReport> GetReconciliationReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            throw new ArgumentException("The 'to' date-time must be after the 'from' date-time.");
        }
        // The transaction search contract supports a maximum range of 31 days.
        if (to - from > TimeSpan.FromDays(31))
        {
            throw new ArgumentException("The reconciliation range must not exceed 31 days.");
        }

        var transactions = await _paymentGateway.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentsSpecification(), cancellationToken);

        var report = new ReconciliationReport { From = from, To = to };
        foreach (var order in orders)
        {
            report.Orders.Add(new ReconciliationOrder
            {
                OrderId = order.Id,
                BuyerId = order.BuyerId,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Currency = order.Currency,
                PayPalOrderId = order.PayPalOrderId,
                PayPalAuthorizationId = order.PayPalAuthorizationId,
                PayPalCaptureId = order.PayPalCaptureId,
                PayPalRefundIds = order.Refunds.Select(r => r.PayPalRefundId).ToList()
            });
        }

        foreach (var txn in transactions)
        {
            var row = new ReconciliationTransaction
            {
                TransactionId = txn.TransactionId,
                EventCode = txn.EventCode,
                Status = txn.Status,
                Amount = txn.Amount,
                Currency = txn.Currency,
                FeeAmount = txn.FeeAmount,
                InvoiceId = txn.InvoiceId,
                CustomId = txn.CustomId,
                ReferenceId = txn.ReferenceId,
                ReferenceIdType = txn.ReferenceIdType,
                InitiationDate = txn.InitiationDate
            };

            var (order, basis) = MatchOrder(txn, report.Orders);
            if (order is not null)
            {
                row.MatchedOrderId = order.OrderId;
                row.MatchBasis = basis;
                order.SeenInPayPalReport = true;
                order.MatchedTransactionIds.Add(txn.TransactionId);
            }

            report.PayPalTransactions.Add(row);
        }

        return report;
    }

    private static (ReconciliationOrder? Order, string? Basis) MatchOrder(GatewayTransaction txn, List<ReconciliationOrder> orders)
    {
        foreach (var order in orders)
        {
            if (order.PayPalCaptureId is not null && txn.TransactionId == order.PayPalCaptureId)
                return (order, "captureId");
            if (order.PayPalAuthorizationId is not null && txn.TransactionId == order.PayPalAuthorizationId)
                return (order, "authorizationId");
            if (order.PayPalOrderId is not null && txn.TransactionId == order.PayPalOrderId)
                return (order, "payPalOrderId");
            if (order.PayPalRefundIds.Contains(txn.TransactionId))
                return (order, "refundId");
        }

        // Fall back to the merchant-supplied references PayPal reports back.
        if (!string.IsNullOrEmpty(txn.CustomId) && txn.CustomId.StartsWith("eshop-order-", StringComparison.Ordinal)
            && int.TryParse(txn.CustomId["eshop-order-".Length..], out var customOrderId))
        {
            var order = orders.FirstOrDefault(o => o.OrderId == customOrderId);
            if (order is not null) return (order, "customId");
        }
        if (!string.IsNullOrEmpty(txn.InvoiceId) && txn.InvoiceId.StartsWith("ESHOP-", StringComparison.Ordinal))
        {
            // Invoice format: ESHOP-{orderId}-{attempt}-{random}
            var remainder = txn.InvoiceId["ESHOP-".Length..];
            var digits = remainder.Split('-')[0];
            if (int.TryParse(digits, out var invoiceOrderId))
            {
                var order = orders.FirstOrDefault(o => o.OrderId == invoiceOrderId);
                if (order is not null) return (order, "invoiceId");
            }
        }

        // A capture/refund references its parent transaction via paypal_reference_id.
        if (!string.IsNullOrEmpty(txn.ReferenceId))
        {
            foreach (var order in orders)
            {
                if (txn.ReferenceId == order.PayPalAuthorizationId || txn.ReferenceId == order.PayPalCaptureId
                    || txn.ReferenceId == order.PayPalOrderId || order.PayPalRefundIds.Contains(txn.ReferenceId))
                    return (order, "referenceId");
            }
        }

        return (null, null);
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentDetailsSpecification(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentResourceNotFoundException($"No order found with id {orderId}.");
        }
        return order;
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.BuyerId != buyerId)
        {
            throw new PaymentResourceNotFoundException($"No order found with id {orderId}.");
        }
        return order;
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_currency))
        {
            throw new InvalidOperationException("Payment currency is not configured. Set PayPal:Currency (PAYPAL_CURRENCY).");
        }
        return _currency;
    }

    /// <summary>
    /// Deterministic PayPal vault customer id for a shopper. Matches the contract pattern
    /// ^[0-9a-zA-Z_-]+$ with max length 22, without storing anything extra.
    /// </summary>
    private static string GetVaultCustomerId(string buyerId) => $"eshop-{StableHash(buyerId, 16)}";

    private static string StableHash(string input, int length)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant()[..length];
    }
}
