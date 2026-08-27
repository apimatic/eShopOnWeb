using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Vaults shoppers' cards with PayPal and keeps only safe display details locally.
/// Full card details are never stored and never logged.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedCard> savedCardRepository,
        IPayPalGateway payPalGateway,
        IAppLogger<SavedCardService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _payPalGateway = payPalGateway;
        _logger = logger;
    }

    /// <summary>
    /// Deterministic, PayPal-compatible customer id for a shopper
    /// (matches the vault API's ^[0-9a-zA-Z_-]+$ pattern, max 36 chars).
    /// </summary>
    public static string VaultCustomerId(string buyerId)
        => "eshop-" + ShortHash(buyerId.ToLowerInvariant());

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        // Hash-derived key: dedupes repeat saves without ever persisting the PAN.
        // Includes every payload field so a changed payload yields a different key.
        var idempotencyKey = "eshop-vault-" + ShortHash(string.Join("|", buyerId, card.Number, card.Expiry,
            card.CardholderName, card.BillingAddressLine1, card.BillingCity, card.BillingState,
            card.BillingPostalCode, card.BillingCountryCode));

        PayPalVaultedCardInfo vaulted;
        try
        {
            vaulted = await _payPalGateway.VaultCardAsync(card, VaultCustomerId(buyerId), idempotencyKey, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            throw new PaymentDeclinedException($"PayPal could not save the card: {ex.Message}");
        }

        var savedCard = new SavedCard(buyerId, vaulted.VaultTokenId, vaulted.Brand,
            vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        await _savedCardRepository.AddAsync(savedCard, cancellationToken);

        _logger.LogInformation($"Buyer {buyerId}: saved card token {vaulted.VaultTokenId} " +
            $"({vaulted.Brand} ending {vaulted.LastDigits}).");
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default)
    {
        var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId, cancellationToken);
        if (savedCard == null || savedCard.BuyerId != buyerId) return false;

        try
        {
            await _payPalGateway.DeleteVaultedCardAsync(savedCard.VaultTokenId, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.StatusCode == 404)
        {
            // Already gone from the vault; still remove it locally.
        }

        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);
        _logger.LogInformation($"Buyer {buyerId}: deleted saved card {savedCardId}.");
        return true;
    }

    private static string ShortHash(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var builder = new StringBuilder(16);
        for (var i = 0; i < 8; i++)
        {
            builder.Append(hash[i].ToString("x2"));
        }
        return builder.ToString();
    }
}
