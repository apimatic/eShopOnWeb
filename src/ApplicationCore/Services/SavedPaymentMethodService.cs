using System;
using System.Collections.Generic;
using System.Linq;
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

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway payPal,
        IAppLogger<SavedPaymentMethodService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card == null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentRequestException("Card number and expiry are required to save a payment method.");
        }

        var customerId = ToPayPalCustomerId(buyerId);
        var merchantCustomerId = ToMerchantCustomerId(buyerId);
        var vaulted = await _payPal.VaultCardAsync(
            customerId,
            merchantCustomerId,
            card,
            $"vault-{buyerId}-{Guid.NewGuid():N}",
            cancellationToken);

        var lastDigits = string.IsNullOrWhiteSpace(vaulted.LastDigits)
            ? LastDigitsFromPan(card.Number)
            : vaulted.LastDigits;

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            lastDigits,
            vaulted.Brand,
            vaulted.Expiry ?? card.Expiry,
            vaulted.CardholderName ?? card.Name,
            vaulted.CustomerId ?? customerId);

        saved = await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation("Saved payment method {PaymentMethodId} for buyer {BuyerId}.", saved.Id, buyerId);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var items = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        return items;
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId), cancellationToken);
        if (saved == null)
        {
            throw new EntityNotFoundException($"Payment method {paymentMethodId} was not found for this shopper.");
        }

        try
        {
            await _payPal.DeletePaymentTokenAsync(saved.PayPalVaultId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogWarning("PayPal vault delete for {VaultId} returned {Message}; removing local record.", saved.PayPalVaultId, ex.Message);
        }

        await _repository.DeleteAsync(saved, cancellationToken);
    }

    internal static string ToPayPalCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        return Convert.ToHexString(hash)[..22].ToLowerInvariant();
    }

    internal static string ToMerchantCustomerId(string buyerId)
    {
        var builder = new StringBuilder();
        foreach (var ch in buyerId)
        {
            if (char.IsLetterOrDigit(ch) || "-_.^*$@#".Contains(ch))
            {
                builder.Append(ch);
            }
        }

        var value = builder.ToString();
        if (value.Length == 0)
        {
            value = ToPayPalCustomerId(buyerId);
        }

        return value.Length <= 64 ? value : value[..64];
    }

    private static string LastDigitsFromPan(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? digits : digits[^4..];
    }
}
