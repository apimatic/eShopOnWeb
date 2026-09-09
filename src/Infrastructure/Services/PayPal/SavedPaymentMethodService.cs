using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Saves, lists and removes a shopper's cards. Cards are vaulted with PayPal; only PayPal's token
/// and safe descriptors are kept locally. Every operation is scoped to the buyer id.
/// </summary>
public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalClient _payPal;
    private readonly IAppLogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalClient payPal,
        IAppLogger<SavedPaymentMethodService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCard card, string? alias, CancellationToken cancellationToken = default)
    {
        var customerId = PayPalCustomerId.For(buyerId);
        var requestId = $"vault-{Guid.NewGuid():N}";

        var vaulted = await _payPal.VaultCardAsync(card, customerId, requestId, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.CustomerId,
            vaulted.VaultTokenId,
            vaulted.Brand,
            vaulted.Last4,
            vaulted.CardholderName,
            vaulted.Expiry,
            alias);

        saved = await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation($"Saved a {vaulted.Brand} card ending {vaulted.Last4} (id {saved.Id}) for the shopper.");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(int savedPaymentMethodId, string buyerId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdForBuyerSpecification(savedPaymentMethodId, buyerId), cancellationToken);
        if (saved is null)
        {
            return false;
        }

        // Best effort: remove from PayPal's vault. Even if that call fails, we remove the local record
        // so the card no longer appears and can no longer be used to pay.
        try
        {
            await _payPal.DeleteVaultedCardAsync(saved.PayPalVaultTokenId, cancellationToken);
        }
        catch (PayPalGatewayException ex)
        {
            _logger.LogWarning($"PayPal vault delete for token {saved.PayPalVaultTokenId} failed ({ex.PayPalErrorName}); removing local record anyway.");
        }

        await _repository.DeleteAsync(saved, cancellationToken);
        _logger.LogInformation($"Removed saved card {savedPaymentMethodId} for the shopper.");
        return true;
    }
}
