using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _payPal;

    public PaymentMethodService(IRepository<PaymentMethod> paymentMethodRepository, IPayPalGateway payPal)
    {
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _payPal.SaveCardAsync(card);

        var paymentMethod = new PaymentMethod(
            buyerId,
            vaulted.CustomerId,
            vaulted.PaymentTokenId,
            vaulted.CardBrand ?? "UNKNOWN",
            vaulted.LastDigits ?? string.Empty,
            vaulted.Expiry ?? string.Empty);

        return await _paymentMethodRepository.AddAsync(paymentMethod);
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetSavedCardsAsync(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var spec = new PaymentMethodsByBuyerSpecification(buyerId);
        return await _paymentMethodRepository.ListAsync(spec);
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var paymentMethod = await _paymentMethodRepository.GetByIdAsync(paymentMethodId)
            ?? throw new PaymentMethodNotFoundException(paymentMethodId);

        if (paymentMethod.BuyerId != buyerId)
        {
            throw new ForbiddenAccessException($"Payment method {paymentMethodId} does not belong to the caller.");
        }

        // Best-effort: invalidate at PayPal first so the token can never be used to pay again,
        // even if our local delete were somehow retried or the token id leaked elsewhere.
        await _payPal.DeleteVaultedCardAsync(paymentMethod.PayPalPaymentTokenId);

        await _paymentMethodRepository.DeleteAsync(paymentMethod);
    }
}
