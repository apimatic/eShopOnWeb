using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _paymentGateway;

    public PaymentMethodService(IRepository<SavedPaymentMethod> repository, IPaymentGateway paymentGateway)
    {
        _repository = repository;
        _paymentGateway = paymentGateway;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Reuse this shopper's existing gateway customer so all their cards group under one customer.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        var existingCustomerId = existing.FirstOrDefault(pm => pm.VaultCustomerId != null)?.VaultCustomerId;

        // Deterministic idempotency key so a double-click saves the card only once at the gateway.
        var idempotencyKey = IdempotencyKeys.SaveCard(buyerId, card);

        var vaulted = await _paymentGateway.SaveCardAsync(card, existingCustomerId, idempotencyKey, cancellationToken);

        // App-level idempotency: if the gateway returned a token we already stored, reuse the existing row.
        var alreadyStored = existing.FirstOrDefault(pm => pm.VaultId == vaulted.VaultId);
        if (alreadyStored != null) return alreadyStored;

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.CustomerId,
            vaulted.Brand,
            vaulted.Last4,
            vaulted.ExpiryMonth,
            vaulted.ExpiryYear,
            vaulted.CardholderName);

        return await _repository.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var methods = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        return methods;
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Ownership is enforced by the specification: another shopper's card is simply not found.
        var method = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByBuyerAndIdSpecification(buyerId, paymentMethodId), cancellationToken);
        if (method is null) return false;

        // Remove from the gateway vault first so the card can no longer be charged, then from our store.
        await _paymentGateway.RemoveVaultedCardAsync(method.VaultId, cancellationToken);
        await _repository.DeleteAsync(method, cancellationToken);
        return true;
    }
}
