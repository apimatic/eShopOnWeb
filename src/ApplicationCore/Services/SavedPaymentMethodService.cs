using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalClient _payPalClient;
    private readonly PaymentOperationGate _gate;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalClient payPalClient,
        PaymentOperationGate gate)
    {
        _repository = repository;
        _payPalClient = payPalClient;
        _gate = gate;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentSource card,
        CancellationToken cancellationToken = default)
    {
        ValidateAndNormalize(card, out var normalized);

        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        var paypalCustomerId = existing
            .Select(m => m.PayPalCustomerId)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

        var merchantCustomerId = SanitizeMerchantCustomerId(buyerId);
        var vaulted = await _payPalClient.CreatePaymentTokenAsync(
            normalized,
            merchantCustomerId,
            paypalCustomerId,
            paypalRequestId: $"eshop-vault-{Guid.NewGuid():N}",
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.CardholderName ?? normalized.Name,
            vaulted.CustomerId ?? paypalCustomerId);

        await _repository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default) =>
        _gate.RunAsync($"pm:{paymentMethodId}", () => DeleteCoreAsync(buyerId, paymentMethodId, cancellationToken));

    private async Task DeleteCoreAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var saved = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (saved is null || !string.Equals(saved.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentException("Saved payment method was not found.", 404);
        }

        await _payPalClient.DeletePaymentTokenAsync(saved.PayPalPaymentTokenId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);
    }

    private static void ValidateAndNormalize(CardPaymentSource card, out CardPaymentSource normalized)
    {
        var number = new string(card.Number.Where(char.IsDigit).ToArray());
        if (number.Length is < 13 or > 19)
        {
            throw new PaymentException("Card number is invalid.", 400);
        }

        if (card.Expiry is not { Length: 7 } || card.Expiry[4] != '-')
        {
            throw new PaymentException("Card expiry must be in YYYY-MM format.", 400);
        }

        if (card.SecurityCode is not { Length: >= 3 and <= 4 })
        {
            throw new PaymentException("Card security code is invalid.", 400);
        }

        normalized = new CardPaymentSource
        {
            Number = number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode.Trim(),
            Name = card.Name,
            BillingAddress = card.BillingAddress
        };
    }

    private static string SanitizeMerchantCustomerId(string buyerId)
    {
        var filtered = new string(buyerId.Where(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or '^' or '*' or '$' or '@' or '#').ToArray());
        if (filtered.Length > 64)
        {
            filtered = filtered[..64];
        }

        return string.IsNullOrWhiteSpace(filtered) ? "shopper" : filtered;
    }
}
