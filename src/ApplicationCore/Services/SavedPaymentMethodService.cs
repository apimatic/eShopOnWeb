using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IAppLogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        IAppLogger<SavedPaymentMethodService> logger)
    {
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        GatewayVaultedCard vaulted;
        try
        {
            vaulted = await _paymentGateway.VaultCardAsync(card, customerId: buyerId,
                idempotencyKey: $"eshop-vault-{Guid.NewGuid():N}",
                cancellationToken: cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.HttpStatusCode >= 400 && ex.HttpStatusCode < 500)
        {
            _logger.LogWarning($"PayPal declined to vault a card for buyer {buyerId}: {ex.ErrorName} {ex.Message} (debug {ex.DebugId})");
            throw new PaymentDeclinedException($"PayPal could not save the card: {ex.Message}");
        }

        var saved = new SavedPaymentMethod(buyerId, vaulted.CustomerId ?? buyerId, vaulted.PaymentTokenId,
            vaulted.Brand, vaulted.LastFourDigits, vaulted.Expiry, vaulted.CardholderName);
        await _paymentMethodRepository.AddAsync(saved, cancellationToken);

        _logger.LogInformation($"Saved payment method {saved.Id} ({saved.Brand} ending {saved.LastFourDigits}) for buyer {buyerId}");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var saved = await _paymentMethodRepository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (saved is null || saved.BuyerId != buyerId)
        {
            throw new SavedPaymentMethodNotFoundException(paymentMethodId);
        }

        try
        {
            await _paymentGateway.DeletePaymentTokenAsync(saved.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.HttpStatusCode == 404)
        {
            // Already gone from PayPal's vault; still remove the local reference.
            _logger.LogWarning($"PayPal payment token for saved method {paymentMethodId} was already deleted.");
        }

        await _paymentMethodRepository.DeleteAsync(saved, cancellationToken);
        _logger.LogInformation($"Deleted saved payment method {paymentMethodId} for buyer {buyerId}");
    }
}
