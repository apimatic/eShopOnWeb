using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.Exceptions;
using FinixApiReference.Core.ErrorResponse;
using FinixApiReference.Core.Models;
using FinixApiReference.Models;
using FinixApiReference.Models.AnyOf;
using FinixApiReference.Models.Enums;
using FinixApiReference.Requests.AuthorizationsApi;
using FinixApiReference.Requests.FeesApi;
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;
using FinixAddress = FinixApiReference.Models.Address;
using FinixAuthorization = FinixApiReference.Models.Authorization;
using OrderEntity = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order;

namespace Microsoft.eShopWeb.PublicApi.Services;

/// <summary>
/// Owns all Finix interactions and the local payment state machine. Card data is passed
/// straight to Finix and is never persisted or logged by this class.
/// </summary>
public sealed class FinixPaymentService
{
    private const int MaxPageSize = 100;

    private readonly FinixApiReferenceClient _client;
    private readonly CatalogContext _db;
    private readonly PaymentOperationLock _locks;
    private readonly IOptions<FinixOptions> _options;

    public FinixPaymentService(
        FinixApiReferenceClient client,
        CatalogContext db,
        PaymentOperationLock locks,
        IOptions<FinixOptions> options)
    {
        _client = client;
        _db = db;
        _locks = locks;
        _options = options;
    }

    public static long ToCents(decimal amount) =>
        checked((long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

    public async Task<OrderPayment> CreatePaymentAsync(OrderEntity order, CancellationToken cancellationToken)
    {
        var payment = new OrderPayment
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Currency = GetCurrencyCode(),
            AmountCents = ToCents(order.Total()),
            Status = PaymentStatus.AwaitingPayment,
            AuthorizationIdempotencyId = Guid.NewGuid().ToString("N")
        };

        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);
        return payment;
    }

