using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saved cards: vaulted at the payment gateway, mirrored locally with safe display
/// data only. A saved card belongs to the shopper who saved it.
/// </summary>
public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IAppLogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPaymentGateway paymentGateway,
        IAppLogger<SavedPaymentMethodService> logger)
    {
        _repository = repository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string ownerId, GatewayCardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.Null(card, nameof(card));

        var existing = await ListAsync(ownerId, cancellationToken);

        // Idempotent in effect: saving the very same card twice returns the existing record.
        var last4 = card.Number.Length >= 4 ? card.Number[^4..] : card.Number;
        var duplicate = existing.FirstOrDefault(m =>
            m.Last4 == last4 && string.Equals(m.Expiry, card.Expiry, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            return duplicate;
        }

        // Link the new card to the shopper's existing gateway customer, if any.
        var customerId = existing.FirstOrDefault()?.PayPalCustomerId;
        var vaulted = await _paymentGateway.VaultCardAsync(card, customerId, $"eshop-vault-{Guid.NewGuid():N}", cancellationToken);

        var saved = new SavedPaymentMethod(ownerId, vaulted.CustomerId, vaulted.PaymentTokenId,
            vaulted.CardBrand, vaulted.Last4, vaulted.Expiry, vaulted.CardholderName);
        await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation($"Saved card {saved.Describe()} for shopper {ownerId} as payment method {saved.Id}.");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        var spec = new SavedPaymentMethodsByOwnerSpecification(ownerId);
        return await _repository.ListAsync(spec, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string ownerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        var saved = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (saved is null || saved.OwnerId != ownerId)
        {
            return false;
        }

        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(saved.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            // Already gone at the gateway: still remove the local record so it cannot be used.
            _logger.LogWarning($"Deleting vaulted card at gateway failed ({ex.Issue ?? ex.Message}); removing local record anyway.");
        }

        await _repository.DeleteAsync(saved, cancellationToken);
        return true;
    }
}
