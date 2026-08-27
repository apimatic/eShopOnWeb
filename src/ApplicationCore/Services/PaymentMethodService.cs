using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalClient _payPalClient;

    public PaymentMethodService(IRepository<PaymentMethod> paymentMethodRepository, IPayPalClient payPalClient)
    {
        _paymentMethodRepository = paymentMethodRepository;
        _payPalClient = payPalClient;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardPaymentSource card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var setupToken = await _payPalClient.CreateSetupTokenAsync(card, Guid.NewGuid().ToString(), cancellationToken);
        if (setupToken.RequiresBuyerAction)
        {
            throw new PaymentRequiresBuyerActionException(
                "PayPal requires the shopper to approve saving this card in a browser (e.g. a 3-D Secure challenge). " +
                "This server-to-server integration does not support an approval round-trip.");
        }

        var paymentToken = await _payPalClient.CreatePaymentTokenAsync(
            setupToken.Id, merchantCustomerId: buyerId, Guid.NewGuid().ToString(), cancellationToken);

        var lastDigits = paymentToken.LastDigits
            ?? (card.Number.Length >= 4 ? card.Number[^4..] : string.Empty);

        var paymentMethod = new PaymentMethod(
            buyerId,
            paymentToken.Id,
            paymentToken.Brand ?? "CARD",
            lastDigits,
            paymentToken.Expiry ?? card.Expiry,
            paymentToken.CardholderName ?? card.Name);

        return await _paymentMethodRepository.AddAsync(paymentMethod, cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _paymentMethodRepository.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var paymentMethod = await _paymentMethodRepository.FirstOrDefaultAsync(
            new PaymentMethodByIdSpecification(paymentMethodId), cancellationToken);
        if (paymentMethod is null || paymentMethod.BuyerId != buyerId)
        {
            throw new ResourceNotFoundException($"Payment method {paymentMethodId} was not found.");
        }

        try
        {
            await _payPalClient.DeletePaymentTokenAsync(paymentMethod.VaultTokenId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone from PayPal's vault; still remove the local record.
        }

        await _paymentMethodRepository.DeleteAsync(paymentMethod, cancellationToken);
    }
}
