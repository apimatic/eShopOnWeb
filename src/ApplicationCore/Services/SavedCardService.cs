using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _savedCards;
    private readonly IPayPalGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedPaymentMethod> savedCards,
        IPayPalGateway gateway,
        IAppLogger<SavedCardService> logger)
    {
        _savedCards = savedCards;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var vaulted = await _gateway.VaultCardAsync(new VaultCardRequest(card, buyerId), ct);

        var saved = new SavedPaymentMethod(
            buyerId, vaulted.VaultId, vaulted.Brand, vaulted.LastFourDigits, vaulted.Expiry, vaulted.CardHolderName);
        await _savedCards.AddAsync(saved, ct);

        _logger.LogInformation("Saved card {0} for buyer {1} ({2} ****{3}).", saved.Id, buyerId, vaulted.Brand, vaulted.LastFourDigits);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetCardsAsync(string buyerId, CancellationToken ct)
    {
        return await _savedCards.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var saved = await _savedCards.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId), ct);
        if (saved is null)
        {
            return false;
        }

        // Best-effort removal at PayPal. Regardless of the outcome there, we remove our own reference so the
        // card no longer appears for the shopper and can no longer be used to pay (pay resolves the vault id
        // only from the caller's own saved cards).
        try
        {
            await _gateway.DeleteVaultedCardAsync(saved.VaultId, ct);
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogWarning("PayPal delete of vault token {0} failed ({1}); removing local reference anyway.", saved.VaultId, ex.Message);
        }

        await _savedCards.DeleteAsync(saved, ct);
        _logger.LogInformation("Deleted saved card {0} for buyer {1}.", paymentMethodId, buyerId);
        return true;
    }
}
