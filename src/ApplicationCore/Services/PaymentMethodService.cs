using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payment;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<Buyer> buyerRepository,
        IPaymentGateway paymentGateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _buyerRepository = buyerRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerIdentity, CardPaymentDetails card, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerIdentity))
        {
            throw new PaymentException("A signed-in shopper is required to save a card.");
        }

        ValidateCard(card);

        var buyer = await GetOrCreateBuyerAsync(buyerIdentity, cancellationToken);
        var merchantCustomerId = CreateMerchantCustomerId(buyerIdentity);

        var saved = await _paymentGateway.SaveCardAsync(
            merchantCustomerId,
            buyer.PayPalCustomerId,
            card,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(saved.PayPalCustomerId))
        {
            buyer.AssignPayPalCustomerId(saved.PayPalCustomerId);
        }
        else if (string.IsNullOrWhiteSpace(buyer.PayPalCustomerId))
        {
            buyer.AssignPayPalCustomerId(merchantCustomerId);
        }

        var last4 = saved.LastDigits ?? LastFour(card.Number);
        var method = buyer.AddPaymentMethod(
            saved.VaultId,
            last4,
            saved.Brand,
            saved.Expiry,
            PaymentMethod.BuildAlias(saved.Brand, last4));

        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        _logger.LogInformation("Saved payment method {PaymentMethodId} for buyer {BuyerId}", method.Id, buyerIdentity);
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerIdentity, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpec(buyerIdentity), cancellationToken);
        if (buyer is null)
        {
            return new List<PaymentMethod>();
        }

        return buyer.PaymentMethods.ToList();
    }

    public async Task DeleteAsync(string buyerIdentity, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpec(buyerIdentity), cancellationToken);
        var method = buyer?.GetPaymentMethod(paymentMethodId);
        if (buyer is null || method is null)
        {
            throw new PaymentNotFoundException("The saved card was not found for this shopper.");
        }

        if (!string.IsNullOrEmpty(method.CardId))
        {
            try
            {
                await _paymentGateway.DeleteSavedCardAsync(method.CardId, cancellationToken);
            }
            catch (PaymentGatewayException ex) when (ex.StatusCode == 404)
            {
                _logger.LogInformation("PayPal vault token for payment method {PaymentMethodId} was already removed", paymentMethodId);
            }
        }

        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        _logger.LogInformation("Deleted payment method {PaymentMethodId} for buyer {BuyerId}", paymentMethodId, buyerIdentity);
    }

    private async Task<Buyer> GetOrCreateBuyerAsync(string buyerIdentity, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpec(buyerIdentity), cancellationToken);
        if (buyer is not null)
        {
            return buyer;
        }

        buyer = new Buyer(buyerIdentity);
        await _buyerRepository.AddAsync(buyer, cancellationToken);
        return buyer;
    }

    internal static string CreateMerchantCustomerId(string identity)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return "c" + hex[..16];
    }

    private static string LastFour(string number) =>
        number.Length >= 4 ? number[^4..] : number;

    private static void ValidateCard(CardPaymentDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || card.Number.Length is < 13 or > 19)
        {
            throw new PaymentException("A valid card number is required.");
        }

        if (string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentException("Card expiry (YYYY-MM) is required.");
        }

        if (string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            throw new PaymentException("Card security code is required.");
        }
    }
}
