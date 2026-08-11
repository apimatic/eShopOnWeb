using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _repository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<PaymentMethod> repository,
        IPaymentGateway gateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var idempotencyKey = $"vault-{Guid.NewGuid():N}";
        var vaulted = await _gateway.VaultCardAsync(idempotencyKey, ToCustomerId(buyerId), card, cancellationToken);

        var method = new PaymentMethod(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.Last4, vaulted.Expiry, alias);
        method = await _repository.AddAsync(method, cancellationToken);
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new PaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var method = await _repository.FirstOrDefaultAsync(
            new PaymentMethodByIdForBuyerSpec(paymentMethodId, buyerId), cancellationToken);
        if (method is null)
        {
            return false;
        }

        // Best-effort removal at PayPal; local removal below is what guarantees the card
        // can no longer be selected to pay, so a provider hiccup must not block it.
        try
        {
            await _gateway.DeleteVaultedCardAsync(method.VaultId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Vault token for payment method {paymentMethodId} could not be removed at " +
                $"PayPal ({ex.Message}); removing the local record regardless.");
        }

        await _repository.DeleteAsync(method, cancellationToken);
        return true;
    }

    /// <summary>
    /// A stable, PayPal-safe merchant customer id derived from the buyer. It groups a
    /// shopper's vaulted cards without exposing the buyer's identity (e.g. their email).
    /// </summary>
    private static string ToCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        return "c" + Convert.ToHexString(hash).ToLowerInvariant().Substring(0, 21);
    }
}
