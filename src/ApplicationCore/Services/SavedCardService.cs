using System.Collections.Generic;
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

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, string? label, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _gateway.VaultCardAsync(buyerId, card, ct);
        var saved = new SavedCard(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.LastFourDigits,
            vaulted.Expiry, label);
        await _repository.AddAsync(saved, ct);

        _logger.LogInformation($"Saved card {saved.Id} for buyer (brand {vaulted.Brand}, ending {vaulted.LastFourDigits}).");
        return saved;
    }

    public async Task<IReadOnlyList<SavedCard>> GetCardsAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedCardsByBuyerSpec(buyerId), ct);
    }

    public async Task RemoveCardAsync(string buyerId, int savedCardId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var saved = await _repository.FirstOrDefaultAsync(new SavedCardByIdForBuyerSpec(savedCardId, buyerId), ct)
            ?? throw new PaymentNotFoundException($"Saved card {savedCardId} was not found.");

        await _gateway.DeleteVaultedCardAsync(saved.PayPalVaultId, ct);
        await _repository.DeleteAsync(saved, ct);

        _logger.LogInformation($"Removed saved card {savedCardId}.");
    }
}
