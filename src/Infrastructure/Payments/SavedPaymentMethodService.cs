using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications.Payments;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>Vaults, lists and removes a shopper's saved cards. Card numbers are never stored or logged.</summary>
public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly ILogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(IRepository<SavedPaymentMethod> repository,
        IPayPalPaymentGateway gateway, ILogger<SavedPaymentMethodService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        if (card is null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.ExpiryYearMonth))
            throw new PaymentFlowException(PaymentFlowError.Validation, "Card number and expiry are required.");

        // Reuse the shopper's existing PayPal customer if we already have one, so all their cards vault
        // under one customer; otherwise let PayPal create one keyed by a stable merchant customer id.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
        var existingCustomerId = existing.Select(m => m.PayPalCustomerId).FirstOrDefault(id => !string.IsNullOrEmpty(id));
        var merchantCustomerId = ToMerchantCustomerId(buyerId);

        var result = await _gateway.VaultCardAsync(
            new VaultCardRequest(card, merchantCustomerId, existingCustomerId), ct);

        var method = new SavedPaymentMethod(buyerId, result.VaultId, result.CustomerId ?? existingCustomerId,
            result.Brand, result.LastFourDigits, result.ExpiryMonthYear, result.CardholderName);
        await _repository.AddAsync(method, ct);

        _logger.LogInformation("Saved card {PaymentMethodId} for {BuyerId} ({Brand} ****{Last4}).",
            method.Id, buyerId, method.Brand, method.LastFourDigits);
        return ToView(method);
    }

    public async Task<IReadOnlyList<SavedCardView>> ListAsync(string buyerId, CancellationToken ct)
    {
        var methods = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
        return methods.Select(ToView).ToList();
    }

    public async Task DeleteAsync(string buyerId, Guid paymentMethodId, CancellationToken ct)
    {
        var method = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(paymentMethodId, buyerId), ct)
            ?? throw new PaymentFlowException(PaymentFlowError.NotFound, "The saved card was not found.");

        // Remove from PayPal first so it can no longer fund a payment, then drop the local record.
        await _gateway.DeleteVaultedCardAsync(method.PayPalVaultId, ct);
        await _repository.DeleteAsync(method, ct);
        _logger.LogInformation("Deleted saved card {PaymentMethodId} for {BuyerId}.", paymentMethodId, buyerId);
    }

    private static SavedCardView ToView(SavedPaymentMethod m) =>
        new(m.Id, m.Brand, m.LastFourDigits, m.ExpiryMonthYear, m.CardholderName, m.CreatedAt);

    /// <summary>Maps a buyer id to a PayPal merchant_customer_id (allowed chars, ≤64 chars).</summary>
    private static string ToMerchantCustomerId(string buyerId)
    {
        var sb = new StringBuilder(buyerId.Length);
        foreach (var c in buyerId)
        {
            sb.Append(char.IsLetterOrDigit(c) || "-_.^*$@#".IndexOf(c) >= 0 ? c : '_');
        }
        var sanitized = sb.ToString();
        return sanitized.Length <= 64 ? sanitized : sanitized.Substring(0, 64);
    }
}
