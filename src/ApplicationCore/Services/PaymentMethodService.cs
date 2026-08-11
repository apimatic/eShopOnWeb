using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>Saves, lists and removes a shopper's cards, backed by the PayPal vault. Always scoped to the caller.</summary>
public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalClient _payPal;

    public PaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalClient payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<SavedPaymentMethod> SaveAsync(string buyerId, CardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        CardValidation.Validate(card);

        var vaulted = await _payPal.VaultCardAsync(card, Guid.NewGuid().ToString(), ct);

        var method = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.CustomerId,
            vaulted.Brand,
            vaulted.LastDigits,
            vaulted.Expiry,
            vaulted.CardholderName ?? card.CardholderName);

        return await _repository.AddAsync(method, ct);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var method = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(buyerId, paymentMethodId), ct);
        if (method is null)
            throw new PaymentMethodNotFoundException(paymentMethodId);

        // Delete the token at PayPal first so it can no longer be charged; tolerant of an already-gone token.
        await _payPal.DeleteVaultedCardAsync(method.VaultId, ct);
        await _repository.DeleteAsync(method, ct);
    }
}
