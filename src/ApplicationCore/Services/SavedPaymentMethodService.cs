using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Manages a shopper's saved cards (Flow 2). The card lives in PayPal's vault; this service keeps
/// only the vault token and a safe summary, and enforces that every card is scoped to its owner.
/// </summary>
public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _gateway;

    public SavedPaymentMethodService(IRepository<SavedPaymentMethod> repository, IPaymentGateway gateway)
    {
        _repository = repository;
        _gateway = gateway;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Reuse the shopper's existing PayPal customer so all their cards vault under one identity.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        var existingCustomerId = existing.FirstOrDefault()?.PayPalCustomerId;

        var vaulted = await _gateway.VaultCardAsync(card, existingCustomerId, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.CustomerId,
            vaulted.Brand,
            vaulted.Last4,
            vaulted.Expiry,
            card.CardholderName);

        return await _repository.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdForBuyerSpec(buyerId, paymentMethodId), cancellationToken)
            ?? throw new PaymentMethodNotFoundException(paymentMethodId);

        await _gateway.DeleteVaultedCardAsync(saved.PayPalVaultId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);
    }
}
