using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // A per-process run id. PayPal idempotency keys must be unique per logical operation, but with
    // the in-memory database order ids reset to 1 on every restart, and several hosts may share one
    // PayPal account. Namespacing the authorize key with this run id keeps a double-click within a
    // run de-duplicated at PayPal while never colliding with a different run's cached result.
    private static readonly string RunId = System.Guid.NewGuid().ToString("N").Substring(0, 12);

    private readonly IRepository<Order> _orderRepository;
    private readonly IPaymentMethodService _paymentMethods;
    private readonly IPayPalPaymentGateway _gateway;

    public PaymentService(IRepository<Order> orderRepository,
        IPaymentMethodService paymentMethods,
        IPayPalPaymentGateway gateway)
    {
        _orderRepository = orderRepository;
        _paymentMethods = paymentMethods;
        _gateway = gateway;
    }

    // ------------------------------------------------------------------ Authorize (shopper)

    public async Task<Order> AuthorizeAsync(int orderId, string buyerId, CardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdAndBuyerSpec(orderId, buyerId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        // Idempotent in effect: a double-click never authorizes twice. If a hold already exists,
        // return the order unchanged.
        if (order.Payment is not null)
            return order;
        if (order.Status == OrderStatus.Cancelled)
            throw new PaymentValidationException("This order was cancelled and can no longer be paid.");

        var instrument = await ResolveInstrumentAsync(buyerId, card, savedPaymentMethodId, cancellationToken);

        var amount = order.Total();
        var currency = _gateway.Currency;
        var idempotencyKey = $"authorize-order-{order.Id}-{RunId}";
        // Stamped onto the PayPal transaction (custom_id) and stored so reconciliation can line the
        // two records up precisely, without colliding with other orders/runs that share the account.
        var reconciliationReference = $"eshop-order-{order.Id}-{RunId}";

        var result = await _gateway.AuthorizeOrderAsync(reconciliationReference, amount, currency,
            instrument, idempotencyKey, cancellationToken);

        order.SetAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status,
            result.ExpiresAt, amount, currency, reconciliationReference);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    private async Task<PaymentInstrument> ResolveInstrumentAsync(string buyerId, CardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken)
    {
        if (savedPaymentMethodId is not null)
        {
            var vaultId = await _paymentMethods.ResolveVaultIdAsync(buyerId, savedPaymentMethodId.Value, cancellationToken);
            return PaymentInstrument.FromVault(vaultId);
        }
        if (card is not null)
            return PaymentInstrument.FromCard(card);

        throw new PaymentValidationException("Provide either card details or a saved payment method id to pay.");
    }

    // ------------------------------------------------------------------ Fulfil / capture (operator)

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        if (order.Status == OrderStatus.Cancelled)
            throw new PaymentValidationException("Cannot fulfil a cancelled order.");
        if (order.Payment is null)
            throw new PaymentValidationException("Cannot fulfil an order that has not been paid.");

        // Idempotent: already captured -> return unchanged.
        if (order.Payment.IsCaptured)
            return order;

        var capture = await CaptureWithRenewalAsync(order, cancellationToken);
        order.MarkFulfilled(capture.CaptureId, capture.Status, capture.GrossAmount,
            capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    private async Task<CaptureResult> CaptureWithRenewalAsync(Order order, CancellationToken cancellationToken)
    {
        var payment = order.Payment!;
        // The authorization id is PayPal-generated and globally unique, so keying the capture on it
        // is idempotent for a double-click yet never collides with another run's cached result.
        try
        {
            return await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId,
                $"capture-{payment.AuthorizationId}", cancellationToken);
        }
        catch (PayPalGatewayException captureError)
        {
            // The hold may have gone stale before fulfilment. Try to renew it, then capture the
            // renewed hold. If it can no longer be renewed, TryRenew throws an operator-actionable error.
            if (!await TryRenewAuthorizationAsync(order, captureError, cancellationToken))
                throw;

            var renewedAuthId = order.Payment!.AuthorizationId;
            return await _gateway.CaptureAuthorizationAsync(renewedAuthId,
                $"capture-{renewedAuthId}", cancellationToken);
        }
    }

    private async Task<bool> TryRenewAuthorizationAsync(Order order, PayPalGatewayException captureError,
        CancellationToken cancellationToken)
    {
        // Only a stale/expired/gone authorization is renewable; other failures (e.g. declined funds)
        // must surface as-is rather than be masked by a renewal attempt.
        var looksStale = captureError.StatusCode is 404 or 409 or 422;
        if (!looksStale)
            return false;

        var payment = order.Payment!;
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId, payment.Amount,
                payment.Currency, cancellationToken);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            return true;
        }
        catch (PayPalGatewayException renewError)
        {
            throw new AuthorizationNotRenewableException(
                $"The payment hold for order {order.Id} has expired and could not be renewed " +
                $"({renewError.PayPalErrorName ?? "renewal was rejected"}). The order cannot be fulfilled " +
                "against this hold; ask the shopper to pay for the order again.",
                renewError);
        }
    }

    // ------------------------------------------------------------------ Cancel / void (operator)

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        if (order.Status == OrderStatus.Fulfilled)
            throw new PaymentValidationException(
                "Cannot cancel an order that has already been fulfilled; issue a refund instead.");

        // Idempotent: already cancelled -> return unchanged.
        if (order.Status == OrderStatus.Cancelled)
            return order;

        if (order.Payment is not null)
        {
            try
            {
                await _gateway.VoidAuthorizationAsync(order.Payment.AuthorizationId, cancellationToken);
            }
            catch (PayPalGatewayException ex) when (ex.StatusCode is 404 or 422)
            {
                // Already released/gone at PayPal — nothing more to release.
            }
        }

        order.Cancel();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    // ------------------------------------------------------------------ Refund (operator)

    public async Task<Order> RefundAsync(int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new PaymentValidationException("A refund requires an idempotency key.");

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        var payment = order.Payment;
        if (payment is null || !payment.IsCaptured)
            throw new PaymentValidationException("Only a fulfilled (captured) order can be refunded.");

        // Idempotent on the caller's key: repeating under the same key returns the original refund
        // and never refunds twice. Two distinct keys remain two legitimate partial refunds.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
            return order;

        var requested = amount ?? payment.RefundableAmount;
        if (requested <= 0m)
            throw new PaymentValidationException("There is nothing left to refund on this order.");
        // A partly-refunded order must never become refundable beyond what was captured.
        if (requested > payment.RefundableAmount)
            throw new PaymentValidationException(
                $"Refund of {requested:0.00} {payment.Currency} exceeds the refundable amount " +
                $"{payment.RefundableAmount:0.00} {payment.Currency} for this order.");

        // amount == null => refund the full remaining balance (PayPal computes it). The PayPal
        // idempotency header combines the globally-unique capture id with the caller's key so it is
        // collision-free across runs, while our own dedup above keys on the caller's raw key.
        var payPalKey = $"refund-{payment.CaptureId}-{idempotencyKey}";
        var result = await _gateway.RefundCaptureAsync(payment.CaptureId!, amount, payment.Currency,
            payPalKey, cancellationToken);

        payment.AddRefund(idempotencyKey, result.RefundId, result.Amount, result.Status);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }
}
