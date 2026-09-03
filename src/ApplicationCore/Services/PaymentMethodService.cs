using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _repository;
    private readonly IPaymentGateway _gateway;

    public PaymentMethodService(IRepository<PaymentMethod> repository, IPaymentGateway gateway)
    {
        _repository = repository;
        _gateway = gateway;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Reuse the shopper's PayPal customer id if they already have one, so all their cards are
        // vaulted under the same customer; otherwise pass a stable merchant customer id for them.
        var existing = await _repository.ListAsync(new PaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        var existingCustomerId = existing
            .Select(pm => pm.PayPalCustomerId)
            .FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var vaulted = await _gateway.VaultCardAsync(card, existingCustomerId, MerchantCustomerId(buyerId), cancellationToken);

        var paymentMethod = new PaymentMethod(
            buyerId,
            vaulted.TokenId,
            vaulted.CustomerId ?? existingCustomerId,
            vaulted.Brand,
            vaulted.LastFourDigits,
            vaulted.Expiry);

        await _repository.AddAsync(paymentMethod, cancellationToken);
        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new PaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var paymentMethod = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);
        // Only the owner may remove a card; hide others' cards behind "not found".
        if (paymentMethod is null || paymentMethod.BuyerId != buyerId)
            return false;

        await _gateway.DeleteVaultedCardAsync(paymentMethod.PayPalVaultId, cancellationToken);
        await _repository.DeleteAsync(paymentMethod, cancellationToken);
        return true;
    }

    /// <summary>A stable, PayPal-safe customer id derived from the shopper id (no raw email on the wire).</summary>
    private static string MerchantCustomerId(string buyerId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        var sb = new StringBuilder("eshop-", 22);
        for (var i = 0; i < 8; i++)
            sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }
}
