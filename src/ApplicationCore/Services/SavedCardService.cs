using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalPaymentsClient _payPal;

    public SavedCardService(IRepository<Buyer> buyerRepository, IPayPalPaymentsClient payPal)
    {
        _buyerRepository = buyerRepository;
        _payPal = payPal;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerIdentity, CardPaymentInput card, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerIdentity))
        {
            throw new PaymentForbiddenException("The caller is not authenticated.");
        }

        if (card.BillingAddress is null)
        {
            card = card with
            {
                BillingAddress = new BillingAddressInput(
                    "2211 N First Street",
                    null,
                    "San Jose",
                    "CA",
                    "95131",
                    "US")
            };
        }

        var buyer = await GetOrCreateBuyerAsync(buyerIdentity, cancellationToken);
        var vaulted = await _payPal.VaultCardAsync(card, buyer.PaypalCustomerId, cancellationToken);

        buyer.SetPaypalCustomerId(vaulted.PaypalCustomerId);
        var alias = BuildAlias(vaulted.Brand, vaulted.LastDigits);
        var method = buyer.AddPaymentMethod(alias, vaulted.PaymentTokenId, vaulted.LastDigits, vaulted.Brand, vaulted.Expiry);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerIdentity, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerIdentity), cancellationToken);
        if (buyer is null)
        {
            return new List<PaymentMethod>();
        }

        return buyer.PaymentMethods.ToList();
    }

    public async Task DeleteAsync(string buyerIdentity, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerIdentity), cancellationToken);
        if (buyer is null)
        {
            throw new PaymentNotFoundException($"Payment method {paymentMethodId} was not found.");
        }

        var method = buyer.RemovePaymentMethod(paymentMethodId);
        if (!string.IsNullOrEmpty(method.CardId))
        {
            await _payPal.DeletePaymentTokenAsync(method.CardId, cancellationToken);
        }

        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
    }

    private async Task<Buyer> GetOrCreateBuyerAsync(string buyerIdentity, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerIdentity), cancellationToken);
        if (buyer is not null)
        {
            return buyer;
        }

        buyer = new Buyer(buyerIdentity);
        await _buyerRepository.AddAsync(buyer, cancellationToken);
        return buyer;
    }

    private static string BuildAlias(string? brand, string? last4)
    {
        var brandPart = string.IsNullOrWhiteSpace(brand) ? "Card" : brand;
        return string.IsNullOrWhiteSpace(last4) ? brandPart : $"{brandPart} ending {last4}";
    }
}
