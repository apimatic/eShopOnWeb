using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalPaymentGateway _payPal;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalPaymentGateway payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<SavedPaymentMethod> SaveAsync(
        string buyerId,
        CardPaymentDetails card,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        var payPalCustomerId = existing
            .Select(m => m.PayPalCustomerId)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

        var requestId = $"eshop-vault-{buyerId}-{Guid.NewGuid():N}";
        var vaulted = await _payPal.SaveCardAsync(buyerId, payPalCustomerId, requestId, card, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.PayPalCustomerId ?? payPalCustomerId,
            vaulted.MerchantCustomerId ?? buyerId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry);

        return await _repository.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var method = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId),
            cancellationToken);
        if (method is null)
        {
            throw new EntityNotFoundException($"Saved payment method {paymentMethodId} was not found for this shopper.");
        }

        try
        {
            await _payPal.DeleteCardAsync(method.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PayPalProviderException ex) when (ex.StatusCode == 404)
        {
            // Already gone on PayPal; still remove the local record.
        }

        await _repository.DeleteAsync(method, cancellationToken);
    }
}
