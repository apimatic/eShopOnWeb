using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway payPal,
        IAppLogger<SavedPaymentMethodService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Reuse the shopper's existing PayPal customer id so all their cards live under one customer.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId));
        var customerId = existing.Select(p => p.PayPalCustomerId).FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var result = await _payPal.VaultCardAsync(card, customerId, idempotencyKey: Guid.NewGuid().ToString("N"));

        var saved = new SavedPaymentMethod(
            buyerId,
            result.VaultId,
            result.CustomerId,
            result.CardBrand,
            result.CardLastFour,
            result.Expiry,
            result.CardholderName);

        saved = await _repository.AddAsync(saved);
        _logger.LogInformation($"Saved card {saved.Id} for buyer (vault token {result.VaultId}, {result.CardBrand} ****{result.CardLastFour}).");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId));
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var pm = await _repository.GetByIdAsync(paymentMethodId);
        if (pm is null || pm.BuyerId != buyerId)
        {
            // Do not reveal another shopper's card — treat as not found.
            throw new PaymentException("Saved card not found.", PaymentErrorReason.NotFound);
        }

        await _payPal.DeleteVaultedCardAsync(pm.PayPalVaultId);
        await _repository.DeleteAsync(pm);
        _logger.LogInformation($"Deleted saved card {paymentMethodId} (vault token {pm.PayPalVaultId}).");
    }
}
