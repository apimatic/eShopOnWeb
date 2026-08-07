using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saves, lists and removes a shopper's reusable cards by vaulting them with PayPal and
/// persisting only the token reference plus a safe descriptor. Never stores or logs raw card data.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway payPalGateway,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _payPalGateway = payPalGateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardDetails card,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Group every card a shopper vaults under a single PayPal customer id: reuse the one from
        // an existing saved card, otherwise let PayPal generate one on the first vault.
        var existing = await _repository.ListAsync(
            new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        var existingCustomerId = existing.FirstOrDefault()?.PayPalCustomerId;

        // Deterministic key so a double-click vaulting the same card dedupes at PayPal.
        var idempotencyKey = IdempotencyKey.Derive("vault", buyerId, card.Number);

        var vaulted = await _payPalGateway.VaultCardAsync(
            card, existingCustomerId, idempotencyKey, cancellationToken);

        // Guard against a duplicate local row for the same PayPal token (e.g. a deduped retry).
        var already = existing.FirstOrDefault(pm => pm.PayPalVaultId == vaulted.VaultId);
        if (already is not null)
        {
            return already;
        }

        var customerId = string.IsNullOrEmpty(vaulted.CustomerId)
            ? existingCustomerId ?? Guid.NewGuid().ToString("N")[..20]
            : vaulted.CustomerId;

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            customerId,
            vaulted.CardBrand ?? "CARD",
            vaulted.LastDigits ?? string.Empty,
            vaulted.Expiry ?? string.Empty,
            vaulted.CardholderName ?? card.Name);

        saved = await _repository.AddAsync(saved, cancellationToken);

        _logger.LogInformation(
            "Saved card {0} for buyer (brand {1}, ending {2}).",
            saved.Id, saved.CardBrand ?? "CARD", saved.LastFourDigits ?? "????");

        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetCardsForBuyerAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var cards = await _repository.ListAsync(
            new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        return cards;
    }

    public async Task<bool> DeleteCardAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(buyerId, paymentMethodId), cancellationToken);

        if (saved is null)
        {
            return false;
        }

        // Remove the card from PayPal's vault first so it can no longer be charged, then drop the
        // local record. The gateway treats an already-absent token as success.
        await _payPalGateway.DeleteVaultedCardAsync(saved.PayPalVaultId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);

        _logger.LogInformation("Deleted saved card {0} for buyer.", paymentMethodId);

        return true;
    }
}
