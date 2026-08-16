using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPaymentGateway gateway,
        IAppLogger<SavedPaymentMethodService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PaymentCard card,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vault = await _gateway.VaultCardAsync(card, buyerId, cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, vault.VaultId, vault.Brand, vault.LastFourDigits,
            vault.Expiry, vault.CardholderName ?? card.CardholderName);
        saved = await _repository.AddAsync(saved, cancellationToken);

        _logger.LogInformation($"Saved card {saved.Id} for {buyerId} (vault {vault.VaultId}, {vault.Brand} ****{vault.LastFourDigits}).");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(paymentMethodId, buyerId), cancellationToken);
        if (saved is null)
        {
            return false;
        }

        try
        {
            await _gateway.DeleteVaultedCardAsync(saved.PayPalVaultId, cancellationToken);
        }
        catch (Exception ex)
        {
            // Even if the vault token is already gone or the delete call fails, remove the local
            // record so the card no longer appears among the shopper's cards and can no longer be
            // used to pay through this app.
            _logger.LogWarning($"Vault delete for card {paymentMethodId} (vault {saved.PayPalVaultId}) failed: {ex.Message}. Removing local record anyway.");
        }

        await _repository.DeleteAsync(saved, cancellationToken);
        _logger.LogInformation($"Deleted saved card {paymentMethodId} for {buyerId}.");
        return true;
    }
}
