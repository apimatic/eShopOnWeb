using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalClient _payPalClient;

    public PaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalClient payPalClient)
    {
        _repository = repository;
        _payPalClient = payPalClient;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Vault the card in PayPal — the raw card never lands in this app's database.
        var vaulted = await _payPalClient.VaultCardAsync(card, idempotencyKey: System.Guid.NewGuid().ToString());

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.CustomerId,
            vaulted.CardBrand,
            vaulted.LastFourDigits,
            vaulted.Expiry,
            vaulted.CardholderName);

        return await _repository.AddAsync(saved);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId)
    {
        var spec = new SavedPaymentMethodsByBuyerSpecification(buyerId);
        return await _repository.ListAsync(spec);
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId)
    {
        var saved = await _repository.GetByIdAsync(paymentMethodId);

        // Not found, or not this shopper's card — treat identically so ownership isn't revealed.
        if (saved is null || saved.BuyerId != buyerId)
            return false;

        // Remove from PayPal's vault so it can no longer be used to pay, then from this app.
        await _payPalClient.DeleteVaultedCardAsync(saved.PayPalVaultId);
        await _repository.DeleteAsync(saved);
        return true;
    }
}
