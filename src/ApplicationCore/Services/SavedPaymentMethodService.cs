using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;

    public SavedPaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalGateway payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentSource card)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new CheckoutException(401, "A signed-in shopper is required to save a card.");
        }

        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId));
        var customerId = existing.Count > 0 ? existing[0].PayPalCustomerId : null;
        var idempotencyKey = $"eshop-vault-{buyerId}-{Guid.NewGuid():N}";

        var vaulted = await _payPal.VaultCardAsync(CardInputNormalizer.Normalize(card), customerId, idempotencyKey);
        var method = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.CustomerId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.CardholderName);

        return await _repository.AddAsync(method);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListForBuyerAsync(string buyerId)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId));
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId)
    {
        var method = await GetOwnedAsync(buyerId, paymentMethodId);
        try
        {
            await _payPal.DeleteVaultedCardAsync(method.PayPalPaymentTokenId);
        }
        catch (CheckoutException ex) when (ex.StatusCode == 404)
        {
            // Already removed at PayPal; still drop the local record.
        }

        await _repository.DeleteAsync(method);
    }

    public async Task<SavedPaymentMethod> GetOwnedAsync(string buyerId, int paymentMethodId)
    {
        var method = await _repository.GetByIdAsync(paymentMethodId);
        if (method == null || !string.Equals(method.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new CheckoutException(404, "Saved payment method was not found.");
        }

        return method;
    }
}
