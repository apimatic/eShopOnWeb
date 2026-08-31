using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

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

        var customerId = VaultCustomerId(buyerId);
        var gatewayCard = new GatewayCard(
            card.Number,
            card.Expiry,
            card.SecurityCode,
            card.CardholderName,
            card.BillingAddress == null
                ? null
                : new GatewayAddress(
                    card.BillingAddress.Line1,
                    card.BillingAddress.Line2,
                    card.BillingAddress.City,
                    card.BillingAddress.State,
                    card.BillingAddress.PostalCode,
                    card.BillingAddress.CountryCode));

        GatewayVaultedCard vaulted;
        try
        {
            vaulted = await _gateway.VaultCardAsync(customerId, gatewayCard, $"eshop-vault-{Guid.NewGuid():N}", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Vaulting a card for buyer failed: {ex.Message}");
            throw new PaymentDeclinedException($"PayPal could not save the card: {ex.Message}");
        }

        var saved = new SavedPaymentMethod(buyerId, vaulted.VaultTokenId, vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        await _repository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var saved = await _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId), cancellationToken)
            ?? throw new NotFoundException($"Saved payment method {paymentMethodId} was not found.");

        await _gateway.DeleteVaultedCardAsync(saved.VaultTokenId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);
    }

    /// <summary>
    /// Derives the PayPal vault customer id for a buyer. The vault API requires
    /// ^[0-9a-zA-Z_-]+$ (max 22 chars on create), which e-mail-shaped buyer ids do not
    /// satisfy, so a stable deterministic id is derived instead.
    /// </summary>
    internal static string VaultCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"eshop-{hex[..15]}";
    }
}
