using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _gateway;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPaymentGateway gateway)
    {
        _repository = repository;
        _gateway = gateway;
    }

    public async Task<SavedPaymentMethod> SaveAsync(string buyerId, CardPaymentDetails card, CancellationToken ct)
    {
        if (card == null)
        {
            throw new PaymentException(400, "Card details are required to save a payment method.");
        }

        var merchantCustomerId = SanitizeMerchantCustomerId(buyerId);
        var result = await _gateway.VaultCardAsync(
            merchantCustomerId,
            card,
            $"eshop-vault-{Guid.NewGuid():N}",
            ct);

        if (result.RequiresPayerAction)
        {
            throw new PaymentException(409,
                "PayPal required a shopper approval challenge while saving the card. This integration does not implement a browser round-trip.");
        }

        var saved = new SavedPaymentMethod(
            buyerId,
            result.VaultId,
            result.PayPalCustomerId,
            result.MerchantCustomerId ?? merchantCustomerId,
            result.LastDigits,
            result.Brand,
            result.Expiry,
            result.CardholderName);

        return await _repository.AddAsync(saved, ct);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken ct)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var method = await GetOwnedAsync(buyerId, paymentMethodId, ct);
        if (method == null)
        {
            throw new PaymentException(404, "Saved payment method was not found.");
        }

        try
        {
            await _gateway.DeleteVaultedCardAsync(method.VaultId, ct);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            // Already gone at PayPal; still drop the local record.
        }

        await _repository.DeleteAsync(method, ct);
    }

    public Task<SavedPaymentMethod?> GetOwnedAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        return _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId), ct);
    }

    private static string SanitizeMerchantCustomerId(string buyerId)
    {
        var cleaned = new char[Math.Min(buyerId.Length, 64)];
        var n = 0;
        foreach (var c in buyerId)
        {
            if (char.IsLetterOrDigit(c) || "-_.^*$@#".IndexOf(c) >= 0)
            {
                cleaned[n++] = c;
                if (n == 64)
                {
                    break;
                }
            }
        }

        return n == 0 ? $"shopper-{Math.Abs(buyerId.GetHashCode())}" : new string(cleaned, 0, n);
    }
}
