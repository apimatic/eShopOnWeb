using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedCard> savedCardRepository, IPayPalGateway gateway,
        IAppLogger<SavedCardService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, SaveCardInput input, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (string.IsNullOrWhiteSpace(input.Number) || string.IsNullOrWhiteSpace(input.Expiry)
            || string.IsNullOrWhiteSpace(input.SecurityCode))
        {
            throw new PaymentStateException("Card number, expiry and security code are required.");
        }

        var command = new PayPalVaultCardCommand(input.Number, input.Expiry, input.SecurityCode, input.Name,
            MerchantCustomerId: buyerId, input.CountryCode, input.AddressLine1, input.AddressLine2,
            input.AdminArea1, input.AdminArea2, input.PostalCode);

        var vaulted = await _gateway.VaultCardAsync(command, ct);

        var card = new SavedCard(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.LastDigits,
            vaulted.Expiry, vaulted.CardholderName, vaulted.PayPalCustomerId);
        card = await _savedCardRepository.AddAsync(card, ct);

        _logger.LogInformation($"Shopper {buyerId} saved card {card.Id} (vault token {vaulted.VaultId}).");
        return ToView(card);
    }

    public async Task<IReadOnlyList<SavedCardView>> ListCardsAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var cards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), ct);
        return cards.OrderByDescending(c => c.Id).Select(ToView).ToList();
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var card = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedCardByIdAndBuyerSpecification(paymentMethodId, buyerId), ct);
        if (card is null)
            return false;

        // Remove at PayPal first so it can no longer be charged; then locally so it no longer lists.
        await _gateway.DeleteVaultedCardAsync(card.VaultId, ct);
        await _savedCardRepository.DeleteAsync(card, ct);

        _logger.LogInformation($"Shopper {buyerId} deleted saved card {paymentMethodId} (vault token {card.VaultId}).");
        return true;
    }

    private static SavedCardView ToView(SavedCard c) =>
        new(c.Id, c.Brand, c.LastDigits, c.Expiry, c.CardholderName);
}
