using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _repository;
    private readonly IPayPalClient _payPalClient;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<PaymentMethod> repository,
        IPayPalClient payPalClient,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _payPalClient = payPalClient;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentException("A card number and expiry (YYYY-MM) are required to save a card.");
        }

        // Group new tokens under this buyer's existing PayPal customer id, if any.
        var existing = await _repository.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        var existingCustomerId = existing
            .Select(pm => pm.PayPalCustomerId)
            .FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var idempotencyKey = $"vault-{buyerId}-{Guid.NewGuid():N}";
        var vaulted = await _payPalClient.VaultCardAsync(card, existingCustomerId, idempotencyKey, cancellationToken);

        var paymentMethod = new PaymentMethod(buyerId, vaulted.VaultId, vaulted.CustomerId,
            vaulted.CardBrand, vaulted.LastFourDigits, vaulted.Expiry, vaulted.CardholderName);
        paymentMethod = await _repository.AddAsync(paymentMethod, cancellationToken);

        _logger.LogInformation($"Saved a {vaulted.CardBrand} card ending {vaulted.LastFourDigits} for a shopper (id {paymentMethod.Id}).");
        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListCardsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var paymentMethod = await _repository
            .FirstOrDefaultAsync(new PaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId), cancellationToken);
        if (paymentMethod is null)
        {
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }

        // Remove from PayPal's vault first so it can no longer be used to pay, then from our store.
        await _payPalClient.DeleteVaultedCardAsync(paymentMethod.PayPalVaultId, cancellationToken);
        await _repository.DeleteAsync(paymentMethod, cancellationToken);

        _logger.LogInformation($"Deleted saved card {paymentMethodId} for a shopper.");
    }
}
