using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Payments.Dto;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Vaults cards with PayPal (Payment Method Tokens API v3) and keeps only the safe display
/// data — brand, last digits, expiry — in the application's own database.
/// </summary>
public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalClient _payPalClient;
    private readonly ILogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalClient payPalClient,
        ILogger<SavedPaymentMethodService> logger)
    {
        _repository = repository;
        _payPalClient = payPalClient;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        var request = new PayPalPaymentTokenRequest
        {
            Customer = new PayPalVaultCustomer { MerchantCustomerId = buyerId },
            PaymentSource = new PayPalVaultPaymentSource
            {
                Card = new PayPalVaultCardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = card.BillingCountryCode == null ? null : new PayPalAddress
                    {
                        AddressLine1 = card.BillingAddressLine1,
                        AddressLine2 = card.BillingAddressLine2,
                        AdminArea2 = card.BillingCity,
                        AdminArea1 = card.BillingState,
                        PostalCode = card.BillingPostalCode,
                        CountryCode = card.BillingCountryCode
                    }
                }
            }
        };

        PayPalPaymentTokenResponse token;
        try
        {
            token = await _payPalClient.CreatePaymentTokenAsync(
                request, $"eshop-vault-{Guid.NewGuid():N}", cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(
                $"PayPal could not save the card: {ex.Message} " +
                $"(error {ex.ErrorName ?? ex.StatusCode.ToString()}, debug id {ex.DebugId}).", ex);
        }

        var saved = new SavedPaymentMethod(
            buyerId,
            token.Id,
            token.PaymentSource?.Card?.Brand,
            token.PaymentSource?.Card?.LastDigits,
            token.PaymentSource?.Card?.Expiry);

        await _repository.AddAsync(saved, cancellationToken);

        _logger.LogInformation("Saved card ending in {LastDigits} for shopper.", saved.LastDigits);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
        => await _repository.ListAsync(new SavedPaymentMethodsByBuyerIdSpec(buyerId), cancellationToken);

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var methods = await _repository.ListAsync(new SavedPaymentMethodsByBuyerIdSpec(buyerId), cancellationToken);
        var method = methods.Find(m => m.Id == paymentMethodId);
        if (method == null)
        {
            return false;
        }

        try
        {
            await _payPalClient.DeletePaymentTokenAsync(method.VaultTokenId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone from the vault; the local record is still removed so the card
            // can never be used to pay again.
            _logger.LogWarning("Vault token already absent at PayPal while deleting saved card {PaymentMethodId}.", paymentMethodId);
        }

        await _repository.DeleteAsync(method, cancellationToken);
        return true;
    }
}
