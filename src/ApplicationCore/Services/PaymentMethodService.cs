using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _repository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<PaymentMethod> repository,
        IPayPalPaymentGateway gateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _gateway.VaultCardAsync(card, cancellationToken);

        var paymentMethod = new PaymentMethod(buyerId, vaulted.TokenId, vaulted.Brand,
            vaulted.LastFourDigits, vaulted.CardholderName, vaulted.Expiry);

        return await _repository.AddAsync(paymentMethod, cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken = default)
    {
        var paymentMethod = await _repository.FirstOrDefaultAsync(
            new PaymentMethodByIdForBuyerSpecification(paymentMethodId, buyerId), cancellationToken);
        if (paymentMethod is null)
            throw new PaymentMethodNotFoundException(paymentMethodId);

        // Remove locally first: that is what makes the card stop appearing and stop being usable to
        // pay. Then clean up the PayPal vault token as best-effort — if that call fails the card is
        // already gone from the app, which is the contract we must honour.
        await _repository.DeleteAsync(paymentMethod, cancellationToken);

        try
        {
            await _gateway.DeleteVaultedCardAsync(paymentMethod.PayPalVaultTokenId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogWarning(
                "Removed saved card {0} locally but could not delete its PayPal vault token: {1}",
                paymentMethodId, ex.Message);
        }
    }
}
