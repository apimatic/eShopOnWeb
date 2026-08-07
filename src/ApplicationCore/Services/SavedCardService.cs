using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<PaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        IAppLogger<SavedCardService> logger)
    {
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(
        string buyerId,
        CardDetails card,
        string? alias,
        CancellationToken cancellationToken = default)
    {
        // Vault the card with PayPal; only the returned token id + safe descriptors are persisted here.
        var idempotencyKey = $"eshop-vault-{Guid.NewGuid():N}";
        var vaulted = await _paymentGateway.VaultCardAsync(card, buyerId, idempotencyKey, cancellationToken);

        var paymentMethod = new PaymentMethod(
            buyerId,
            vaulted.VaultTokenId,
            vaulted.Brand ?? string.Empty,
            vaulted.Last4 ?? string.Empty,
            vaulted.Expiry ?? string.Empty,
            vaulted.CardHolderName,
            alias);

        paymentMethod = await _paymentMethodRepository.AddAsync(paymentMethod, cancellationToken);

        _logger.LogInformation(
            $"Saved card {paymentMethod.Id} for buyer {buyerId} ({paymentMethod.CardBrand} ****{paymentMethod.Last4}).");
        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _paymentMethodRepository.ListAsync(
            new PaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var paymentMethod = await _paymentMethodRepository.FirstOrDefaultAsync(
            new PaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId), cancellationToken);
        if (paymentMethod is null)
        {
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }

        // Best-effort delete at PayPal. Even if that fails, removing the local record makes the card
        // disappear from the shopper's list and unusable to pay (payments resolve the token locally).
        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(paymentMethod.VaultTokenId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogWarning(
                $"Could not delete vault token for saved card {paymentMethodId} at PayPal ({ex.ErrorName}, debug_id: {ex.DebugId}); removing local record anyway.");
        }

        await _paymentMethodRepository.DeleteAsync(paymentMethod, cancellationToken);
        _logger.LogInformation($"Deleted saved card {paymentMethodId} for buyer {buyerId}.");
    }
}
