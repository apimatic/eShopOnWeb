using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly ICardVault _cardVault;
    private readonly IAppLogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        ICardVault cardVault,
        IAppLogger<SavedPaymentMethodService> logger)
    {
        _repository = repository;
        _cardVault = cardVault;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        // Attach to the same provider customer record when the shopper has vaulted before.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
        var payPalCustomerId = existing.FirstOrDefault(m => m.PayPalCustomerId != null)?.PayPalCustomerId;

        var requestKey = $"eshop-vault-{Guid.NewGuid():N}";
        var result = await _cardVault.VaultCardAsync(card, buyerId, payPalCustomerId, requestKey, ct);

        var method = new SavedPaymentMethod(
            buyerId,
            result.VaultPaymentTokenId,
            result.PayPalCustomerId ?? payPalCustomerId,
            result.Brand,
            result.LastDigits,
            result.Expiry);
        await _repository.AddAsync(method, ct);

        _logger.LogInformation("Saved payment method {paymentMethodId} for buyer (brand {brand}, last digits {lastDigits})",
            method.Id, method.Brand, method.LastDigits);
        return method;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken ct)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var method = await _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpec(paymentMethodId), ct);
        if (method == null || method.BuyerId != buyerId)
        {
            throw new ResourceNotFoundException($"Payment method {paymentMethodId} was not found.");
        }

        try
        {
            await _cardVault.DeleteCardAsync(method.VaultPaymentTokenId, ct);
        }
        catch (PaymentGatewayException ex) when (ex.ProviderStatusCode == 404)
        {
            // Already gone at the provider; the local record is still removed below.
            _logger.LogWarning("Vault token for payment method {paymentMethodId} was already gone at PayPal", paymentMethodId);
        }

        await _repository.DeleteAsync(method, ct);
    }
}
