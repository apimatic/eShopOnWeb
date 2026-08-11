using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _repository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<PaymentMethod> repository,
        IPayPalPaymentGateway gateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _gateway.VaultCardAsync(card, Guid.NewGuid().ToString(), customerId: null, cancellationToken);

        var paymentMethod = new PaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.CustomerId,
            vaulted.Brand,
            vaulted.Last4,
            vaulted.Expiry,
            string.IsNullOrWhiteSpace(vaulted.CardholderName) ? card.Name : vaulted.CardholderName,
            alias);

        paymentMethod = await _repository.AddAsync(paymentMethod, cancellationToken);
        _logger.LogInformation($"Saved card {paymentMethod.Id} ({vaulted.Brand} ****{vaulted.Last4}) for {buyerId}.");
        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var cards = await _repository.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        return cards;
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var paymentMethod = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (paymentMethod is null || paymentMethod.BuyerId != buyerId)
        {
            // Never reveal another shopper's card.
            throw new EntityNotFoundException($"Saved card {paymentMethodId} was not found.");
        }

        try
        {
            await _gateway.DeleteVaultedCardAsync(paymentMethod.PayPalVaultId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            // Best effort at PayPal; regardless, remove it locally so it can no longer be used to pay.
            _logger.LogWarning($"Could not delete vaulted card {paymentMethod.PayPalVaultId} at PayPal ({ex.Message}); removing local reference anyway.");
        }

        await _repository.DeleteAsync(paymentMethod, cancellationToken);
        _logger.LogInformation($"Removed saved card {paymentMethodId} for {buyerId}.");
    }
}
