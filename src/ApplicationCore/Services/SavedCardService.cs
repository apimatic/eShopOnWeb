using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedPaymentMethod> savedCardRepository,
        IPaymentGateway paymentGateway,
        IAppLogger<SavedCardService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _paymentGateway.VaultCardAsync(card, Guid.NewGuid().ToString("N"), cancellationToken);

        var savedCard = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.LastFourDigits,
            vaulted.Expiry, card.CardholderName);
        await _savedCardRepository.AddAsync(savedCard, cancellationToken);

        _logger.LogInformation($"Shopper {buyerId} saved card ending {vaulted.LastFourDigits} ({vaulted.Brand}).");
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListCardsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var cards = await _savedCardRepository.ListAsync(
            new CustomerSavedPaymentMethodsSpecification(buyerId), cancellationToken);
        return cards;
    }

    public async Task<Result> DeleteCardAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        // Scoped to the owner: another shopper's card simply isn't found.
        var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId), cancellationToken);
        if (savedCard is null)
        {
            return Result.NotFound($"Saved card {paymentMethodId} was not found.");
        }

        // Best-effort removal from the provider's vault. Removing our record is what guarantees the
        // card can no longer be used to pay (pay-with-saved-card resolves the vault id from here), so
        // a provider hiccup on delete must not leave the card usable — we still remove it locally.
        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(savedCard.VaultId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                $"Provider vault delete failed for card {paymentMethodId}; removing local record anyway. {ex.Message}");
        }

        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);
        _logger.LogInformation($"Shopper {buyerId} removed saved card {paymentMethodId}.");
        return Result.Success();
    }
}
