using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<PaymentMethod> _repository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<PaymentMethod> repository, IPayPalPaymentGateway gateway,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string ownerId, CardDetails card, string? alias,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _gateway.VaultCardAsync(card, cancellationToken);
        var method = new PaymentMethod(ownerId, vaulted.VaultTokenId, vaulted.Brand, vaulted.Last4,
            vaulted.Expiry, vaulted.CardHolderName, alias);
        method = await _repository.AddAsync(method, cancellationToken);
        _logger.LogInformation($"Saved card {method.Id} for {ownerId} (token {vaulted.VaultTokenId}).");
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string ownerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        return await _repository.ListAsync(new PaymentMethodsByOwnerSpecification(ownerId), cancellationToken);
    }

    public async Task DeleteCardAsync(string ownerId, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        var method = await _repository.FirstOrDefaultAsync(
            new PaymentMethodByIdForOwnerSpecification(paymentMethodId, ownerId), cancellationToken);
        if (method is null)
        {
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }

        // Best-effort removal at PayPal; regardless, remove the local record so the card can no longer
        // be used to pay (pay looks the card up by id, scoped to the owner).
        try
        {
            await _gateway.DeleteVaultTokenAsync(method.VaultTokenId, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning($"PayPal vault-token delete failed for card {paymentMethodId} " +
                $"({ex.PayPalName}); removing local record anyway.");
        }

        await _repository.DeleteAsync(method, cancellationToken);
        _logger.LogInformation($"Deleted saved card {paymentMethodId} for {ownerId}.");
    }
}
