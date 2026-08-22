using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payment;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalPaymentGateway _payPal;

    public PaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalPaymentGateway payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<SavedPaymentMethod> SaveAsync(
        string buyerId,
        CardPaymentInput card,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new PaymentException("A signed-in shopper is required.", 401);

        var vaulted = await _payPal.SaveCardAsync(
            buyerId,
            card,
            $"eshop-vault-{buyerId}-{Guid.NewGuid():N}",
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PayPalPaymentTokenId,
            vaulted.Brand,
            vaulted.LastDigits,
            vaulted.Expiry,
            vaulted.CardholderName);
        await _repository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId), cancellationToken);
        if (saved is null)
            throw new PaymentException("Saved payment method was not found.", 404);

        try
        {
            await _payPal.DeleteSavedCardAsync(saved.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode is 404)
        {
            // Already gone at PayPal; still drop the local record.
        }

        await _repository.DeleteAsync(saved, cancellationToken);
    }
}
