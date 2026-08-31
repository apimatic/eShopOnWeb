using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedCard> savedCardRepository,
        IPaymentGateway gateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, GatewayCardDetails card, CancellationToken cancellationToken = default)
    {
        var vaulted = await _gateway.CreatePaymentTokenAsync(
            VaultCustomerId(buyerId),
            card,
            idempotencyKey: $"eshop-vault-{Guid.NewGuid():N}",
            cancellationToken);

        var savedCard = new SavedCard(buyerId, vaulted.VaultTokenId, vaulted.CardholderName, vaulted.Brand, vaulted.LastDigits, vaulted.Expiry);
        await _savedCardRepository.AddAsync(savedCard, cancellationToken);
        _logger.LogInformation($"Saved card {savedCard.Id} ({vaulted.Brand} ending {vaulted.LastDigits}) vaulted for shopper.");
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerIdSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteSavedCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default)
    {
        var savedCard = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpecification(savedCardId), cancellationToken);
        if (savedCard == null || savedCard.BuyerId != buyerId)
        {
            return false;
        }

        try
        {
            await _gateway.DeletePaymentTokenAsync(savedCard.VaultTokenId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            // A token PayPal no longer knows about must not block local removal.
            _logger.LogWarning($"Vault token deletion at the processor failed for saved card {savedCard.Id}: {ex.Message}");
        }

        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);
        return true;
    }

    /// <summary>
    /// Deterministic processor customer id for a shopper. Matches the vault API's
    /// customer id pattern (^[0-9a-zA-Z_-]+$, max 22 chars) without exposing the username.
    /// </summary>
    internal static string VaultCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        return "c" + Convert.ToHexString(hash).ToLowerInvariant()[..20];
    }
}
