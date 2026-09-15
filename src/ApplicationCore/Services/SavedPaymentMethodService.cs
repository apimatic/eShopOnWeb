using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
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

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentRequest card)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("A signed-in shopper is required.", 401);
        }

        var paypalCard = new PayPalCardDetails(
            CardInput.NormalizeNumber(card.Number),
            CardInput.NormalizeExpiry(card.Expiry),
            card.SecurityCode.Trim(),
            string.IsNullOrWhiteSpace(card.Name) ? "Shopper" : card.Name.Trim(),
            card.BillingAddress is null
                ? new PayPalBillingAddress("123 Test Street", null, "Seattle", "WA", "98101", "US")
                : new PayPalBillingAddress(
                    card.BillingAddress.Street,
                    null,
                    card.BillingAddress.City,
                    card.BillingAddress.State,
                    card.BillingAddress.ZipCode,
                    string.IsNullOrWhiteSpace(card.BillingAddress.Country)
                        ? "US"
                        : card.BillingAddress.Country.Trim().ToUpperInvariant()));

        var lastFour = paypalCard.Number.Length >= 4 ? paypalCard.Number[^4..] : paypalCard.Number;
        var requestId = $"eshop-vault-{Sanitize(buyerId)}-{lastFour}-{paypalCard.Expiry}";
        var vaulted = await _payPal.VaultCardAsync(Sanitize(buyerId), paypalCard, requestId);

        var existing = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByPaypalTokenSpec(vaulted.PaymentTokenId));
        if (existing is not null)
        {
            return existing;
        }

        var method = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.Brand,
            vaulted.LastDigits,
            vaulted.Expiry,
            vaulted.CardholderName ?? paypalCard.Name);

        await _repository.AddAsync(method);
        _logger.LogInformation("Saved payment method {PaymentMethodId} ending {LastFour}", method.Id, method.LastFourDigits);
        return method;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId) =>
        await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId));

    public async Task DeleteAsync(string buyerId, int paymentMethodId)
    {
        var method = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId))
            ?? throw new PaymentMethodNotFoundException(paymentMethodId);

        try
        {
            await _payPal.DeletePaymentTokenAsync(method.PaypalPaymentTokenId);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("PayPal token for payment method {PaymentMethodId} was already removed.", method.Id);
        }

        await _repository.DeleteAsync(method);
        _logger.LogInformation("Deleted payment method {PaymentMethodId}", method.Id);
    }

    private static string Sanitize(string buyerId)
    {
        var chars = new char[buyerId.Length];
        var n = 0;
        foreach (var c in buyerId)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '@')
            {
                chars[n++] = c;
            }
        }

        var value = n == 0 ? "buyer00" : new string(chars, 0, n);
        if (value.Length < 7)
        {
            value = (value + "buyer00")[..7];
        }

        return value.Length <= 64 ? value : value[..64];
    }
}
