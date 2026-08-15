using System.Collections.Generic;
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
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _gateway;

    public SavedCardService(IRepository<SavedPaymentMethod> repository, IPaymentGateway gateway)
    {
        _repository = repository;
        _gateway = gateway;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _gateway.VaultCardAsync(card, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.CardBrand,
            vaulted.Last4,
            vaulted.ExpiryMonth,
            vaulted.ExpiryYear,
            vaulted.CardholderName);

        return await _repository.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var saved = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (saved is null || saved.BuyerId != buyerId)
        {
            throw new PaymentNotFoundException($"Saved card {paymentMethodId} was not found.");
        }

        // Forget the card in the vault first, then locally. A vault-side miss is tolerated.
        await _gateway.DeleteVaultedCardAsync(saved.VaultId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);
    }
}
