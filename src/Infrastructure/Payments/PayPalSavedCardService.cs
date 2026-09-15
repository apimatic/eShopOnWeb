using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;
using PayPalServerSdk.Models;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalSavedCardService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _methods;
    private readonly IRepository<ShopperPayPalCustomer> _customers;
    private readonly PayPalGateway _gateway;
    private readonly PayPalSettings _settings;

    public PayPalSavedCardService(
        IRepository<SavedPaymentMethod> methods,
        IRepository<ShopperPayPalCustomer> customers,
        PayPalGateway gateway,
        IOptions<PayPalSettings> settings)
    {
        _methods = methods;
        _customers = customers;
        _gateway = gateway;
        _settings = settings.Value;
    }

    public async Task<SavedPaymentMethod> SaveAsync(string buyerId, CardPaymentInput card, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        PayPalOrderPaymentService.ValidateCard(card);

        var existingCustomer = await _customers.FirstOrDefaultAsync(
            new ShopperPayPalCustomerByBuyerSpec(buyerId), cancellationToken);

        var request = new PaymentTokenRequest
        {
            Customer = new Customer
            {
                Id = existingCustomer?.PayPalCustomerId,
                MerchantCustomerId = buyerId
            },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = PayPalOrderPaymentService.ToVaultCard(card)
            }
        };

        var token = await _gateway.CreatePaymentTokenAsync(
            payPalRequestId: $"eshop-vault-{buyerId}-{Guid.NewGuid():N}",
            body: request,
            cancellationToken: cancellationToken);

        if (string.IsNullOrEmpty(token.Id))
        {
            throw new ApiException("PayPal did not return a payment token id.", 502);
        }

        var payPalCustomerId = token.Customer?.Id;
        if (!string.IsNullOrEmpty(payPalCustomerId))
        {
            if (existingCustomer is null)
            {
                await _customers.AddAsync(new ShopperPayPalCustomer(buyerId, payPalCustomerId), cancellationToken);
            }
            else if (!string.Equals(existingCustomer.PayPalCustomerId, payPalCustomerId, StringComparison.Ordinal))
            {
                existingCustomer.SetPayPalCustomerId(payPalCustomerId);
                await _customers.UpdateAsync(existingCustomer, cancellationToken);
            }
        }

        var cardEntity = token.PaymentSource?.Card;
        var saved = new SavedPaymentMethod(
            buyerId,
            token.Id,
            cardEntity?.LastDigits,
            cardEntity?.Brand?.Value,
            cardEntity?.Expiry,
            cardEntity?.Name);

        return await _methods.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        var spec = new SavedPaymentMethodsByBuyerSpec(buyerId);
        return await _methods.ListAsync(spec, cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, string paymentMethodId, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var spec = new SavedPaymentMethodByTokenSpec(paymentMethodId);
        var saved = await _methods.FirstOrDefaultAsync(spec, cancellationToken);
        if (saved is null || !string.Equals(saved.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new ApiException("Saved card was not found.", 404);
        }

        try
        {
            await _gateway.DeletePaymentTokenAsync(saved.PaymentTokenId, cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            // Already gone on PayPal; still drop the local record.
        }

        await _methods.DeleteAsync(saved, cancellationToken);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new ApiException("PayPal is not configured. Set PayPal:ClientId and PayPal:ClientSecret.", 503);
        }
    }
}
