using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalPaymentsClient _paypal;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalPaymentsClient paypal)
    {
        _repository = repository;
        _paypal = paypal;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardPayment card,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("A signed-in shopper is required to save a card.", 401);
        }

        var vaulted = await _paypal.VaultCardAsync(new PayPalVaultCardRequest
        {
            RequestId = $"eshop-vault-{buyerId}-{Guid.NewGuid():N}",
            MerchantCustomerId = buyerId,
            Card = MapCard(card)
        }, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.Name);

        await _repository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var method = await GetOwnedAsync(buyerId, paymentMethodId, cancellationToken);
        try
        {
            await _paypal.DeleteVaultedCardAsync(method.PaypalVaultId, cancellationToken);
        }
        catch (PaymentException)
        {
            // Continue so the shopper's copy is removed even if PayPal already dropped the token.
        }

        method.MarkDeleted();
        await _repository.UpdateAsync(method, cancellationToken);
    }

    public async Task<SavedPaymentMethod> GetOwnedAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var method = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId), cancellationToken);
        if (method == null)
        {
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }

        return method;
    }

    private static PayPalCardDetails MapCard(CardPayment card)
    {
        var number = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (number.Length is < 13 or > 19)
        {
            throw new PaymentException("Card number must contain 13 to 19 digits.");
        }

        if (string.IsNullOrWhiteSpace(card.Expiry) || card.Expiry.Length != 7)
        {
            throw new PaymentException("Card expiry must be in YYYY-MM format, for example 2028-04.");
        }

        if (string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            throw new PaymentException("Card security code is required.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentException("Cardholder name is required.");
        }

        var billing = card.BillingAddress ?? new Address("123 Main St.", "San Jose", "CA", "US", "95131");
        return new PayPalCardDetails
        {
            Number = number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = new PayPalShippingAddress
            {
                AddressLine1 = billing.Street,
                AdminArea2 = billing.City,
                AdminArea1 = billing.State,
                PostalCode = billing.ZipCode,
                CountryCode = billing.Country is { Length: 2 } ? billing.Country.ToUpperInvariant() : "US"
            }
        };
    }
}
