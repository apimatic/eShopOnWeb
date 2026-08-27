using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPaymentGateway gateway,
        IAppLogger<SavedPaymentMethodService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        if (!Regex.IsMatch(card.Number ?? string.Empty, "^[0-9]{13,19}$"))
        {
            throw new PaymentConflictException("Card number must be 13-19 digits.");
        }
        if (!Regex.IsMatch(card.Expiry ?? string.Empty, "^[0-9]{4}-(0[1-9]|1[0-2])$"))
        {
            throw new PaymentConflictException("Card expiry must be in YYYY-MM format.");
        }

        var result = await _gateway.CreateVaultTokenAsync(card, buyerId,
            $"eshop-vault-{buyerId}-{Guid.NewGuid():N}", cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, result.VaultTokenId, result.PayPalCustomerId,
            result.Brand, result.LastFourDigits, result.Expiry, result.CardholderName);
        await _repository.AddAsync(saved, cancellationToken);

        _logger.LogInformation($"Saved a {result.Brand} card ending in {result.LastFourDigits} for buyer {buyerId}.");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var saved = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (saved is null || saved.BuyerId != buyerId || !saved.IsActive)
        {
            throw new ResourceNotFoundException($"Payment method {paymentMethodId} was not found.");
        }

        try
        {
            await _gateway.DeleteVaultTokenAsync(saved.VaultTokenId, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound)
        {
            // Already gone from the vault — removal is still achieved.
        }

        saved.Deactivate();
        await _repository.UpdateAsync(saved, cancellationToken);

        _logger.LogInformation($"Deleted saved payment method {paymentMethodId} for buyer {buyerId}.");
    }
}
