using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalClient _payPalClient;

    public SavedPaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalClient payPalClient)
    {
        _repository = repository;
        _payPalClient = payPalClient;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var setupToken = await _payPalClient.CreateSetupTokenAsync(
            card, $"eshop-setup-{Guid.NewGuid():N}", cancellationToken);

        if (!string.Equals(setupToken.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                "PayPal requires the shopper to approve this card in a browser before it can be saved " +
                $"(setup token status: {setupToken.Status}). This integration does not support an approval round-trip.");
        }

        var paymentToken = await _payPalClient.CreatePaymentTokenAsync(
            setupToken.Id, $"eshop-payment-token-{Guid.NewGuid():N}", cancellationToken);

        var (expiryMonth, expiryYear) = ParseExpiry(paymentToken.Expiry ?? card.Expiry);

        var saved = new SavedPaymentMethod(
            buyerId,
            paymentToken.CustomerId ?? setupToken.CustomerId,
            paymentToken.Id,
            paymentToken.Brand,
            paymentToken.LastDigits,
            expiryMonth,
            expiryYear,
            card.Name);

        return await _repository.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var saved = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (saved is null || saved.BuyerId != buyerId)
        {
            throw new EntityNotFoundException($"Saved payment method {paymentMethodId} was not found.");
        }

        try
        {
            await _payPalClient.DeletePaymentTokenAsync(saved.VaultTokenId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone from PayPal's vault; still remove it locally.
        }

        await _repository.DeleteAsync(saved, cancellationToken);
    }

    private static (int? Month, int? Year) ParseExpiry(string? expiry)
    {
        // PayPal expiry format: YYYY-MM
        if (expiry is not null && expiry.Length == 7 && expiry[4] == '-'
            && int.TryParse(expiry[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            && int.TryParse(expiry[5..], NumberStyles.None, CultureInfo.InvariantCulture, out var month))
        {
            return (month, year);
        }
        return (null, null);
    }
}
