using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedPaymentMethod> repository,
        IPaymentGateway gateway,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // One provider customer id per shopper, reused across all of their cards; null on the
        // shopper's first card — the provider generates it and we persist it from the response.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
        var customerId = existing.FirstOrDefault()?.VaultCustomerId;
        var merchantCustomerId = $"eshop-{Guid.NewGuid():N}";

        var vaulted = await _gateway.VaultCardAsync(
            customerId, merchantCustomerId, card,
            idempotencyKey: $"eshop-vault-{Guid.NewGuid():N}", ct);

        var saved = new SavedPaymentMethod(
            buyerId, vaulted.CustomerId ?? customerId ?? merchantCustomerId, vaulted.TokenId,
            vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);

        saved = await _repository.AddAsync(saved, ct);
        _logger.LogInformation($"Saved card {saved.Id} for buyer (token {vaulted.TokenId}, {vaulted.Brand} ending {vaulted.LastDigits}).");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken ct)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var saved = await _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpecification(paymentMethodId), ct);
        if (saved == null || saved.BuyerId != buyerId)
        {
            return false;
        }

        try
        {
            await _gateway.DeleteVaultedCardAsync(saved.VaultTokenId, ct);
        }
        catch (PaymentGatewayException ex) when (ex.ProviderStatusCode == 404)
        {
            // Already gone at the provider; still remove it locally.
        }

        await _repository.DeleteAsync(saved, ct);
        _logger.LogInformation($"Deleted saved card {paymentMethodId} for buyer.");
        return true;
    }
}
