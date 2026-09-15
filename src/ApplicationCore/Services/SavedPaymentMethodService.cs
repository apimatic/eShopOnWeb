using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
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
            throw new OrderPaymentException(401, "The caller is not authenticated.");
        }

        if (card == null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new OrderPaymentException(400, "Card number and expiry are required.");
        }

        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId));
        var customerId = existing.Count > 0 ? existing[0].PayPalCustomerId : null;

        var vaulted = await _payPal.VaultCardAsync(
            card,
            customerId,
            idempotencyKey: $"eshop-vault-{buyerId}-{Guid.NewGuid():N}");

        var lastDigits = string.IsNullOrWhiteSpace(vaulted.LastDigits) ? card.LastDigits : vaulted.LastDigits;
        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            lastDigits,
            vaulted.Brand,
            string.IsNullOrWhiteSpace(vaulted.Expiry) ? card.Expiry : vaulted.Expiry,
            vaulted.Name ?? card.Name,
            vaulted.CustomerId ?? customerId);

        await _repository.AddAsync(saved);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId));
    }

    public async Task DeleteAsync(int paymentMethodId, string buyerId)
    {
        var saved = await GetOwnedAsync(paymentMethodId, buyerId);
        await _payPal.DeleteVaultedCardAsync(saved.PayPalVaultId);
        await _repository.DeleteAsync(saved);
    }

    public async Task<SavedPaymentMethod> GetOwnedAsync(int paymentMethodId, string buyerId)
    {
        var saved = await _repository.GetByIdAsync(paymentMethodId);
        if (saved == null || !saved.BelongsTo(buyerId))
        {
            throw new OrderPaymentException(404, "The saved payment method was not found.");
        }

        return saved;
    }
}
