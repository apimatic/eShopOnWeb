using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _repository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedCard> repository, IPayPalPaymentGateway gateway,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // A unique request id per save; the PAN is never used to build it.
        var idempotencyKey = Guid.NewGuid().ToString("N");

        var vaulted = await _gateway.VaultCardAsync(card, idempotencyKey, cancellationToken);

        // If this vault id is already saved for the shopper (idempotent replay), return the existing row.
        var owned = await _repository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
        var existing = owned.FirstOrDefault(c => c.PayPalVaultId == vaulted.VaultId);
        if (existing is not null)
            return existing;

        var saved = new SavedCard(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.Last4, vaulted.Expiry,
            card.CardholderName);
        await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation("Saved card for buyer {0}: {1} ending {2}.", buyerId, vaulted.Brand, vaulted.Last4);
        return saved;
    }

    public async Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _repository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken)
    {
        var card = await _repository.FirstOrDefaultAsync(
            new SavedCardByIdAndBuyerSpecification(paymentMethodId, buyerId), cancellationToken);
        if (card is null)
            return false;

        try
        {
            await _gateway.DeleteVaultedCardAsync(card.PayPalVaultId, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.ProviderStatusCode == 404)
        {
            // Already gone at PayPal — still remove it locally so it can no longer be used.
            _logger.LogWarning("Vault token for card {0} already absent at PayPal; removing local record.", paymentMethodId);
        }

        await _repository.DeleteAsync(card, cancellationToken);
        _logger.LogInformation("Removed saved card {0} for buyer {1}.", paymentMethodId, buyerId);
        return true;
    }
}
