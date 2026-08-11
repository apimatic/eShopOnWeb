using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<PaymentMethod> paymentMethodRepository,
        IPayPalClient payPalClient,
        IAppLogger<PaymentMethodService> logger)
    {
        _paymentMethodRepository = paymentMethodRepository;
        _payPalClient = payPalClient;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default)
    {
        // Group all of a shopper's vaulted cards under one PayPal customer id: reuse an existing one if we
        // already have a saved card for this shopper, otherwise let PayPal mint one.
        var existing = await _paymentMethodRepository.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        var customerId = existing.FirstOrDefault()?.PayPalCustomerId;

        var (setupTokenId, mintedCustomerId) = await _payPalClient.CreateSetupTokenAsync(card, customerId, cancellationToken);
        var vaulted = await _payPalClient.CreatePaymentTokenAsync(setupTokenId, cancellationToken);

        var paymentMethod = new PaymentMethod(
            buyerId,
            vaulted.VaultId,
            string.IsNullOrEmpty(vaulted.CustomerId) ? mintedCustomerId : vaulted.CustomerId,
            vaulted.Brand,
            vaulted.LastFourDigits,
            vaulted.Expiry,
            card.CardholderName);

        await _paymentMethodRepository.AddAsync(paymentMethod, cancellationToken);

        // Log the saved-card id and safe description only — never the card number.
        _logger.LogInformation($"Saved card {paymentMethod.Id} ({paymentMethod.Describe()}) for buyer.");

        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _paymentMethodRepository.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var paymentMethod = await _paymentMethodRepository.FirstOrDefaultAsync(
            new PaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId), cancellationToken);

        if (paymentMethod is null)
        {
            throw new NotFoundException($"Saved card {paymentMethodId} was not found.");
        }

        await _payPalClient.DeletePaymentTokenAsync(paymentMethod.PayPalVaultId, cancellationToken);
        await _paymentMethodRepository.DeleteAsync(paymentMethod, cancellationToken);
    }
}
