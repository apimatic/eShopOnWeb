using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>Saves and removes a shopper's vaulted cards, always scoped to the owning shopper.</summary>
public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalPaymentService _payPal;

    public PaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalPaymentService payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentDetails card,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Vault the card at PayPal under this shopper as the merchant customer; use a fresh idempotency key
        // so a double-click saves the card once.
        var idempotencyKey = $"vault-{buyerId}-{Guid.NewGuid():N}";
        var vaulted = await _payPal.VaultCardAsync(card, buyerId, idempotencyKey, ct);

        var saved = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.CustomerId, vaulted.Brand,
            vaulted.LastFourDigits, vaulted.Expiry, vaulted.CardholderName ?? card.CardholderName);
        return await _repository.AddAsync(saved, ct);
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var saved = await _repository.FirstOrDefaultAsync(
            new PaymentMethodByIdSpecification(paymentMethodId, buyerId), ct)
            ?? throw new EntityNotFoundException($"Saved card {paymentMethodId} was not found for this shopper.");

        // Remove from PayPal's vault first so it can no longer be used to pay, then from our store.
        await _payPal.DeleteVaultedCardAsync(saved.VaultId, ct);
        await _repository.DeleteAsync(saved, ct);
    }
}
