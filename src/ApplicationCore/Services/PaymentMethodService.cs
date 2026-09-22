using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _gateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway gateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var reference = $"vault-{Guid.NewGuid():N}";
        var vaulted = await _gateway.VaultCardAsync(reference, card, MerchantCustomerId(buyerId), ct);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.Brand,
            vaulted.Last4,
            vaulted.Expiry,
            vaulted.CardholderName);

        await _repository.AddAsync(saved, ct);
        _logger.LogInformation("Saved card {Id} (vault {Vault}) for buyer.", saved.Id, saved.VaultId);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetForBuyerAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var method = await _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpecification(paymentMethodId), ct);
        if (method is null || method.BuyerId != buyerId)
        {
            // Not found, or owned by another shopper — either way, the caller cannot delete it.
            return false;
        }

        await _gateway.DeleteVaultedCardAsync(method.VaultId, ct);
        await _repository.DeleteAsync(method, ct);
        _logger.LogInformation("Deleted saved card {Id} for buyer.", paymentMethodId);
        return true;
    }

    /// <summary>
    /// A deterministic, PayPal-safe merchant customer id derived from the shopper's identity, so a shopper's
    /// vaulted cards are associated to one PayPal customer. Pattern: ^[0-9a-zA-Z-_.^*$@#]+$, max 64.
    /// </summary>
    private static string MerchantCustomerId(string buyerId)
    {
        var sb = new StringBuilder("eshop-");
        foreach (var c in buyerId)
        {
            sb.Append(IsAllowed(c) ? c : '_');
        }

        var id = sb.ToString();
        return id.Length <= 64 ? id : id.Substring(0, 64);

        static bool IsAllowed(char c) =>
            char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '^' or '*' or '$' or '@' or '#';
    }
}
