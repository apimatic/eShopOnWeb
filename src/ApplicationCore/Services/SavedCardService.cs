using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saves cards for shoppers by vaulting them with PayPal and persisting only a safe descriptor and the vault
/// token. Every read/delete is scoped to the caller so one shopper can never touch another's card.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway payPal,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var customerId = DeriveCustomerId(buyerId);
        var requestId = Guid.NewGuid().ToString("N");

        var vaulted = await _payPal.VaultCardAsync(new VaultCardRequest(card, customerId), requestId, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.TokenId,
            vaulted.CustomerId,
            vaulted.Brand,
            vaulted.LastDigits,
            vaulted.ExpiryYearMonth,
            vaulted.CardholderName);

        saved = await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation($"Saved a {saved.Brand} card ending {saved.LastDigits} for {buyerId} (vault {saved.PayPalVaultId}).");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var method = await GetOwnedAsync(buyerId, paymentMethodId, cancellationToken);
        if (method is null)
        {
            throw new PaymentEntityNotFoundException($"Saved payment method {paymentMethodId} was not found for the current user.");
        }

        // Remove the vaulted token at PayPal first so the card can no longer be used to pay, then locally.
        try
        {
            await _payPal.DeleteVaultedCardAsync(method.PayPalVaultId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogInformation($"Vault token {method.PayPalVaultId} was already gone at PayPal; removing locally.");
        }

        await _repository.DeleteAsync(method, cancellationToken);
        _logger.LogInformation($"Deleted saved payment method {paymentMethodId} for {buyerId}.");
    }

    public async Task<SavedPaymentMethod?> GetOwnedAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var method = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (method is null || !string.Equals(method.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return null;
        }
        return method;
    }

    /// <summary>
    /// Deterministic, PayPal-safe customer id for a shopper (pattern ^[0-9a-zA-Z_-]+$, max 22 chars per the vault
    /// spec's merchant_partner_customer_id). The email itself cannot be used because it contains characters PayPal
    /// rejects, so we derive a stable hash of it. "cust-" + 16 hex = 21 chars.
    /// </summary>
    private static string DeriveCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return "cust-" + hex.Substring(0, 16);
    }
}
