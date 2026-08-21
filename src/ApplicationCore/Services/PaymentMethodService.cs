using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _repository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<PaymentMethod> repository,
        IPaymentGateway gateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<Result<SavedCardView>> SaveCardAsync(string buyerId, CardInput card, CancellationToken ct = default)
    {
        if (card is null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            return Result<SavedCardView>.Invalid(new ValidationError { ErrorMessage = "Card number and expiry are required." });
        }

        var gatewayCard = new GatewayCard(
            card.Number, card.Expiry, card.SecurityCode, card.CardholderName,
            BuildBilling(card));

        var vaulted = await _gateway.VaultCardAsync(gatewayCard, $"eshop-vault-{Guid.NewGuid():N}", ct);

        var method = new PaymentMethod(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.LastDigits, vaulted.Expiry);
        await _repository.AddAsync(method, ct);

        _logger.LogInformation("Saved card {PaymentMethodId} ({Brand} ****{Last4}) for {BuyerId}.",
            method.Id, method.CardBrand, method.LastFourDigits, buyerId);
        return Result<SavedCardView>.Created(new SavedCardView(method.Id, method.CardBrand, method.LastFourDigits, method.Expiry));
    }

    public async Task<Result<IReadOnlyList<SavedCardView>>> GetCardsAsync(string buyerId, CancellationToken ct = default)
    {
        var cards = await _repository.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), ct);
        var views = cards
            .Select(c => new SavedCardView(c.Id, c.CardBrand, c.LastFourDigits, c.Expiry))
            .ToList();
        return Result<IReadOnlyList<SavedCardView>>.Success(views);
    }

    public async Task<Result> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        var method = await _repository.FirstOrDefaultAsync(new PaymentMethodByIdSpecification(paymentMethodId), ct);
        if (method is null || method.BuyerId != buyerId)
        {
            return Result.NotFound();
        }

        await _gateway.DeleteVaultedCardAsync(method.PayPalVaultId, ct);
        await _repository.DeleteAsync(method, ct);

        _logger.LogInformation("Deleted saved card {PaymentMethodId} for {BuyerId}.", paymentMethodId, buyerId);
        return Result.Success();
    }

    private static GatewayBillingAddress? BuildBilling(CardInput card)
    {
        if (string.IsNullOrEmpty(card.CountryCode) ||
            (card.BillingLine1 is null && card.BillingPostalCode is null && card.BillingCity is null))
        {
            return null;
        }
        return new GatewayBillingAddress(
            card.BillingLine1, card.BillingLine2, card.BillingState, card.BillingCity, card.BillingPostalCode, card.CountryCode);
    }
}
