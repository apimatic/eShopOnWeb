using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentProcessor _paymentProcessor;

    public SavedPaymentMethodService(IRepository<SavedPaymentMethod> repository, IPaymentProcessor paymentProcessor)
    {
        _repository = repository;
        _paymentProcessor = paymentProcessor;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentRequestValidationException("Card number and expiry are required.");
        }

        ProcessorVaultedCard vaulted;
        try
        {
            vaulted = await _paymentProcessor.VaultCardAsync(card, buyerId, $"eshop-vlt-{Guid.NewGuid():N}", cancellationToken);
        }
        catch (PaymentProcessorException ex) when (ex.ProcessorStatusCode is >= 400 and < 500)
        {
            throw new PaymentDeclinedException($"The card could not be saved: {ex.Message}");
        }

        var saved = new SavedPaymentMethod(buyerId, vaulted.VaultTokenId, vaulted.CardBrand, vaulted.LastDigits,
            vaulted.Expiry, vaulted.CardholderName);
        await _repository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var saved = await _repository.GetByIdAsync(savedPaymentMethodId, cancellationToken);
        if (saved is null || saved.BuyerId != buyerId)
        {
            throw new NotFoundException($"Payment method {savedPaymentMethodId} was not found.");
        }

        try
        {
            await _paymentProcessor.DeleteVaultedCardAsync(saved.PayPalVaultTokenId, cancellationToken);
        }
        catch (PaymentProcessorException ex) when (ex.ProcessorStatusCode == 404)
        {
            // Already gone at the processor; still remove it locally.
        }

        await _repository.DeleteAsync(saved, cancellationToken);
    }
}
