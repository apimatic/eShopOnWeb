using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentDetails card,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(card.Number) ||
            string.IsNullOrWhiteSpace(card.Expiry) ||
            string.IsNullOrWhiteSpace(card.SecurityCode) ||
            string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentException("Card number, expiry, security code and name are required to save a card.");
        }

        var vaulted = await _payPal.VaultCardAsync(new PayPalVaultCardRequest
        {
            RequestId = $"eshop-vault-{buyerId}-{System.Guid.NewGuid():N}",
            MerchantCustomerId = buyerId,
            CardNumber = new string(card.Number.Where(char.IsDigit).ToArray()),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            CardholderName = card.Name,
            BillingAddress = new PayPalBillingAddress
            {
                AddressLine1 = string.IsNullOrWhiteSpace(card.BillingAddress?.AddressLine1) ? "123 Main Street" : card.BillingAddress!.AddressLine1,
                AddressLine2 = card.BillingAddress?.AddressLine2,
                AdminArea2 = string.IsNullOrWhiteSpace(card.BillingAddress?.AdminArea2) ? "San Jose" : card.BillingAddress!.AdminArea2,
                AdminArea1 = string.IsNullOrWhiteSpace(card.BillingAddress?.AdminArea1) ? "CA" : card.BillingAddress!.AdminArea1,
                PostalCode = string.IsNullOrWhiteSpace(card.BillingAddress?.PostalCode) ? "95131" : card.BillingAddress!.PostalCode,
                CountryCode = string.IsNullOrWhiteSpace(card.BillingAddress?.CountryCode) ? "US" : card.BillingAddress!.CountryCode
            }
        }, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.CardholderName);

        return await _repository.AddAsync(saved, cancellationToken);
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
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByBuyerAndIdSpec(buyerId, paymentMethodId), cancellationToken);
        if (saved is null)
        {
            throw new PaymentForbiddenException("The saved card was not found or does not belong to the current shopper.");
        }

        await _payPal.DeleteVaultedCardAsync(saved.PayPalVaultId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);
    }
}
