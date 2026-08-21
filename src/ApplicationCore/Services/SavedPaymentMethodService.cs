using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalPaymentsGateway _payPal;

    public SavedPaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalPaymentsGateway payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<SavedPaymentMethod> SaveAsync(
        string buyerId,
        CardPaymentDetails card,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card is null)
        {
            throw new CommerceException(400, "Card details are required to save a payment method.");
        }

        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        var payPalCustomerId = existing.Select(m => m.PayPalCustomerId).FirstOrDefault(id => !string.IsNullOrEmpty(id));
        var merchantCustomerId = ToMerchantCustomerId(buyerId);
        var requestId = $"eshop-vault-{buyerId}-{Guid.NewGuid():N}";

        var vaulted = await _payPal.VaultCardAsync(merchantCustomerId, payPalCustomerId, card, requestId, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.PayPalCustomerId ?? payPalCustomerId,
            vaulted.Last4,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.CardholderName);

        return await _repository.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var methods = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        return methods;
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId), cancellationToken);
        if (saved is null)
        {
            throw new CommerceException(404, "Saved payment method was not found.");
        }

        try
        {
            await _payPal.DeletePaymentTokenAsync(saved.PayPalPaymentTokenId, cancellationToken);
        }
        catch (CommerceException ex) when (ex.StatusCode == 404)
        {
            // Already removed at PayPal; continue so it disappears locally too.
        }

        await _repository.DeleteAsync(saved, cancellationToken);
    }

    internal static string ToMerchantCustomerId(string buyerId)
    {
        var sanitized = Regex.Replace(buyerId, @"[^0-9a-zA-Z-_.^*$@#]", "-");
        if (sanitized.Length <= 64)
        {
            return sanitized;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyerId))).ToLowerInvariant();
        return hash[..64];
    }
}
