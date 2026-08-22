using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalPaymentGateway _payPal;

    public SavedPaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalPaymentGateway payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<SavedPaymentMethod> SaveAsync(string buyerId, CardPaymentInput card, CancellationToken ct)
    {
        var requestId = $"eshop-vault-{buyerId}-{Guid.NewGuid():N}";
        var vaulted = await _payPal.SaveCardAsync(buyerId, card, requestId, ct);
        var method = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.CustomerId,
            vaulted.Brand,
            vaulted.LastDigits,
            vaulted.Expiry,
            vaulted.Name);

        return await _repository.AddAsync(method, ct);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken ct)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var method = await GetOwnedAsync(buyerId, paymentMethodId, ct)
            ?? throw new CheckoutException("Saved card was not found.", 404);

        try
        {
            await _payPal.DeletePaymentTokenAsync(method.PayPalPaymentTokenId, ct);
        }
        catch (CheckoutException ex) when (ex.StatusCode == 404)
        {
            // Already removed at PayPal; still drop our row so it cannot be used to pay.
        }

        await _repository.DeleteAsync(method, ct);
    }

    public async Task<SavedPaymentMethod?> GetOwnedAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var method = await _repository.GetByIdAsync(paymentMethodId, ct);
        if (method is null || method.BuyerId != buyerId)
        {
            return null;
        }

        return method;
    }
}
