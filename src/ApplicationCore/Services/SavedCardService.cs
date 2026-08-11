using System.Collections.Generic;
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
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IRepository<SavedPaymentMethod> _repository;

    public SavedCardService(IPayPalPaymentGateway gateway, IRepository<SavedPaymentMethod> repository)
    {
        _gateway = gateway;
        _repository = repository;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var vaulted = await _gateway.VaultCardAsync(card, cancellationToken);
        var saved = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.CardBrand, vaulted.Last4,
            vaulted.ExpiryMonth, vaulted.ExpiryYear, vaulted.CardholderName);
        return await _repository.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListCardsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsForBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdForBuyerSpecification(paymentMethodId, buyerId), cancellationToken);
        if (saved is null)
        {
            return false;
        }

        // Removing the local record makes the card unlistable and unusable to pay (a pay request can
        // only reference a saved card the caller still owns).
        await _repository.DeleteAsync(saved, cancellationToken);
        return true;
    }
}
