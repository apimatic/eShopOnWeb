using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway payPal,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("A signed-in shopper is required.", 401);
        }

        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentException("Card number and expiry (YYYY-MM) are required.");
        }

        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        var paypalCustomerId = existing.Select(m => m.PayPalCustomerId).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

        var vaulted = await _payPal.VaultCardAsync(
            card,
            paypalCustomerId,
            requestId: $"eshop-vault-{buyerId}-{Guid.NewGuid():N}",
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.PayPalCustomerId ?? paypalCustomerId,
            vaulted.Brand,
            vaulted.LastDigits,
            vaulted.Expiry,
            vaulted.CardholderName ?? card.Name);

        await _repository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpec(paymentMethodId, buyerId), cancellationToken);
        if (saved is null)
        {
            throw new PaymentException("Saved payment method was not found.", 404);
        }

        try
        {
            await _payPal.DeletePaymentTokenAsync(saved.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("PayPal payment token {TokenId} was already removed.", saved.PayPalPaymentTokenId);
        }

        await _repository.DeleteAsync(saved, cancellationToken);
    }
}
