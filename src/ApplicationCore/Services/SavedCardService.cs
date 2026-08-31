using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalClient _payPalClient;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalClient payPalClient,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _payPalClient = payPalClient;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Reuse the PayPal customer id from the buyer's previously vaulted cards,
        // so all of a buyer's cards sit under one PayPal customer.
        var existing = await GetSavedCardsAsync(buyerId, cancellationToken);
        var payPalCustomerId = existing.FirstOrDefault()?.PayPalCustomerId;

        var requestId = $"eshop-vault-{buyerId.GetHashCode():x}-{Guid.NewGuid():N}";
        var setupToken = await _payPalClient.CreateSetupTokenAsync(card, payPalCustomerId, requestId, cancellationToken);

        if (string.Equals(setupToken.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                "PayPal requires the shopper to approve saving this card in a browser (3D Secure challenge); " +
                "this integration does not support approval round-trips.");
        }

        var vaulted = await _payPalClient.CreatePaymentTokenAsync(setupToken.SetupTokenId, requestId, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultTokenId,
            string.IsNullOrEmpty(vaulted.CustomerId) ? setupToken.CustomerId : vaulted.CustomerId,
            vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);

        await _repository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var spec = new SavedPaymentMethodsByBuyerSpec(buyerId);
        return await _repository.ListAsync(spec, cancellationToken);
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var saved = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (saved == null || saved.BuyerId != buyerId)
        {
            throw new PaymentException($"Saved payment method {paymentMethodId} was not found.");
        }

        try
        {
            await _payPalClient.DeletePaymentTokenAsync(saved.VaultTokenId, cancellationToken);
        }
        catch (Exception ex)
        {
            // The vault token may already be gone at PayPal; the local record is removed regardless.
            _logger.LogWarning($"Deleting PayPal vault token for payment method {paymentMethodId} failed: {ex.Message}");
        }

        await _repository.DeleteAsync(saved, cancellationToken);
    }
}
