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

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IAppLogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(
        IRepository<Buyer> buyerRepository,
        IPaymentGateway paymentGateway,
        IAppLogger<SavedPaymentMethodService> logger)
    {
        _buyerRepository = buyerRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentSource card,
        CancellationToken cancellationToken = default)
    {
        var buyer = await GetOrCreateBuyer(buyerId, cancellationToken);
        var vaulted = await _paymentGateway.VaultCardAsync(
            buyer.PayPalCustomerId,
            card,
            $"eshop-vault-{buyer.PayPalCustomerId}-{System.Guid.NewGuid():N}",
            cancellationToken);

        var last4 = vaulted.LastDigits;
        if (string.IsNullOrEmpty(last4) && card.Number.Length >= 4)
        {
            last4 = card.Number[^4..];
        }

        var method = buyer.AddPaymentMethod(
            vaulted.VaultId,
            last4,
            vaulted.Brand,
            vaulted.Expiry ?? card.Expiry,
            alias: DescribeCard(vaulted.Brand, last4));

        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        _logger.LogInformation("Saved payment method {PaymentMethodId} for buyer", method.Id);
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), cancellationToken);
        if (buyer is null)
        {
            return System.Array.Empty<PaymentMethod>();
        }

        return buyer.PaymentMethods.ToList();
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), cancellationToken);
        var method = buyer?.GetPaymentMethod(paymentMethodId);
        if (buyer is null || method is null)
        {
            throw new PaymentException($"Saved payment method {paymentMethodId} was not found.", 404);
        }

        if (!string.IsNullOrEmpty(method.CardId))
        {
            try
            {
                await _paymentGateway.DeleteVaultedCardAsync(method.CardId, cancellationToken);
            }
            catch (PaymentException ex) when (ex.StatusCode == 404)
            {
                _logger.LogWarning("PayPal vault token already absent when deleting payment method {PaymentMethodId}", paymentMethodId);
            }
        }

        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
    }

    private async Task<Buyer> GetOrCreateBuyer(string buyerId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), cancellationToken);
        if (buyer is not null)
        {
            return buyer;
        }

        buyer = new Buyer(buyerId);
        await _buyerRepository.AddAsync(buyer, cancellationToken);
        return buyer;
    }

    private static string DescribeCard(string? brand, string? last4)
    {
        var network = string.IsNullOrWhiteSpace(brand) ? "Card" : brand;
        return string.IsNullOrWhiteSpace(last4) ? network : $"{network} ending {last4}";
    }
}
