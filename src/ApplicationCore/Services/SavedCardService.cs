using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _gateway;

    public SavedCardService(IRepository<SavedPaymentMethod> repository, IPayPalGateway gateway)
    {
        _repository = repository;
        _gateway = gateway;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card, string? alias,
        CancellationToken cancellationToken = default)
    {
        // Group all of a shopper's saved cards under the same PayPal customer id.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        var existingCustomerId = existing.Select(c => c.PayPalCustomerId).FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var result = await _gateway.VaultCardAsync(
            card, existingCustomerId, merchantCustomerId: buyerId, requestId: Guid.NewGuid().ToString("N"), cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId: buyerId,
            payPalVaultTokenId: result.VaultTokenId,
            payPalCustomerId: result.CustomerId,
            cardBrand: result.CardBrand,
            cardLast4: result.CardLast4,
            cardExpiry: result.CardExpiry,
            alias: alias);

        await _repository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetCardsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(paymentMethodId, buyerId), cancellationToken)
            ?? throw new SavedPaymentMethodNotFoundException(paymentMethodId);

        try
        {
            await _gateway.DeleteVaultedCardAsync(saved.PayPalVaultTokenId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.PayPalHttpStatus == 404)
        {
            // Already gone from the vault — removing the local record still fulfils the intent.
        }

        await _repository.DeleteAsync(saved, cancellationToken);
    }
}
