using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saves, lists and removes a shopper's reusable cards. The card is vaulted with PayPal; this app keeps
/// only the vault token and safe metadata. Every operation is scoped to the calling shopper.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway payPal,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default)
    {
        // Group all of this shopper's saved cards under one PayPal customer id.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
        var customerId = existing.FirstOrDefault()?.PayPalCustomerId;

        var vaulted = await _payPal.VaultCardAsync(card, customerId, requestId: Guid.NewGuid().ToString("N"), ct);

        var saved = new SavedPaymentMethod(
            buyerId, vaulted.VaultId, vaulted.CustomerId,
            vaulted.Brand, vaulted.Last4, vaulted.Expiry, vaulted.CardholderName ?? card.Name);

        saved = await _repository.AddAsync(saved, ct);
        _logger.LogInformation($"Saved card {saved.Id} ({saved.Brand} ****{saved.Last4}) for {buyerId}.");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(paymentMethodId, buyerId), ct);
        if (saved is null)
            return false;

        // Remove from PayPal's vault first so it can no longer be charged, then from our store.
        await _payPal.DeleteVaultedCardAsync(saved.VaultId, ct);
        await _repository.DeleteAsync(saved, ct);
        _logger.LogInformation($"Removed saved card {paymentMethodId} for {buyerId}.");
        return true;
    }
}
