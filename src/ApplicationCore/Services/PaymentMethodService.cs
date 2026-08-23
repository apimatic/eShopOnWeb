using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway payPalGateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _payPalGateway = payPalGateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerIdSpec(buyerId), cancellationToken);
        var paypalCustomerId = existing.FirstOrDefault()?.PayPalCustomerId;
        var merchantCustomerId = SanitizeCustomerId(buyerId);

        var vaulted = await _payPalGateway.VaultCardAsync(
            card,
            merchantCustomerId,
            paypalCustomerId,
            $"eshop-vault-{buyerId}-{Guid.NewGuid():N}",
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.CustomerId ?? paypalCustomerId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.Name);

        await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation("Saved payment method {PaymentMethodId} for buyer {BuyerId}", saved.Id, buyerId);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerIdSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var method = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (method is null || !method.BelongsTo(buyerId))
        {
            throw new PaymentException("Saved payment method was not found.", 404);
        }

        try
        {
            await _payPalGateway.DeleteVaultedCardAsync(method.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("PayPal payment token {TokenId} was already deleted", method.PayPalPaymentTokenId);
        }

        await _repository.DeleteAsync(method, cancellationToken);
        _logger.LogInformation("Deleted payment method {PaymentMethodId} for buyer {BuyerId}", paymentMethodId, buyerId);
    }

    private static string SanitizeCustomerId(string buyerId)
    {
        var sanitized = new string(buyerId.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or '@').ToArray());
        if (sanitized.Length > 64)
        {
            sanitized = sanitized[..64];
        }

        return string.IsNullOrWhiteSpace(sanitized) ? $"buyer-{buyerId.GetHashCode():X}" : sanitized;
    }
}
