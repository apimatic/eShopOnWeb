using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<Buyer> buyerRepository,
        IPayPalPaymentGateway gateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _buyerRepository = buyerRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(
        string buyerId, SaveCardInstruction instruction, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(instruction, nameof(instruction));

        // Vault the card at PayPal first — the full card never touches this app's database.
        var vault = await _gateway.VaultCardAsync(instruction.Card, cancellationToken);

        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);

        if (buyer is null)
        {
            buyer = new Buyer(buyerId);
            var method = buyer.AddPaymentMethod(vault.TokenId, vault.Brand, vault.Last4, vault.Expiry, instruction.Alias);
            await _buyerRepository.AddAsync(buyer, cancellationToken);
            _logger.LogInformation("Saved first card for buyer (token {TokenId}).", vault.TokenId);
            return method;
        }
        else
        {
            var method = buyer.AddPaymentMethod(vault.TokenId, vault.Brand, vault.Last4, vault.Expiry, instruction.Alias);
            await _buyerRepository.UpdateAsync(buyer, cancellationToken);
            _logger.LogInformation("Saved card for buyer (token {TokenId}).", vault.TokenId);
            return method;
        }
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        return buyer?.PaymentMethods.ToList() ?? new List<PaymentMethod>();
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);

        var method = buyer?.FindPaymentMethod(paymentMethodId)
            ?? throw new PaymentEntityNotFoundException($"Saved card {paymentMethodId} was not found.");

        // Remove the token at PayPal first so it can never be used to pay again; only then drop it locally.
        await _gateway.DeleteVaultedCardAsync(method.CardId, cancellationToken);

        buyer!.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        _logger.LogInformation("Deleted saved card {PaymentMethodId} (token {TokenId}).", paymentMethodId, method.CardId);
    }
}
