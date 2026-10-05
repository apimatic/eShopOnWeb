using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record SaveCardResult(PaymentMethod PaymentMethod, bool AlreadySaved);

public sealed record RemoveCardResult(PaymentMethod PaymentMethod, bool ProviderDeletionPending);

/// <summary>
/// Saved cards: the card is vaulted at the processor and only its token plus display details are
/// stored here, scoped to the buyer who saved it.
/// </summary>
public class SavedCardService
{
    private static readonly Regex IdempotencyKeyPattern = new("^[A-Za-z0-9_.:-]{1,64}$", RegexOptions.Compiled);

    private readonly IRepository<PaymentMethod> _paymentMethods;
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentClaimStore _claims;
    private readonly TimeProvider _clock;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<PaymentMethod> paymentMethods,
        IPaymentGateway gateway,
        IPaymentClaimStore claims,
        TimeProvider clock,
        IAppLogger<SavedCardService> logger)
    {
        _paymentMethods = paymentMethods;
        _gateway = gateway;
        _claims = claims;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SaveCardResult> SaveAsync(string buyerId, CardDetails card, string? alias, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        CardValidator.Validate(card, _clock.GetUtcNow());
        if (alias is { Length: > 100 })
            throw new PaymentValidationException("Alias must be at most 100 characters.");
        if (idempotencyKey is not null && !IdempotencyKeyPattern.IsMatch(idempotencyKey))
            throw new PaymentValidationException("idempotencyKey must be 1-64 characters: letters, digits, '_', '-', '.', ':'.");

        PaymentClaimLease? claim = null;
        if (idempotencyKey is not null)
        {
            claim = await _claims.TryAcquireAsync($"card:{buyerId}:{idempotencyKey}", TimeSpan.MaxValue, cancellationToken);
            if (claim is null)
            {
                var existing = await _paymentMethods.FirstOrDefaultAsync(new PaymentMethodBySaveKeySpec(buyerId, idempotencyKey), cancellationToken)
                    ?? throw new PaymentConflictException($"A card is already being saved under idempotency key '{idempotencyKey}'. Retry shortly.", "OPERATION_IN_PROGRESS");
                if (existing.IsRemoved)
                    throw new PaymentConflictException($"The card saved under idempotency key '{idempotencyKey}' has since been removed; use a new key.", "IDEMPOTENCY_KEY_REUSED");
                return new SaveCardResult(existing, AlreadySaved: true);
            }
        }

        try
        {
            // Group all of a buyer's cards under one PayPal customer.
            var previous = await _paymentMethods.ListAsync(new PaymentMethodsForBuyerSpec(buyerId, includeRemoved: true), cancellationToken);
            var customerId = previous.Select(m => m.ProviderCustomerId).FirstOrDefault(id => !string.IsNullOrEmpty(id));

            var saved = await _gateway.SaveCardAsync(card, customerId, $"eshop-card-{Guid.NewGuid():N}", cancellationToken);

            var method = new PaymentMethod(
                buyerId,
                saved.PaymentTokenId,
                saved.ProviderCustomerId ?? customerId,
                saved.Brand,
                saved.LastDigits ?? card.LastDigits,
                saved.Expiry ?? card.Expiry,
                alias,
                idempotencyKey,
                _clock.GetUtcNow());
            await _paymentMethods.AddAsync(method, cancellationToken);
            _logger.LogInformation("Saved card {PaymentMethodId} (PayPal token {TokenId}, {Brand} ending {Last4}).",
                method.Id, saved.PaymentTokenId, method.Brand ?? "card", method.Last4 ?? "");
            return new SaveCardResult(method, AlreadySaved: false);
        }
        catch
        {
            // Nothing was stored for this key; let the caller retry under it.
            if (claim is not null)
                await _claims.ReleaseAsync(claim, CancellationToken.None);
            throw;
        }
    }

    public Task<List<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default) =>
        _paymentMethods.ListAsync(new PaymentMethodsForBuyerSpec(buyerId), cancellationToken);

    /// <summary>
    /// Removes the card for the buyer first (it can no longer be listed or used to pay), then deletes the
    /// token at the processor. If the processor is unreachable the removal still stands and a repeated
    /// DELETE retries the processor deletion.
    /// </summary>
    public async Task<RemoveCardResult> RemoveAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var method = await _paymentMethods.FirstOrDefaultAsync(new PaymentMethodForBuyerSpec(paymentMethodId, buyerId), cancellationToken);
        if (method is null || (method.IsRemoved && !method.ProviderDeletionPending))
            throw new PaymentResourceNotFoundException($"Saved card {paymentMethodId} was not found.");

        method.Remove(_clock.GetUtcNow());
        await _paymentMethods.UpdateAsync(method, cancellationToken);

        try
        {
            await _gateway.DeleteSavedCardAsync(method.CardId!, cancellationToken);
            method.ConfirmProviderDeletion();
            await _paymentMethods.UpdateAsync(method, cancellationToken);
            _logger.LogInformation("Removed saved card {PaymentMethodId} and its PayPal token.", method.Id);
        }
        catch (PaymentProviderException ex)
        {
            _logger.LogWarning("Saved card {PaymentMethodId} removed locally, but deleting PayPal token failed: {Message} (debug_id {DebugId}). A repeated DELETE retries it.",
                method.Id, ex.Message, ex.DebugId ?? "");
        }

        return new RemoveCardResult(method, method.ProviderDeletionPending);
    }
}
