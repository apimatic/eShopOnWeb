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
    private readonly IPaymentGateway _paymentGateway;

    public SavedPaymentMethodService(IRepository<SavedPaymentMethod> repository, IPaymentGateway paymentGateway)
    {
        _repository = repository;
        _paymentGateway = paymentGateway;
    }

    public async Task<SavedPaymentMethod> SaveAsync(string buyerId, CardDetails card, CancellationToken cancellationToken)
    {
        var merchantCustomerId = ToMerchantCustomerId(buyerId);
        var vaulted = await _paymentGateway.VaultCardAsync(
            card,
            merchantCustomerId,
            $"eshop-vault-{buyerId}-{Guid.NewGuid():N}",
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.MerchantCustomerId ?? merchantCustomerId,
            vaulted.PayPalCustomerId,
            vaulted.LastDigits ?? LastDigitsFrom(card.Number),
            vaulted.Brand,
            vaulted.Expiry ?? card.Expiry,
            vaulted.CardholderName ?? card.Name);

        return await _repository.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken)
    {
        var method = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId), cancellationToken);
        if (method is null)
        {
            throw new PaymentException("Saved payment method was not found.", 404);
        }

        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(method.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            // Already gone at PayPal; still drop the local record.
        }

        await _repository.DeleteAsync(method, cancellationToken);
    }

    internal static string ToMerchantCustomerId(string buyerId)
    {
        if (string.IsNullOrEmpty(buyerId))
        {
            throw new PaymentException("A signed-in shopper is required.", 401);
        }

        return buyerId.Length <= 64 ? buyerId : buyerId[..64];
    }

    private static string LastDigitsFrom(string number)
    {
        var digits = number.Replace(" ", string.Empty);
        return digits.Length <= 4 ? digits : digits[^4..];
    }
}
