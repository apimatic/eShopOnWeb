using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;

    public SavedCardService(IRepository<SavedPaymentMethod> repository, IPayPalGateway payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<SavedCardDto> SaveCardAsync(string buyerId, CardPaymentCommand card, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("The caller is not authenticated.", HttpStatusCode.Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentException("Card number and expiry are required.");
        }

        var vaulted = await _payPal.VaultCardAsync(new PayPalCardDetails
        {
            Number = card.Number.Replace(" ", string.Empty),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress == null
                ? null
                : new PayPalBillingAddress
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = card.BillingAddress.CountryCode
                }
        }, $"eshop-vault-{Guid.NewGuid():N}", cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.Name,
            vaulted.CustomerId);

        await _repository.AddAsync(saved, cancellationToken);
        return Map(saved);
    }

    public async Task<IReadOnlyList<SavedCardDto>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var cards = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        return cards.Select(Map).ToList();
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpec(paymentMethodId, buyerId),
            cancellationToken);
        if (saved == null)
        {
            throw new PaymentException("Saved payment method was not found.", HttpStatusCode.NotFound);
        }

        await _payPal.DeletePaymentTokenAsync(saved.PayPalPaymentTokenId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);
    }

    private static SavedCardDto Map(SavedPaymentMethod saved)
    {
        var brand = string.IsNullOrWhiteSpace(saved.Brand) ? "Card" : saved.Brand;
        var expiry = string.IsNullOrWhiteSpace(saved.Expiry) ? string.Empty : $" exp {saved.Expiry}";
        return new SavedCardDto
        {
            PaymentMethodId = saved.Id,
            LastDigits = saved.LastDigits,
            Brand = saved.Brand,
            Expiry = saved.Expiry,
            CardholderName = saved.CardholderName,
            DisplayName = $"{brand} •••• {saved.LastDigits}{expiry}"
        };
    }
}
