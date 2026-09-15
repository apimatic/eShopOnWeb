using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
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
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card == null || string.IsNullOrWhiteSpace(card.Number))
        {
            throw new PaymentException("Card details are required to save a payment method.");
        }

        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId));
        var paypalCustomerId = existing.FirstOrDefault(m => !string.IsNullOrEmpty(m.PayPalCustomerId))?.PayPalCustomerId;

        var vaulted = await _payPal.VaultCardAsync(card, paypalCustomerId, $"eshop-vault-{buyerId}-{System.Guid.NewGuid():N}");

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.Name,
            vaulted.CustomerId ?? paypalCustomerId);

        return await _repository.AddAsync(saved);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId));
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var method = await _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId));
        if (method == null)
        {
            throw new ResourceNotFoundException("Saved payment method was not found.");
        }

        try
        {
            await _payPal.DeletePaymentTokenAsync(method.PayPalPaymentTokenId);
        }
        catch (ResourceNotFoundException)
        {
            // Already removed from PayPal; still drop the local record.
        }

        await _repository.DeleteAsync(method);
    }
}
