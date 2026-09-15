using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalPaymentGateway _payPal;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalPaymentGateway payPal,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // The card is vaulted at PayPal; only the durable token and a safe description come back to us.
        // Reuse the PayPal customer id from a card this shopper already saved so all their cards group together.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        var existingCustomerId = existing.Count > 0 ? existing[0].PayPalCustomerId : null;
        var merchantCustomerId = MerchantCustomerId(buyerId);

        var vaulted = await _payPal.VaultCardAsync(card, existingCustomerId, merchantCustomerId, $"eshop-vault-{Guid.NewGuid():N}", cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            string.IsNullOrEmpty(vaulted.CustomerId) ? (existingCustomerId ?? merchantCustomerId) : vaulted.CustomerId,
            vaulted.Brand ?? "Card",
            vaulted.LastDigits ?? string.Empty,
            vaulted.Expiry ?? string.Empty,
            vaulted.CardholderName ?? card.Name ?? string.Empty);

        saved = await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation("Saved card {0} for buyer {1} ({2}).", saved.Id, buyerId, saved.DisplayName);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var methods = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        return methods;
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var method = await _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpecification(paymentMethodId, buyerId), cancellationToken);
        if (method is null)
        {
            // Either it never existed or it belongs to another shopper — either way, not the caller's to delete.
            return false;
        }

        // Remove it from PayPal's vault first so it can no longer be charged, then from our store.
        await _payPal.DeleteVaultedCardAsync(method.PayPalVaultId, cancellationToken);
        await _repository.DeleteAsync(method, cancellationToken);
        _logger.LogInformation("Deleted saved card {0} for buyer {1}.", paymentMethodId, buyerId);
        return true;
    }

    /// <summary>
    /// A stable, PayPal-safe merchant customer id derived from the shopper identity. PayPal restricts this
    /// to [A-Za-z0-9_-]; it is sent as <c>customer.merchant_customer_id</c> the first time a shopper vaults a card.
    /// </summary>
    private static string MerchantCustomerId(string buyerId)
    {
        Span<char> buffer = stackalloc char[buyerId.Length];
        for (int i = 0; i < buyerId.Length; i++)
        {
            var c = buyerId[i];
            buffer[i] = char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '-';
        }
        var sanitized = new string(buffer);
        return sanitized.Length > 22 ? sanitized[..22] : sanitized;
    }
}
