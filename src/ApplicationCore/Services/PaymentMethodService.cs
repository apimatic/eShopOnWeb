using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the saved-card flow on the PayPal Vault. The application's own database keeps only a
/// safe descriptor and the vault id; the card number and security code are never persisted here.
/// Every card is owned by the shopper who saved it and is only ever queried/removed under that owner.
/// </summary>
public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalVault _vault;

    public PaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalVault vault)
    {
        _repository = repository;
        _vault = vault;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Reuse a stable PayPal customer id per shopper so their vaulted cards are grouped together.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        var customerId = existing.Select(e => e.PayPalCustomerId).FirstOrDefault() ?? NewCustomerId();

        // A double-click on the same card dedupes at PayPal via a deterministic request id.
        var requestId = $"eshop-vault-{customerId}-{card.Last4}-{card.Expiry}".Replace(":", "-");
        var saved = await _vault.VaultCardAsync(customerId, card, requestId, cancellationToken);

        var method = new SavedPaymentMethod(
            buyerId,
            saved.VaultId,
            saved.CustomerId,
            saved.Brand,
            saved.Last4 ?? card.Last4,
            saved.Expiry ?? card.Expiry,
            saved.CardHolderName ?? card.Name);

        method = await _repository.AddAsync(method, cancellationToken);
        return method;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetCardsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteCardAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var method = (await _repository.ListAsync(new SavedPaymentMethodByIdSpec(paymentMethodId, buyerId), cancellationToken)).FirstOrDefault();
        if (method is null)
        {
            // Owner-scoped: never reveal that another shopper's card exists.
            throw new PaymentResourceNotFoundException($"Saved card {paymentMethodId} was not found for this shopper.");
        }

        // Remove from PayPal first so a deleted card can no longer be used to pay; then drop our record.
        await _vault.DeleteVaultedCardAsync(method.PayPalVaultId, cancellationToken);
        await _repository.DeleteAsync(method, cancellationToken);
    }

    /// <summary>A 22-char request-id-safe customer id (PayPal merchant_partner_customer_id pattern).</summary>
    private static string NewCustomerId() => Guid.NewGuid().ToString("N").Substring(0, 22);
}