    public async Task<SavedCard> SaveCardAsync(
        string buyerId,
        CardDetails card,
        CancellationToken cancellationToken)
    {
        ValidateCard(card);
        var identityId = await GetOrCreateIdentityAsync(buyerId, card, cancellationToken);
        var instrument = await CreatePaymentInstrumentAsync(identityId, card, cancellationToken);

        var savedCard = new SavedCard
        {
            BuyerId = buyerId,
            FinixIdentityId = identityId,
            FinixPaymentInstrumentId = instrument.Id!,
            Brand = instrument.Brand?.Value,
            Last4 = instrument.LastFour,
            ExpirationMonth = instrument.ExpirationMonth ?? card.ExpirationMonth,
            ExpirationYear = instrument.ExpirationYear ?? card.ExpirationYear,
            CardholderName = instrument.Name ?? card.CardholderName,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.SavedCards.Add(savedCard);
        await _db.SaveChangesAsync(cancellationToken);
        return savedCard;
    }

    public Task<List<SavedCard>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken) =>
        _db.SavedCards
            .Where(c => c.BuyerId == buyerId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<bool> DeleteSavedCardAsync(
        string buyerId,
        string paymentMethodId,
        CancellationToken cancellationToken)
    {
        using var handle = await _locks.AcquireAsync($"buyer:{buyerId}", cancellationToken);

        var savedCard = await _db.SavedCards
            .FirstOrDefaultAsync(c => c.PublicId == paymentMethodId && c.BuyerId == buyerId, cancellationToken);
        if (savedCard is null)
        {
            return false;
        }

        try
        {
            await _client.PaymentInstrumentsApi.UpdatePaymentInstrument(
                new UpdatePaymentInstrumentRequest
                {
                    PaymentInstrumentId = savedCard.FinixPaymentInstrumentId,
                    Body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(
                        new PaymentInstrumentDisableAPaymentInstrument { Enabled = false })
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException)
        {
            // The local record is the source of truth for this app. Removing it makes the
            // card unusable through this API even if Finix reports the instrument as gone.
        }

        _db.SavedCards.Remove(savedCard);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<OrderPayment> PayAsync(
        OrderEntity order,
        OrderPayment payment,
        string? paymentMethodId,
        CardDetails? card,
        CancellationToken cancellationToken)
    {
        using var handle = await _locks.AcquireAsync($"order:{order.Id}", cancellationToken);
        EnsureConfigured();

        switch (payment.Status)
        {
            case PaymentStatus.Authorized:
            case PaymentStatus.Captured:
            case PaymentStatus.PartiallyRefunded:
            case PaymentStatus.Refunded:
                return payment;
            case PaymentStatus.Canceled:
                throw new PaymentOperationException(
                    "This order was canceled. Place a new order to take a payment.",
                    PaymentFailureKind.Conflict);
        }

        if (!string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            return payment;
        }

        string source;
        string? savedCardPublicId = null;

        if (!string.IsNullOrWhiteSpace(paymentMethodId))
        {
            var savedCard = await _db.SavedCards
                .FirstOrDefaultAsync(c => c.PublicId == paymentMethodId && c.BuyerId == order.BuyerId, cancellationToken);
            if (savedCard is null)
            {
                throw new PaymentOperationException(
                    "Saved card not found for this shopper.",
                    PaymentFailureKind.NotFound);
            }

            source = savedCard.FinixPaymentInstrumentId;
            savedCardPublicId = savedCard.PublicId;
        }
        else
        {
            if (card is null)
            {
                throw new PaymentOperationException(
                    "Provide card details or a saved paymentMethodId.",
                    PaymentFailureKind.Validation);
            }

            ValidateCard(card);
            var identityId = await GetOrCreateIdentityAsync(order.BuyerId, card, cancellationToken);
            var instrument = await CreatePaymentInstrumentAsync(identityId, card, cancellationToken);
            source = instrument.Id!;
        }

        FinixAuthorization? authorization = null;
        try
        {
            authorization = await CreateAuthorizationWithSourceRetryAsync(
                payment,
                source,
                order.Id,
                cancellationToken);
        }
        catch (Exception ex)
        {
            payment.Status = PaymentStatus.PaymentFailed;
            payment.FailureCode = "AUTHORIZATION_FAILED";
            payment.FailureMessage = DescribeFinixError(ex);
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            throw ToOperationException(ex, "Card authorization");
        }

        ApplyAuthorization(payment, authorization);
        payment.FinixPaymentInstrumentId = source;
        payment.SavedCardPublicId = savedCardPublicId;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        if (payment.Status == PaymentStatus.PaymentFailed)
        {
            throw new PaymentOperationException(
                $"Finix declined the card authorization: {payment.FailureMessage ?? "no reason supplied"}",
                PaymentFailureKind.Conflict,
                payment.FailureCode);
        }

        if (authorization.Amount is { } authorizedAmount && authorizedAmount != payment.AmountCents)
        {
            payment.Status = PaymentStatus.PaymentFailed;
            payment.FailureCode = "AMOUNT_MISMATCH";
            payment.FailureMessage =
                $"Finix authorized {authorizedAmount} cents but the order total is {payment.AmountCents} cents.";
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(
                payment.FailureMessage + " Cancel the order or retry the payment.",
                PaymentFailureKind.Conflict,
                payment.FailureCode);
        }

        return payment;
    }

    public async Task<OrderPayment> FulfilAsync(
        OrderEntity order,
        OrderPayment payment,
        CancellationToken cancellationToken)
    {
        using var handle = await _locks.AcquireAsync($"order:{order.Id}", cancellationToken);
        EnsureConfigured();

        if (payment.Status is PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded)
        {
            return payment;
        }

        if (payment.Status == PaymentStatus.Canceled)
        {
            throw new PaymentOperationException(
                "This order was canceled and cannot be fulfilled.",
                PaymentFailureKind.Conflict);
        }

        if (payment.Status is PaymentStatus.AwaitingPayment or PaymentStatus.PaymentFailed)
        {
            throw new PaymentOperationException(
                "This order has not been paid. Authorize a card before fulfilling it.",
                PaymentFailureKind.Conflict);
        }

        if (string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            throw new PaymentOperationException(
                "This order has no Finix authorization to capture.",
                PaymentFailureKind.Conflict);
        }

        FinixAuthorization? currentAuthorization = null;
        try
        {
            currentAuthorization = await _client.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = payment.FinixAuthorizationId },
                cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            throw ToOperationException(
                ex,
                "Could not read the Finix authorization before capture");
        }

        if (currentAuthorization.IsVoid == true
            || currentAuthorization.State == State1.Canceled
            || currentAuthorization.State == State1.Failed)
        {
            throw new PaymentOperationException(
                $"The card hold can no longer be captured (Finix authorization {payment.FinixAuthorizationId} " +
                $"is {currentAuthorization.State?.Value ?? "unknown"}" +
                (currentAuthorization.IsVoid == true ? ", voided" : string.Empty) +
                "). Cancel the order or ask the shopper to pay again with a new authorization.",
                PaymentFailureKind.Conflict,
                currentAuthorization.FailureCode);
        }

        if (currentAuthorization.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            payment.Status = PaymentStatus.CaptureFailed;
            payment.FailureCode = "AUTHORIZATION_EXPIRED";
            payment.FailureMessage =
                $"The card hold expired at {expiresAt:O}. It can no longer be captured; cancel the order or ask the shopper to pay again.";
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(
                payment.FailureMessage,
                PaymentFailureKind.Conflict,
                payment.FailureCode);
        }

        FinixAuthorization? captured;
        try
        {
            captured = await _client.AuthorizationsApi.CaptureAuthorization(
                new CaptureAuthorizationRequest
                {
                    AuthorizationId = payment.FinixAuthorizationId,
                    Body = AuthorizationsRequest1.CaptureAuthorization(
                        new FinixApiReference.Models.CaptureAuthorization
                        {
                            CaptureAmount = payment.AmountCents
                        })
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // If a previous attempt captured but the response was lost, adopt the
            // authorization's transfer rather than taking the money twice.
            var recovered = await TryGetAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
            if (recovered is not null && !string.IsNullOrWhiteSpace(recovered.Transfer))
            {
                captured = recovered;
            }
            else
            {
                payment.Status = PaymentStatus.CaptureFailed;
                payment.FailureCode = "CAPTURE_FAILED";
                payment.FailureMessage = DescribeFinixError(ex);
                payment.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentOperationException(
                    $"Finix could not capture the card hold: {payment.FailureMessage}. " +
                    "The order is not fulfilled; the hold may have expired or been voided. " +
                    "Cancel the order or ask the shopper to pay again.",
                    PaymentFailureKind.Conflict,
                    payment.FailureCode,
                    ex);
            }
        }

        await ApplyCaptureAsync(payment, captured, cancellationToken);
        payment.Status = PaymentStatus.Captured;
        payment.FulfilledAt = DateTimeOffset.UtcNow;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return payment;
    }

    public async Task<OrderPayment> CancelAsync(
        OrderEntity order,
        OrderPayment payment,
        CancellationToken cancellationToken)
    {
        using var handle = await _locks.AcquireAsync($"order:{order.Id}", cancellationToken);
        EnsureConfigured();

        if (payment.Status == PaymentStatus.Canceled)
        {
            return payment;
        }

        if (payment.Status is PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded)
        {
            throw new PaymentOperationException(
                "This order has already been fulfilled. Use the refunds endpoint instead of cancel.",
                PaymentFailureKind.Conflict);
        }

        if (payment.Status == PaymentStatus.Authorized
            && !string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            try
            {
                var voided = await _client.AuthorizationsApi.VoidAuthorization(
                    new VoidAuthorizationRequest
                    {
                        AuthorizationIdVoidTo = payment.FinixAuthorizationId,
                        Body = new VoidAnAuthorization { VoidMe = true }
                    },
                    cancellationToken: cancellationToken);

                payment.AuthorizationState = voided.IsVoid == true || voided.VoidState == VoidState.Succeeded
                    ? "VOIDED"
                    : voided.State?.Value ?? payment.AuthorizationState;
            }
            catch (Exception ex)
            {
                var current = await TryGetAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
                if (current is null || (current.IsVoid != true && current.State != State1.Canceled))
                {
                    throw new PaymentOperationException(
                        $"Finix could not release the card hold: {DescribeFinixError(ex)}. " +
                        "The order was not canceled; retry the cancellation or contact the payment provider.",
                        PaymentFailureKind.Upstream,
                        null,
                        ex);
                }

                payment.AuthorizationState = current.IsVoid == true || current.State == State1.Canceled
                    ? "VOIDED"
                    : current.State?.Value ?? payment.AuthorizationState;
            }
        }

        payment.Status = PaymentStatus.Canceled;
        payment.CanceledAt = DateTimeOffset.UtcNow;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(
        OrderEntity order,
        OrderPayment payment,
        long? requestedAmountCents,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var handle = await _locks.AcquireAsync($"order:{order.Id}", cancellationToken);
        EnsureConfigured();

        if (payment.Status is not (PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded))
        {
            throw new PaymentOperationException(
                "Only a captured payment can be refunded. Fulfil the order before refunding it.",
                PaymentFailureKind.Conflict);
        }

        if (string.IsNullOrWhiteSpace(payment.FinixTransferId))
        {
            throw new PaymentOperationException(
                "The captured Finix transfer id is missing, so the payment cannot be refunded.",
                PaymentFailureKind.Conflict);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentOperationException(
                "An idempotencyKey is required for refunds.",
                PaymentFailureKind.Validation);
        }

        idempotencyKey = idempotencyKey.Trim();

        var existingRefund = await _db.PaymentRefunds
            .FirstOrDefaultAsync(r => r.OrderPaymentId == payment.Id && r.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existingRefund is not null)
        {
            return existingRefund;
        }

        var alreadyRefunded = await _db.PaymentRefunds
            .Where(r => r.OrderPaymentId == payment.Id)
            .SumAsync(r => (long?)r.AmountCents, cancellationToken) ?? 0L;

        var remaining = payment.CapturedAmountCents - alreadyRefunded;
        if (remaining <= 0)
        {
            throw new PaymentOperationException(
                "This payment has already been refunded in full.",
                PaymentFailureKind.Conflict);
        }

        var amount = requestedAmountCents ?? remaining;
        if (amount <= 0)
        {
            throw new PaymentOperationException(
                "The refund amount must be greater than zero.",
                PaymentFailureKind.Validation);
        }

        if (amount > remaining)
        {
            throw new PaymentOperationException(
                $"The refund amount {amount} cents exceeds the remaining refundable amount of {remaining} cents.",
                PaymentFailureKind.Conflict);
        }

        if (amount > int.MaxValue)
        {
            throw new PaymentOperationException(
                "The refund amount is too large for a single Finix reversal.",
                PaymentFailureKind.Validation);
        }

        await RefreshTransferStateAsync(payment, cancellationToken);
        if (amount < payment.CapturedAmountCents)
        {
            await WaitForTransferSettledAsync(payment.FinixTransferId, cancellationToken);
        }

        var recovered = await FindReversalByIdempotencyIdAsync(
            payment.FinixTransferId, idempotencyKey, cancellationToken);
        if (recovered is not null)
        {
            return await PersistRefundAsync(payment, amount, idempotencyKey, recovered, cancellationToken);
        }

        Transfer? reversal;
        try
        {
            reversal = await CreateReversalWithPendingRetryAsync(
                payment.FinixTransferId,
                amount,
                idempotencyKey,
                cancellationToken);
        }
        catch (Exception ex)
        {
            var found = await FindReversalByIdempotencyIdAsync(
                payment.FinixTransferId, idempotencyKey, cancellationToken);
            if (found is not null)
            {
                return await PersistRefundAsync(payment, amount, idempotencyKey, found, cancellationToken);
            }

            throw ToOperationException(ex, "Refund");
        }

        return await PersistRefundAsync(payment, amount, idempotencyKey, reversal, cancellationToken);
    }

    private async Task<FinixAuthorization> CreateAuthorizationWithSourceRetryAsync(
        OrderPayment payment,
        string source,
        int orderId,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt < 15; attempt++)
        {
            try
            {
                return await _client.AuthorizationsApi.CreateAuthorization(
                    new CreateAuthorizationRequest
                    {
                        Body = AuthorizationsRequest.AuthorizationAllFields(
                            new AuthorizationAllFields
                            {
                                Amount = payment.AmountCents,
                                Currency = ParseCurrency(),
                                Merchant = RequireMerchantId(),
                                Source = source,
                                IdempotencyId = payment.AuthorizationIdempotencyId,
                                Tags = new Dictionary<string, string>
                                {
                                    ["order_id"] = orderId.ToString(),
                                    ["payment_id"] = payment.Id.ToString()
                                }
                            })
                    },
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;

                // A retry with the same idempotency id raises in Finix. Look the
                // original authorization up before treating this as a new failure.
                var existing = await FindAuthorizationByIdempotencyIdAsync(
                    payment.AuthorizationIdempotencyId, cancellationToken);
                if (existing is not null)
                {
                    return existing;
                }

                // Newly created payment instruments can take a moment to become
                // visible to the authorization endpoint. Retry that specific case.
                if (attempt >= 14 || !IsSourceNotReadyError(ex))
                {
                    throw;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        throw lastException ?? new PaymentOperationException(
            "Finix did not accept the card authorization.",
            PaymentFailureKind.Upstream);
    }

    private static bool IsSourceNotReadyError(Exception exception)
    {
        var message = DescribeFinixError(exception);
        return message.Contains("source is incorrect or not found", StringComparison.OrdinalIgnoreCase)
               || (message.Contains("INVALID_FIELD", StringComparison.OrdinalIgnoreCase)
                   && message.Contains("source", StringComparison.OrdinalIgnoreCase));
    }
    private async Task RefreshTransferStateAsync(
        OrderPayment payment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payment.FinixTransferId))
        {
            return;
        }

        try
        {
            var transfer = await _client.TransfersApi.GetTransfer(
                new GetTransferRequest { TransferId = payment.FinixTransferId },
                cancellationToken: cancellationToken);
            payment.TransferState = transfer.State?.Value;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // State refresh is best effort.
        }
    }

    private async Task WaitForTransferSettledAsync(
        string transferId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                var transfer = await _client.TransfersApi.GetTransfer(
                    new GetTransferRequest { TransferId = transferId },
                    cancellationToken: cancellationToken);
                if (!string.Equals(transfer.State?.Value, "PENDING", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            catch (Exception)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private async Task<Transfer> CreateReversalWithPendingRetryAsync(
        string transferId,
        long amount,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt < 30; attempt++)
        {
            var existing = await FindReversalByIdempotencyIdAsync(
                transferId, idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            try
            {
                var body = new RefundTransfer
                {
                    RefundAmount = checked((int)amount),
                    AdditionalProperties = new AdditionalProperties()
                };
                body.AdditionalProperties.Set("idempotency_id", idempotencyKey);

                return await _client.TransfersApi.CreateTransferReversal(
                    new CreateTransferReversalRequest
                    {
                        TransferId = transferId,
                        Body = TransfersReversalsRequest.RefundTransfer(body)
                    },
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt >= 29 || !IsPendingTransferError(ex))
                {
                    throw;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        throw lastException ?? new PaymentOperationException(
            "Finix did not accept the refund reversal.",
            PaymentFailureKind.Upstream);
    }

    private static bool IsPendingTransferError(Exception exception) =>
        DescribeFinixError(exception).Contains("PENDING", StringComparison.OrdinalIgnoreCase);

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (to < from)
        {
            throw new PaymentOperationException(
                "'to' must be greater than or equal to 'from'.",
                PaymentFailureKind.Validation);
        }

        var finixTransfers = await ListAllTransfersAsync(from, to, cancellationToken);
        var payments = await _db.OrderPayments.ToListAsync(cancellationToken);
        var refunds = await _db.PaymentRefunds.ToListAsync(cancellationToken);
        var paymentsById = payments.ToDictionary(p => p.Id);

        var report = new ReconciliationReport { From = from, To = to };
        var finixById = finixTransfers
            .Where(t => !string.IsNullOrWhiteSpace(t.Id))
            .ToDictionary(t => t.Id!, StringComparer.Ordinal);

        // A just-created transfer may not be in the list yet (or may be past the
        // page cap). Confirm each eShop transfer id directly so the report does
        // not report a false mismatch immediately after a capture or refund.
        var missingTransferIds = payments
            .Where(p => p.CapturedAt is { } capturedAt && capturedAt >= from && capturedAt <= to)
            .Select(p => p.FinixTransferId)
            .Concat(refunds
                .Where(r => r.CreatedAt >= from && r.CreatedAt <= to)
                .Select(r => r.FinixTransferId))
            .Where(id => !string.IsNullOrWhiteSpace(id) && !finixById.ContainsKey(id!))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var transferId in missingTransferIds)
        {
            try
            {
                var transfer = await _client.TransfersApi.GetTransfer(
                    new GetTransferRequest { TransferId = transferId },
                    cancellationToken: cancellationToken);
                if (!string.IsNullOrWhiteSpace(transfer.Id))
                {
                    finixById[transfer.Id!] = transfer;
                    if (transfer.CreatedAt is null
                        || (transfer.CreatedAt >= from && transfer.CreatedAt <= to))
                    {
                        finixTransfers.Add(transfer);
                    }
                }
            }
            catch (Exception)
            {
                // It will remain an unmatched eShop transaction.
            }
        }

        var eshopCaptureTransferIds = new HashSet<string>(StringComparer.Ordinal);
        var eshopRefundTransferIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var payment in payments.Where(p =>
                     p.CapturedAt is { } capturedAt && capturedAt >= from && capturedAt <= to))
        {
            var matched = payment.FinixTransferId is not null
                          && finixById.ContainsKey(payment.FinixTransferId);
            if (payment.FinixTransferId is not null)
            {
                eshopCaptureTransferIds.Add(payment.FinixTransferId);
            }

            report.EshopTransactions.Add(new EshopTransactionRecord
            {
                OrderId = payment.OrderId,
                PaymentId = payment.Id,
                Kind = "capture",
                FinixTransferId = payment.FinixTransferId,
                AmountCents = payment.CapturedAmountCents,
                Currency = payment.Currency,
                CreatedAt = payment.CapturedAt,
                Matched = matched
            });
        }

        foreach (var refund in refunds.Where(r => r.CreatedAt >= from && r.CreatedAt <= to))
        {
            if (!paymentsById.TryGetValue(refund.OrderPaymentId, out var payment))
            {
                continue;
            }

            var matched = refund.FinixTransferId is not null
                          && finixById.ContainsKey(refund.FinixTransferId);
            if (refund.FinixTransferId is not null)
            {
                eshopRefundTransferIds.Add(refund.FinixTransferId);
            }

            report.EshopTransactions.Add(new EshopTransactionRecord
            {
                OrderId = payment.OrderId,
                PaymentId = payment.Id,
                Kind = "refund",
                FinixTransferId = refund.FinixTransferId,
                AmountCents = refund.AmountCents,
                Currency = payment.Currency,
                CreatedAt = refund.CreatedAt,
                RefundId = refund.PublicId,
                Matched = matched
            });
        }

        foreach (var transfer in finixTransfers)
        {
            var isCapture = transfer.Id is not null && eshopCaptureTransferIds.Contains(transfer.Id);
            var isRefund = transfer.Id is not null && eshopRefundTransferIds.Contains(transfer.Id);

            int? orderId = null;
            var capturePayment = payments.FirstOrDefault(p => p.FinixTransferId == transfer.Id);
            if (capturePayment is not null)
            {
                orderId = capturePayment.OrderId;
            }
            else
            {
                var matchedRefund = refunds.FirstOrDefault(r => r.FinixTransferId == transfer.Id);
                if (matchedRefund is not null
                    && paymentsById.TryGetValue(matchedRefund.OrderPaymentId, out var refundPayment))
                {
                    orderId = refundPayment.OrderId;
                }
            }

            report.FinixTransactions.Add(new FinixTransactionRecord
            {
                Id = transfer.Id,
                Type = transfer.Type?.Value,
                State = transfer.State?.Value,
                Amount = transfer.Amount,
                Currency = transfer.Currency?.Value,
                CreatedAt = transfer.CreatedAt,
                ParentTransfer = transfer.ParentTransfer,
                TraceId = transfer.TraceId,
                OrderId = orderId,
                EshopKind = isCapture ? "capture" : isRefund ? "refund" : null,
                Matched = isCapture || isRefund
            });
        }

        report.UnmatchedFinixTransactions = report.FinixTransactions.Where(t => !t.Matched).ToList();
        report.UnmatchedEshopTransactions = report.EshopTransactions.Where(t => !t.Matched).ToList();

        return report;
    }

    private async Task<PaymentRefund> PersistRefundAsync(
        OrderPayment payment,
        long amount,
        string idempotencyKey,
        Transfer reversal,
        CancellationToken cancellationToken)
    {
        var refund = new PaymentRefund
        {
            OrderPaymentId = payment.Id,
            PublicId = Guid.NewGuid().ToString("N"),
            IdempotencyKey = idempotencyKey,
            AmountCents = amount,
            FinixTransferId = reversal.Id,
            FinixState = reversal.State?.Value,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.PaymentRefunds.Add(refund);

        var totalRefunded = await _db.PaymentRefunds
            .Where(r => r.OrderPaymentId == payment.Id)
            .SumAsync(r => (long?)r.AmountCents, cancellationToken) ?? 0L;
        totalRefunded += amount;

        payment.Status = totalRefunded >= payment.CapturedAmountCents
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        payment.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return refund;
    }

    private async Task ApplyCaptureAsync(
        OrderPayment payment,
        FinixAuthorization captured,
        CancellationToken cancellationToken)
    {
        var transferId = captured.Transfer;
        Transfer? transfer = null;
        decimal feeCents = 0m;

        if (!string.IsNullOrWhiteSpace(transferId))
        {
            try
            {
                transfer = await _client.TransfersApi.GetTransfer(
                    new GetTransferRequest { TransferId = transferId },
                    cancellationToken: cancellationToken);
            }
            catch (Exception)
            {
                transfer = null;
            }

            try
            {
                feeCents = await SumFeesAsync(transferId, cancellationToken);
            }
            catch (Exception)
            {
                feeCents = 0m;
            }
        }

        var capturedAmount = transfer?.Amount ?? captured.Amount ?? payment.AmountCents;
        payment.FinixAuthorizationId = captured.Id ?? payment.FinixAuthorizationId;
        payment.AuthorizationState = captured.State?.Value ?? payment.AuthorizationState;
        payment.FinixTransferId = transferId ?? payment.FinixTransferId;
        payment.TransferState = transfer?.State?.Value;
        payment.CapturedAt = DateTimeOffset.UtcNow;
        payment.CapturedAmountCents = capturedAmount;
        payment.FinixFeeCents = feeCents;
        payment.NetProceedsCents = capturedAmount - feeCents;
        payment.FailureCode = captured.FailureCode;
        payment.FailureMessage = captured.FailureMessage;
    }

    private async Task<decimal> SumFeesAsync(string transferId, CancellationToken cancellationToken)
    {
        var total = 0m;
        string? cursor = null;

        while (true)
        {
            var page = await _client.FeesApi.ListFees(
                new ListFeesRequest
                {
                    LinkedTo = transferId,
                    MerchantId = RequireMerchantId(),
                    Limit = MaxPageSize,
                    AfterCursor = cursor
                },
                cancellationToken: cancellationToken);

            var fees = page.Embedded?.Fees;
            if (fees is not null)
            {
                foreach (var fee in fees)
                {
                    total += (decimal)(fee.Amount ?? 0d);
                }
            }

            cursor = page.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }
        }

        return total;
    }

    private async Task<List<Transfer>> ListAllTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var transfers = new List<Transfer>();
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var pages = 0;

        // The SDK's date query formatter emits fractional seconds, which Finix rejects.
        // List pages and filter the range locally so the report still covers the whole
        // requested window.
        while (pages++ < 100)
        {
            var page = await _client.TransfersApi.ListTransfers(
                new ListTransfersRequest
                {
                    Merchant = RequireMerchantId(),
                    Limit = MaxPageSize,
                    AfterCursor = cursor
                },
                cancellationToken: cancellationToken);

            if (page.Embedded?.Transfers is { } pageTransfers)
            {
                transfers.AddRange(pageTransfers.Where(t =>
                    t.CreatedAt is { } createdAt && createdAt >= from && createdAt <= to));
            }

            var next = page.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(next) || !seenCursors.Add(next))
            {
                break;
            }

            cursor = next;
        }

        return transfers;
    }

    private async Task<FinixAuthorization?> FindAuthorizationByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        try
        {
            string? cursor = null;
            var seenCursors = new HashSet<string>(StringComparer.Ordinal);
            var pages = 0;

            while (pages++ < 50)
            {
                var page = await _client.AuthorizationsApi.ListAuthorizations(
                    new ListAuthorizationsRequest
                    {
                        IdempotencyId = idempotencyId,
                        Merchant = RequireMerchantId(),
                        Limit = MaxPageSize,
                        AfterCursor = cursor
                    },
                    cancellationToken: cancellationToken);

                var match = page.Embedded?.Authorizations?.FirstOrDefault(a =>
                    string.Equals(a.IdempotencyId, idempotencyId, StringComparison.Ordinal));
                if (match is not null)
                {
                    return match;
                }

                var next = page.Page?.NextCursor;
                if (string.IsNullOrWhiteSpace(next) || !seenCursors.Add(next))
                {
                    return null;
                }

                cursor = next;
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<Transfer?> FindReversalByIdempotencyIdAsync(
        string parentTransferId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            string? cursor = null;
            var seenCursors = new HashSet<string>(StringComparer.Ordinal);
            var parentMatches = new List<Transfer>();

            while (true)
            {
                var page = await _client.TransfersApi.ListTransfers(
                    new ListTransfersRequest
                    {
                        ParentTransferId = parentTransferId,
                        IdempotencyId = idempotencyKey,
                        Merchant = RequireMerchantId(),
                        Limit = MaxPageSize,
                        AfterCursor = cursor
                    },
                    cancellationToken: cancellationToken);

                foreach (var transfer in page.Embedded?.Transfers ?? [])
                {
                    if (!string.Equals(transfer.Type?.Value, "REVERSAL", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.Equals(transfer.IdempotencyId, idempotencyKey, StringComparison.Ordinal))
                    {
                        return transfer;
                    }

                    if (string.Equals(transfer.ParentTransfer, parentTransferId, StringComparison.Ordinal))
                    {
                        parentMatches.Add(transfer);
                    }
                }

                var next = page.Page?.NextCursor;
                if (string.IsNullOrWhiteSpace(next) || !seenCursors.Add(next))
                {
                    break;
                }

                cursor = next;
            }

            // Finix does not always echo idempotency_id on a reversal. If there is
            // exactly one reversal for this capture, it is safe to treat it as ours.
            return parentMatches.Count == 1 ? parentMatches[0] : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<FinixAuthorization?> TryGetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _client.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = authorizationId },
                cancellationToken: cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<string> GetOrCreateIdentityAsync(
        string buyerId,
        CardDetails card,
        CancellationToken cancellationToken)
    {
        var existing = await _db.FinixBuyerIdentities
            .FirstOrDefaultAsync(i => i.BuyerId == buyerId, cancellationToken);
        if (existing is not null)
        {
            return existing.FinixIdentityId;
        }

        using var handle = await _locks.AcquireAsync($"buyer-identity:{buyerId}", cancellationToken);

        existing = await _db.FinixBuyerIdentities
            .FirstOrDefaultAsync(i => i.BuyerId == buyerId, cancellationToken);
        if (existing is not null)
        {
            return existing.FinixIdentityId;
        }

        var identity = await _client.Identities.CreateIdentity(
            new CreateIdentityRequest
            {
                Body = IdentitiesRequest.Buyer(
                    new FinixApiReference.Models.Buyer
                    {
                        Entity = new Entity5
                        {
                            FirstName = card.FirstName,
                            LastName = card.LastName,
                            Email = BuildEmail(buyerId),
                            PersonalAddress = ToFinixAddress(card.Address)
                        },
                        IdentityRoles = [IdentityRole5.Buyer],
                        Type = Type10.Personal
                    })
            },
            cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(identity.Id))
        {
            throw new PaymentOperationException(
                "Finix did not return an identity id for the shopper.",
                PaymentFailureKind.Upstream);
        }

        _db.FinixBuyerIdentities.Add(new FinixBuyerIdentity
        {
            BuyerId = buyerId,
            FinixIdentityId = identity.Id,
            CreatedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent request created the identity first. Re-read it.
            var local = _db.FinixBuyerIdentities.Local.FirstOrDefault(i => i.BuyerId == buyerId);
            if (local is not null)
            {
                _db.Entry(local).State = EntityState.Detached;
            }
            existing = await _db.FinixBuyerIdentities
                .FirstOrDefaultAsync(i => i.BuyerId == buyerId, cancellationToken);
            if (existing is not null)
            {
                return existing.FinixIdentityId;
            }

            throw;
        }

        return identity.Id;
    }

    private async Task<PaymentInstrumentCard> CreatePaymentInstrumentAsync(
        string identityId,
        CardDetails card,
        CancellationToken cancellationToken)
    {
        var result = await _client.PaymentInstrumentsApi.CreatePaymentInstrument(
            new CreatePaymentInstrumentRequest
            {
                Body = PaymentInstrumentsRequest.PaymentInstrumentCard1(
                    new PaymentInstrumentCard1
                    {
                        Identity = identityId,
                        Number = NormalizeCardNumber(card.Number),
                        ExpirationMonth = card.ExpirationMonth,
                        ExpirationYear = NormalizeYear(card.ExpirationYear),
                        SecurityCode = card.SecurityCode,
                        Name = string.IsNullOrWhiteSpace(card.CardholderName)
                            ? "eShop Shopper"
                            : card.CardholderName.Trim(),
                        Address = ToFinixAddress(card.Address)
                    })
            },
            cancellationToken: cancellationToken);

        if (!result.TryGetPaymentInstrumentCard(out var instrument)
            || string.IsNullOrWhiteSpace(instrument.Id))
        {
            throw new PaymentOperationException(
                "Finix did not return a payment instrument id.",
                PaymentFailureKind.Upstream);
        }

        return instrument;
    }

    private void ApplyAuthorization(OrderPayment payment, FinixAuthorization authorization)
    {
        payment.FinixAuthorizationId = authorization.Id;
        payment.AuthorizationState = authorization.State?.Value;
        payment.AuthorizedAt = DateTimeOffset.UtcNow;
        payment.AuthorizationExpiresAt = authorization.ExpiresAt;
        payment.FailureCode = authorization.FailureCode;
        payment.FailureMessage = authorization.FailureMessage;

        payment.Status = authorization.State == State1.Succeeded
                         || authorization.State == State1.Pending
            ? PaymentStatus.Authorized
            : PaymentStatus.PaymentFailed;
    }

    private FinixApiReference.Models.Enums.Currency ParseCurrency()
    {
        var value = GetCurrencyCode();
        if (FinixApiReference.Models.Enums.Currency.TryGetKnownValue(value, out var known))
        {
            return known;
        }

        return JsonSerializer.Deserialize<FinixApiReference.Models.Enums.Currency>(
                   JsonSerializer.Serialize(value))
               ?? throw new PaymentOperationException(
                   $"Unsupported Finix currency '{value}'.",
                   PaymentFailureKind.Validation);
    }

    private static Country ParseCountry(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "USA" : value.Trim().ToUpperInvariant();
        if (Country.TryGetKnownValue(normalized, out var known))
        {
            return known;
        }

        return JsonSerializer.Deserialize<Country>(JsonSerializer.Serialize(normalized))
               ?? Country.Usa;
    }

    private static FinixAddress ToFinixAddress(AddressDetails? address) =>
        new()
        {
            Line1 = FirstNonEmpty(address?.Line1, "1 Main Street"),
            City = FirstNonEmpty(address?.City, "San Francisco"),
            Region = FirstNonEmpty(address?.Region, "CA"),
            PostalCode = FirstNonEmpty(address?.PostalCode, "94105"),
            Country = ParseCountry(address?.Country)
        };

    private static string FirstNonEmpty(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string NormalizeCardNumber(string number) =>
        new(number.Where(char.IsDigit).ToArray());

    private static int NormalizeYear(int year) => year < 100 ? year + 2000 : year;

    private static string BuildEmail(string buyerId) =>
        buyerId.Contains('@', StringComparison.Ordinal)
            ? buyerId
            : $"{Guid.NewGuid():N}@example.com";

    private static void ValidateCard(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.Number))
        {
            throw new PaymentOperationException("Card number is required.", PaymentFailureKind.Validation);
        }

        var digits = NormalizeCardNumber(card.Number);
        if (digits.Length is < 12 or > 19)
        {
            throw new PaymentOperationException(
                "Card number must contain 12 to 19 digits.",
                PaymentFailureKind.Validation);
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentOperationException(
                "Expiration month must be between 1 and 12.",
                PaymentFailureKind.Validation);
        }

        var year = NormalizeYear(card.ExpirationYear);
        if (year < DateTimeOffset.UtcNow.Year
            || (year == DateTimeOffset.UtcNow.Year && card.ExpirationMonth < DateTimeOffset.UtcNow.Month))
        {
            throw new PaymentOperationException(
                "The card expiration date is in the past.",
                PaymentFailureKind.Validation);
        }
    }

    private void EnsureConfigured()
    {
        var options = _options.Value;
        if (string.IsNullOrWhiteSpace(options.Username)
            || string.IsNullOrWhiteSpace(options.Password)
            || string.IsNullOrWhiteSpace(options.MerchantId)
            || string.IsNullOrWhiteSpace(options.Currency))
        {
            throw new PaymentOperationException(
                "Finix is not configured. Set Finix:Username, Finix:Password, Finix:MerchantId and Finix:Currency " +
                "(the FINIX_USERNAME, FINIX_PASSWORD, FINIX_MERCHANT_ID and FINIX_CURRENCY environment variables).",
                PaymentFailureKind.Upstream);
        }
    }

    private string RequireMerchantId() =>
        string.IsNullOrWhiteSpace(_options.Value.MerchantId)
            ? throw new PaymentOperationException(
                "Finix:MerchantId is not configured.",
                PaymentFailureKind.Upstream)
            : _options.Value.MerchantId!;

    private string GetCurrencyCode() =>
        string.IsNullOrWhiteSpace(_options.Value.Currency)
            ? throw new PaymentOperationException(
                "Finix:Currency is not configured.",
                PaymentFailureKind.Upstream)
            : _options.Value.Currency!.Trim().ToUpperInvariant();

    private static PaymentOperationException ToOperationException(
        Exception exception,
        string operation)
    {
        var status = exception is ApiException api ? api.StatusCode : (HttpStatusCode?)null;
        var business = status is HttpStatusCode.BadRequest
            or HttpStatusCode.PaymentRequired
            or HttpStatusCode.NotFound
            or HttpStatusCode.Conflict
            or HttpStatusCode.UnprocessableEntity;

        return new PaymentOperationException(
            $"{operation} failed: {DescribeFinixError(exception)}",
            business ? PaymentFailureKind.Conflict : PaymentFailureKind.Upstream,
            null,
            exception);
    }

    private static string DescribeFinixError(Exception exception)
    {
        if (exception is not ApiException apiException)
        {
            return exception.Message;
        }

        try
        {
            var errorProperty = apiException.GetType().GetProperty(
                "Error",
                BindingFlags.Public | BindingFlags.Instance);
            var error = errorProperty?.GetValue(apiException);

            if (error is ApiError apiError && apiError.TryGetRawError(out var raw))
            {
                var body = raw.ReadAsString();
                var messages = ExtractMessages(body);
                if (!string.IsNullOrWhiteSpace(messages))
                {
                    return messages;
                }

                return Truncate(body);
            }

            if (error is not null)
            {
                var tryGeneric = error.GetType().GetMethod(
                    "TryGetGenericError",
                    BindingFlags.Public | BindingFlags.Instance);
                if (tryGeneric is not null)
                {
                    var arguments = new object?[] { null };
                    if ((bool)tryGeneric.Invoke(error, arguments)! && arguments[0] is not null)
                    {
                        var genericError = arguments[0]!;
                        var json = JsonSerializer.Serialize(genericError, genericError.GetType());
                        var messages = ExtractMessages(json);
                        if (!string.IsNullOrWhiteSpace(messages))
                        {
                            return messages;
                        }

                        return Truncate(json);
                    }
                }

                var serialized = JsonSerializer.Serialize(error, error.GetType());
                var fallbackMessages = ExtractMessages(serialized);
                if (!string.IsNullOrWhiteSpace(fallbackMessages))
                {
                    return fallbackMessages;
                }
            }
        }
        catch (Exception)
        {
            // Fall through to the exception message.
        }

        return apiException.Message;
    }

    private static string Truncate(string value) =>
        value.Length <= 1000 ? value : value[..1000];

    private static string? ExtractMessages(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var messages = new List<string>();

            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("_embedded", out var embedded)
                && embedded.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array)
            {
                foreach (var error in errors.EnumerateArray())
                {
                    var code = error.TryGetProperty("code", out var codeElement)
                        ? codeElement.GetString()
                        : null;
                    var message = error.TryGetProperty("message", out var messageElement)
                        ? messageElement.GetString()
                        : null;

                    if (!string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(message))
                    {
                        messages.Add(string.Join(": ", new[] { code, message }.Where(s => !string.IsNullOrWhiteSpace(s))));
                    }
                }
            }

            if (messages.Count > 0)
            {
                return string.Join("; ", messages);
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
