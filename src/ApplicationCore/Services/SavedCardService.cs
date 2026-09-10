using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _gateway;

    public SavedCardService(IRepository<SavedPaymentMethod> repository, IPaymentGateway gateway)
    {
        _repository = repository;
        _gateway = gateway;
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var saved = await _gateway.VaultCardAsync(ToCustomerReference(buyerId), card, ct);

        var entity = new SavedPaymentMethod(buyerId, saved.VaultId, saved.CardBrand, saved.LastFourDigits,
            saved.Expiry, saved.CardholderName);
        await _repository.AddAsync(entity, ct);

        return ToView(entity);
    }

    public async Task<IReadOnlyList<SavedCardView>> GetCardsAsync(string buyerId, CancellationToken ct)
    {
        var cards = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
        return cards.Select(ToView).ToList();
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var card = await _repository.GetByIdAsync(paymentMethodId, ct);
        if (card is null || card.BuyerId != buyerId)
        {
            return false; // not found, or owned by another shopper — indistinguishable to the caller.
        }

        // Remove from PayPal's vault first so it can no longer be used to pay, then drop the local record.
        await _gateway.DeleteVaultedCardAsync(card.PayPalVaultId, ct);
        await _repository.DeleteAsync(card, ct);
        return true;
    }

    public async Task<string?> GetVaultIdForCallerAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var card = await _repository.GetByIdAsync(paymentMethodId, ct);
        return card is not null && card.BuyerId == buyerId ? card.PayPalVaultId : null;
    }

    private static SavedCardView ToView(SavedPaymentMethod m) =>
        new(m.Id, m.CardBrand, m.LastFourDigits, m.Expiry, m.CardholderName, m.CreatedDate);

    /// <summary>Derive a PayPal-safe merchant customer reference from the shopper's identity.</summary>
    private static string ToCustomerReference(string buyerId)
    {
        var cleaned = Regex.Replace(buyerId, "[^0-9a-zA-Z_-]", "-");
        return cleaned.Length > 64 ? cleaned.Substring(0, 64) : cleaned;
    }
}
