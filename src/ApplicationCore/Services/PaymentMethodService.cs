using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;

    public PaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalGateway payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var customerId = DeriveCustomerId(buyerId);
        var requestId = $"vault-{Guid.NewGuid():N}";

        var result = await _payPal.VaultCardAsync(customerId, card, requestId, ct);

        var saved = new SavedPaymentMethod(
            buyerId,
            result.CustomerId,
            result.VaultId,
            result.Brand,
            result.LastDigits,
            result.Expiry,
            result.Name);

        await _repository.AddAsync(saved, ct);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var card = await _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpecification(paymentMethodId, buyerId), ct);
        if (card is null)
            return false; // not found, or belongs to another shopper

        // Remove from PayPal's vault so it can no longer be used to pay, then from our store.
        try
        {
            await _payPal.DeleteVaultedCardAsync(card.PayPalVaultId, ct);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone at PayPal; continue to remove our record.
        }

        await _repository.DeleteAsync(card, ct);
        return true;
    }

    /// <summary>
    /// Derives a stable PayPal customer id for a shopper. PayPal's vault customer id must
    /// match <c>^[0-9a-zA-Z_-]+$</c> and be at most 22 characters, so the buyer id (an email)
    /// cannot be used directly. A deterministic hash keeps every card a shopper saves under
    /// the same PayPal customer.
    /// </summary>
    private static string DeriveCustomerId(string buyerId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return ("c" + hex).Substring(0, 22);
    }
}
