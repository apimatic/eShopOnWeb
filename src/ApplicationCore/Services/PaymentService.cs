using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates authorize/fulfil/cancel/refund over a <see cref="Payment"/>, coordinating the
/// domain aggregate with <see cref="IPayPalGateway"/>. Every step is idempotent in effect: local
/// state guards a double-click, and each PayPal call carries a deterministic idempotency key.
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IReadRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _gateway;

    public PaymentService(
        IRepository<Payment> paymentRepository,
        IReadRepository<SavedCard> savedCardRepository,
        IPayPalGateway gateway)
    {
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
    }

    public async Task<Payment> PayAsync(int orderId, string buyerId, PaymentInstruction instruction, CancellationToken ct)
    {
        var payment = await GetOwnedPaymentAsync(orderId, buyerId, ct);

        // Idempotent: a hold (or more) already exists — never authorize the shopper twice.
        if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment;
        }
        if (payment.Status == PaymentStatus.Cancelled)
        {
            throw new PaymentException($"Order {orderId} was cancelled and can no longer be paid.", 409);
        }

        var card = await ResolveCardAsync(buyerId, instruction, ct);

        try
        {
            var result = await _gateway.AuthorizeAsync(
                payment.Amount, orderId.ToString(CultureInfo.InvariantCulture),
                payment.InvoiceId, card, payment.InvoiceId, ct);

            payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status);
            await _paymentRepository.UpdateAsync(payment, ct);
            return payment;
        }
        catch (PayPalGatewayException)
        {
            payment.MarkFailed();
            await _paymentRepository.UpdateAsync(payment, ct);
            throw;
        }
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await GetPaymentAsync(orderId, ct);

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment; // already fulfilled — idempotent
        }
        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentException(
                $"Order {orderId} cannot be fulfilled from status {payment.Status}; it must be authorized first.", 409);
        }

        var authorizationId = payment.AuthorizationId!;
        var captureKey = $"{payment.InvoiceId}-cap";

        PayPalCaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(authorizationId, payment.Amount, captureKey, ct);
        }
        catch (PayPalGatewayException ex) when (IsStaleAuthorization(ex))
        {
            // The hold went stale before fulfilment — renew it rather than failing outright.
            PayPalAuthorizationRef renewed;
            try
            {
                renewed = await _gateway.ReauthorizeAsync(
                    authorizationId, payment.Amount, $"{payment.InvoiceId}-reauth", ct);
            }
            catch (PayPalGatewayException reauthEx)
            {
                throw new PaymentException(
                    $"Order {orderId}: the authorization has gone stale and can no longer be renewed " +
                    $"({reauthEx.Issue ?? reauthEx.Message}). A fresh payment is required before this order can be fulfilled.",
                    409);
            }

            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status);
            await _paymentRepository.UpdateAsync(payment, ct);

            capture = await _gateway.CaptureAsync(
                renewed.AuthorizationId, payment.Amount, $"{captureKey}-renewed", ct);
        }

        payment.MarkCaptured(
            capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);
        return payment;
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await GetPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return payment; // idempotent
        }
        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentException(
                $"Order {orderId} cannot be cancelled from status {payment.Status}; only an authorized, " +
                "not-yet-fulfilled order can be cancelled (use a refund after fulfilment).", 409);
        }

        await _gateway.VoidAsync(payment.AuthorizationId!, $"{payment.InvoiceId}-void", ct);
        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, ct);
        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(
        int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var payment = await GetOwnedPaymentAsync(orderId, buyerId, ct);

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentException(
                $"Order {orderId} cannot be refunded from status {payment.Status}; only a captured order can be refunded.", 409);
        }

        // Idempotent: repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        // Validate before moving money so the domain invariant is never breached mid-flight.
        if (amount is <= 0m)
        {
            throw new PaymentException("A refund amount, when supplied, must be positive.", 422);
        }
        var refundAmount = amount ?? payment.RefundableAmount;
        if (refundAmount <= 0m)
        {
            throw new PaymentException($"Order {orderId} has nothing left to refund.", 422);
        }
        if (refundAmount > payment.RefundableAmount)
        {
            throw new PaymentException(
                $"Refund of {refundAmount:0.00} exceeds the refundable amount {payment.RefundableAmount:0.00}.", 422);
        }

        var result = await _gateway.RefundAsync(payment.CaptureId!, amount, idempotencyKey, ct);
        var recorded = result.Amount > 0m ? result.Amount : refundAmount;

        var refund = payment.AddRefund(result.RefundId, recorded, idempotencyKey, result.Status);
        await _paymentRepository.UpdateAsync(payment, ct);
        return refund;
    }

    private async Task<Payment> GetPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        return payment ?? throw new PaymentException($"No payment found for order {orderId}.", 404);
    }

    private async Task<Payment> GetOwnedPaymentAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var payment = await GetPaymentAsync(orderId, ct);
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not reveal another shopper's order — treat as not found for this caller.
            throw new PaymentException($"No payment found for order {orderId}.", 404);
        }
        return payment;
    }

    private async Task<CardPaymentInput> ResolveCardAsync(string buyerId, PaymentInstruction instruction, CancellationToken ct)
    {
        if (instruction.SavedCardId is int savedCardId)
        {
            var card = await _savedCardRepository.GetByIdAsync(savedCardId, ct);
            if (card is null || !string.Equals(card.BuyerId, buyerId, StringComparison.Ordinal))
            {
                throw new PaymentException($"Saved card {savedCardId} was not found.", 404);
            }
            return new CardPaymentInput { VaultId = card.VaultTokenId };
        }

        if (string.IsNullOrWhiteSpace(instruction.CardNumber) || string.IsNullOrWhiteSpace(instruction.Expiry))
        {
            throw new PaymentException(
                "Provide either a saved card id, or card number and expiry for a one-off payment.", 400);
        }

        return new CardPaymentInput
        {
            Number = instruction.CardNumber,
            Expiry = instruction.Expiry,
            SecurityCode = instruction.SecurityCode,
            CardholderName = instruction.CardholderName
        };
    }

    // A capture fails on a stale hold with a renewable issue; PayPal names it in the error body.
    private static bool IsStaleAuthorization(PayPalGatewayException ex)
    {
        var issue = ex.Issue;
        return string.Equals(issue, "AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(issue, "AUTHORIZATION_ALREADY_CAPTURED_OR_EXPIRED", StringComparison.OrdinalIgnoreCase)
            || (issue is null && ex.Message.Contains("expired", StringComparison.OrdinalIgnoreCase));
    }
}
