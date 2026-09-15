using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Payment> paymentRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IPayPalClient payPalClient,
        IAppLogger<PaymentService> logger)
    {
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPalClient = payPalClient;
        _logger = logger;
    }

    public async Task<Payment> AuthorizeAsync(int orderId, string buyerId, PayPalCardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderAndBuyerSpecification(orderId, buyerId), cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} was not found.");

        // Idempotent in effect: once a hold exists (or money has moved) a repeat never authorizes twice.
        if (payment.Status != PaymentStatus.AwaitingPayment)
        {
            if (payment.AuthorizationId is not null)
            {
                return payment;
            }
            throw new PaymentException($"Order {orderId} cannot be paid in its current state ({payment.Status}).");
        }

        var invoiceId = payment.InvoiceId;
        var idempotencyKey = $"authorize-{payment.InvoiceId}";

        PayPalAuthorizationResult result;
        string description;

        if (savedPaymentMethodId.HasValue)
        {
            var savedCard = await _paymentMethodRepository.FirstOrDefaultAsync(
                new PaymentMethodByIdAndBuyerSpecification(savedPaymentMethodId.Value, buyerId), cancellationToken)
                ?? throw new NotFoundException($"Saved card {savedPaymentMethodId.Value} was not found.");

            result = await _payPalClient.AuthorizeOrderWithVaultAsync(
                payment.Amount, payment.CurrencyCode, savedCard.PayPalVaultId, invoiceId, idempotencyKey, cancellationToken);
            description = savedCard.Describe();
        }
        else if (card is not null)
        {
            result = await _payPalClient.AuthorizeOrderWithCardAsync(
                payment.Amount, payment.CurrencyCode, card, invoiceId, idempotencyKey, cancellationToken);
            description = $"CARD ****{card.LastFourDigits}";
        }
        else
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId to pay.");
        }

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus, description);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Authorized order {orderId}: authorization {result.AuthorizationId} ({result.AuthorizationStatus}).");
        return payment;
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderSpecification(orderId), cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} was not found.");

        // Idempotent: already captured -> return the existing capture.
        if (payment.CaptureId is not null)
        {
            return payment;
        }

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentException($"Order {orderId} cannot be fulfilled before it is paid (current state: {payment.Status}).");
        }

        var amount = payment.Amount;
        var currency = payment.CurrencyCode;
        var invoiceId = payment.InvoiceId;
        var idempotencyKey = $"capture-{payment.InvoiceId}";
        var authorizationId = payment.AuthorizationId;

        // An authorization that has gone stale must be renewed rather than failing fulfilment outright.
        var authorizationStatus = await _payPalClient.GetAuthorizationStatusAsync(authorizationId, cancellationToken);
        var reauthorized = false;
        if (IsStale(authorizationStatus))
        {
            authorizationId = await RenewOrThrowAsync(payment, authorizationId, amount, currency, cancellationToken);
            reauthorized = true;
        }

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPalClient.CaptureAsync(authorizationId, amount, currency, invoiceId, idempotencyKey, cancellationToken);
        }
        catch (PayPalApiException ex) when (!reauthorized && IsExpiredError(ex))
        {
            authorizationId = await RenewOrThrowAsync(payment, authorizationId, amount, currency, cancellationToken);
            capture = await _payPalClient.CaptureAsync(authorizationId, amount, currency, invoiceId, idempotencyKey, cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Fulfilled order {orderId}: captured {capture.CaptureId} gross={capture.GrossAmount} fee={capture.PayPalFee} net={capture.NetAmount}.");
        return payment;
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderSpecification(orderId), cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} was not found.");

        if (payment.Status == PaymentStatus.Canceled)
        {
            return payment;
        }

        if (payment.CaptureId is not null)
        {
            throw new PaymentException($"Order {orderId} has already been fulfilled; issue a refund to return funds.");
        }

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentException($"Order {orderId} has no held funds to release (current state: {payment.Status}).");
        }

        await _payPalClient.VoidAsync(payment.AuthorizationId, cancellationToken);
        payment.MarkCanceled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Canceled order {orderId}: released authorization {payment.AuthorizationId}.");
        return payment;
    }

    public async Task<Refund> RefundAsync(int orderId, decimal? amount, string idempotencyKey,
        string requesterBuyerId, bool requesterIsAdmin, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("A refund requires an idempotency key.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderSpecification(orderId), cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} was not found.");

        // A non-admin caller may only refund an order they own; hide others' orders as "not found".
        if (!requesterIsAdmin && !string.Equals(payment.BuyerId, requesterBuyerId, StringComparison.Ordinal))
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }

        if (payment.CaptureId is null)
        {
            throw new PaymentException($"Order {orderId} has not been fulfilled; there is nothing to refund.");
        }

        // Idempotency: repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var remaining = payment.RefundableRemaining();
        var refundAmount = amount ?? remaining;

        if (refundAmount <= 0)
        {
            throw new PaymentException($"Order {orderId} has nothing left to refund.");
        }
        // A partly-refunded order must never become refundable beyond what was captured.
        if (refundAmount > remaining)
        {
            throw new PaymentException(
                $"Refund of {refundAmount:0.00} exceeds the {remaining:0.00} still refundable against this capture.");
        }

        var idem = $"refund-{payment.InvoiceId}-{idempotencyKey}";
        var result = await _payPalClient.RefundAsync(
            payment.CaptureId, amount, payment.CurrencyCode, payment.InvoiceId, idem, cancellationToken);

        var recordedAmount = result.Amount > 0 ? result.Amount : refundAmount;
        var refund = payment.AddRefund(idempotencyKey, result.RefundId, recordedAmount, result.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Refunded order {orderId}: refund {result.RefundId} amount={recordedAmount} status={result.Status}.");
        return refund;
    }

    private async Task<string> RenewOrThrowAsync(Payment payment, string authorizationId, decimal amount,
        string currency, CancellationToken cancellationToken)
    {
        try
        {
            var newAuthorizationId = await _payPalClient.ReauthorizeAsync(authorizationId, amount, currency, cancellationToken);
            payment.RenewAuthorization(newAuthorizationId, "CREATED");
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation($"Renewed stale authorization for order {payment.OrderId}: {authorizationId} -> {newAuthorizationId}.");
            return newAuthorizationId;
        }
        catch (PayPalApiException ex)
        {
            throw new AuthorizationExpiredException(
                $"The payment authorization for order {payment.OrderId} has expired and could not be renewed " +
                $"({ex.ErrorName ?? "AUTHORIZATION_EXPIRED"}). Ask the shopper to pay the order again before fulfilling it.");
        }
    }

    private static bool IsStale(string authorizationStatus) =>
        string.Equals(authorizationStatus, "EXPIRED", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpiredError(PayPalApiException ex) =>
        ex.HasIssue("AUTHORIZATION_EXPIRED", "PAYMENT_ALREADY_DONE_OR_EXPIRED");
}
