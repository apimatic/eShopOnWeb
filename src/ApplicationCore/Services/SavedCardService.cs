using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<PaymentMethod> _repository;
    private readonly IPayPalClient _payPal;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<PaymentMethod> repository, IPayPalClient payPal,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, PayPalCard card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _payPal.VaultCardAsync(card, customerId: null, Guid.NewGuid().ToString("N"), ct);

        var method = new PaymentMethod(buyerId, vaulted.VaultId, vaulted.CustomerId,
            vaulted.Brand, vaulted.LastFour, vaulted.Expiry, vaulted.Name);
        await _repository.AddAsync(method, ct);
        _logger.LogInformation("Shopper {BuyerId} saved card {Brand} ****{Last4} (vault {VaultId}).",
            buyerId, vaulted.Brand, vaulted.LastFour, vaulted.VaultId);
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListCardsAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new PaymentMethodsByBuyerSpec(buyerId), ct);
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var method = await _repository.FirstOrDefaultAsync(
            new PaymentMethodByIdForBuyerSpec(paymentMethodId, buyerId), ct);
        if (method is null)
            throw new PaymentOperationException($"Saved card {paymentMethodId} was not found for this shopper.", 404);

        // Best-effort removal from PayPal's vault; the local record is removed regardless so the
        // card can no longer be seen or used to pay through this app.
        try
        {
            await _payPal.DeleteVaultedCardAsync(method.PayPalVaultId, ct);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("PayPal vault delete for card {PaymentMethodId} (vault {VaultId}) failed: {Message}. " +
                "Removing local record anyway.", paymentMethodId, method.PayPalVaultId, ex.Message);
        }

        await _repository.DeleteAsync(method, ct);
        _logger.LogInformation("Shopper {BuyerId} deleted saved card {PaymentMethodId}.", buyerId, paymentMethodId);
    }
}
