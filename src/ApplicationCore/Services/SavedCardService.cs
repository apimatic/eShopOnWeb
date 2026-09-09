using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _repository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedCard> repository, IPaymentGateway gateway, IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCard> SaveAsync(string buyerId, CardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var result = await _gateway.VaultCardAsync(new VaultCardRequest { BuyerReference = buyerId, Card = card }, ct);

        var savedCard = new SavedCard(
            buyerId,
            result.VaultId,
            result.CustomerId,
            result.Brand,
            result.LastDigits,
            result.Expiry,
            result.CardholderName ?? card.CardholderName);

        await _repository.AddAsync(savedCard, ct);
        _logger.LogInformation($"Saved card for {buyerId}: vault={result.VaultId} brand={result.Brand} last4={result.LastDigits}.");
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedCardsByBuyerSpec(buyerId), ct);
    }

    public async Task DeleteAsync(int savedCardId, string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var card = await _repository.FirstOrDefaultAsync(new SavedCardByIdSpec(savedCardId, buyerId), ct);
        if (card is null)
            throw new PaymentOperationException("Saved card was not found for this shopper.", PaymentOperationError.NotFound);

        try
        {
            await _gateway.DeleteVaultedCardAsync(card.PayPalVaultId, ct);
        }
        catch (PaymentGatewayException ex) when (ex.Kind == PaymentGatewayErrorKind.NotFound)
        {
            // Already gone at PayPal — removing our record still leaves the desired end state.
            _logger.LogWarning($"Vault token {card.PayPalVaultId} was already absent at PayPal; removing local record.");
        }

        await _repository.DeleteAsync(card, ct);
        _logger.LogInformation($"Deleted saved card {savedCardId} for {buyerId}.");
    }
}
