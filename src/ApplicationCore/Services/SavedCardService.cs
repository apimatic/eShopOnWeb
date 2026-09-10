using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;
    private readonly string _currency;

    public SavedCardService(IRepository<SavedCard> savedCardRepository, IPayPalGateway gateway,
        IAppLogger<SavedCardService> logger, IPayPalCurrencyProvider currencyProvider)
    {
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _logger = logger;
        _currency = currencyProvider.Currency;
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Reuse the shopper's existing PayPal customer id so all their cards belong to one customer.
        var existing = await _savedCardRepository.FirstOrDefaultAsync(new FirstSavedCardByBuyerSpec(buyerId), ct);
        var existingCustomerId = existing?.PayPalCustomerId;

        var vaulted = await _gateway.VaultCardAsync(existingCustomerId, SanitizeCustomerId(buyerId), card, _currency, ct);

        var savedCard = new SavedCard(buyerId, vaulted.VaultId, vaulted.CustomerId ?? existingCustomerId,
            vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        savedCard = await _savedCardRepository.AddAsync(savedCard, ct);

        _logger.LogInformation("Saved card {0} for buyer ({1} ****{2})", savedCard.Id, vaulted.Brand,
            vaulted.LastDigits);
        return ToView(savedCard);
    }

    public async Task<IReadOnlyList<SavedCardView>> GetCardsAsync(string buyerId, CancellationToken ct)
    {
        var cards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpec(buyerId), ct);
        return cards.Select(ToView).ToList();
    }

    public async Task DeleteCardAsync(string buyerId, int savedCardId, CancellationToken ct)
    {
        var card = await _savedCardRepository.GetByIdAsync(savedCardId, ct);
        if (card is null || card.BuyerId != buyerId)
            throw new PaymentNotFoundException($"Saved card {savedCardId} was not found.");

        await _gateway.DeleteVaultedCardAsync(card.PayPalVaultId, ct);
        await _savedCardRepository.DeleteAsync(card, ct);
        _logger.LogInformation("Deleted saved card {0} for buyer", savedCardId);
    }

    private static SavedCardView ToView(SavedCard c)
        => new(c.Id, c.Brand, c.LastDigits, c.Expiry, c.CardholderName, c.CreatedAt);

    /// <summary>Keeps only characters PayPal's merchant_customer_id allows, capped at 64.</summary>
    private static string SanitizeCustomerId(string buyerId)
    {
        var sb = new StringBuilder(buyerId.Length);
        foreach (var ch in buyerId)
        {
            if (char.IsLetterOrDigit(ch) || "-_.^*$@#".IndexOf(ch) >= 0)
                sb.Append(ch);
        }
        var result = sb.Length == 0 ? "eshop-customer" : sb.ToString();
        return result.Length > 64 ? result.Substring(0, 64) : result;
    }
}
