using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedCard> savedCardRepository,
        IPayPalGateway payPal,
        IAppLogger<PaymentMethodService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var request = new VaultCardRequest(card, MerchantCustomerId(buyerId), Guid.NewGuid().ToString("N"));
        var vaulted = await _payPal.VaultCardAsync(request, cancellationToken);

        var savedCard = new SavedCard(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.Last4, vaulted.Expiry, card.CardholderName);
        await _savedCardRepository.AddAsync(savedCard, cancellationToken);

        _logger.LogInformation("Saved card for buyer {0}: vault token {1} ({2} ****{3}).",
            buyerId, vaulted.VaultId, vaulted.Brand, vaulted.Last4);
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var cards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
        return cards;
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var card = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedCardByIdForBuyerSpecification(savedCardId, buyerId), cancellationToken);
        if (card is null)
        {
            return false;
        }

        // Delete the vault token at PayPal first so it can no longer be used to pay anywhere.
        try
        {
            await _payPal.DeleteVaultedCardAsync(card.PayPalVaultId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone at PayPal — proceed to remove the local reference.
            _logger.LogWarning("Vault token {0} for buyer {1} was already absent at PayPal.", card.PayPalVaultId, buyerId);
        }

        await _savedCardRepository.DeleteAsync(card, cancellationToken);
        _logger.LogInformation("Deleted saved card {0} (vault token {1}) for buyer {2}.", savedCardId, card.PayPalVaultId, buyerId);
        return true;
    }

    // A stable, safe per-buyer customer reference for PayPal (<= 64 chars, no PII in the clear).
    private static string MerchantCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"eshop-{hex[..24]}";
    }
}
