using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedCard> savedCardRepository, IPaymentGateway gateway,
        IAppLogger<SavedCardService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, string? alias,
        CancellationToken cancellationToken = default)
    {
        var vaulted = await _gateway.VaultCardAsync(card, cancellationToken);
        var savedCard = new SavedCard(buyerId, vaulted.VaultTokenId, vaulted.Brand, vaulted.LastDigits,
            vaulted.Expiry, alias);
        await _savedCardRepository.AddAsync(savedCard, cancellationToken);

        _logger.LogInformation($"Vaulted a card for {buyerId}: token={vaulted.VaultTokenId} brand={vaulted.Brand} last4={vaulted.LastDigits}");
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> GetCardsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedCardByIdSpecification(paymentMethodId, buyerId), cancellationToken)
            ?? throw new PaymentMethodNotFoundException(paymentMethodId);

        // Remove our record first so the card is immediately unusable to pay, then release the vault
        // token. A best-effort vault delete keeps us from leaving the card usable if PayPal hiccups.
        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);
        try
        {
            await _gateway.DeleteVaultedCardAsync(savedCard.VaultTokenId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogWarning($"Removed saved card {paymentMethodId} locally but the PayPal vault delete failed: {ex.Message}");
        }
    }
}
