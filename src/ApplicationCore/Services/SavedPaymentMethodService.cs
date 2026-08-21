using System;
using System.Collections.Generic;
using System.Threading;
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

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<SavedPaymentMethod> SaveAsync(
        string buyerId,
        CardPaymentInput card,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _payPal.SaveCardAsync(
            buyerId,
            CardInputNormalizer.Normalize(card),
            $"eshop-vault-{buyerId}-{Guid.NewGuid():N}",
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.TokenId,
            vaulted.CustomerId,
            vaulted.Brand,
            vaulted.LastDigits,
            vaulted.Expiry);

        return await _repository.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var saved = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (saved == null || saved.IsDeleted)
        {
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }

        if (!string.Equals(saved.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentMethodAccessDeniedException();
        }

        try
        {
            await _payPal.DeletePaymentTokenAsync(saved.PayPalTokenId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode is 404 or 400)
        {
            // Token already gone at PayPal; still drop it locally.
        }

        saved.MarkDeleted();
        await _repository.UpdateAsync(saved, cancellationToken);
    }
}
