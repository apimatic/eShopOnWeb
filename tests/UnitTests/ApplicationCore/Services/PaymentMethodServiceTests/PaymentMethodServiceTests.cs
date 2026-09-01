using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentMethodServiceTests;

public class PaymentMethodServiceTests
{
    private const string BuyerId = "test-buyer";
    private static readonly CardDetails TestCard = new("4111111111111111", "2030-01", "123", "Demo Buyer", null);

    private readonly IRepository<SavedCard> _cards = Substitute.For<IRepository<SavedCard>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IAppLogger<PaymentMethodService> _logger = Substitute.For<IAppLogger<PaymentMethodService>>();

    private PaymentMethodService CreateService() => new(_cards, _gateway, _logger);

    [Fact]
    public async Task SaveCardStoresOnlySafeDescriptors()
    {
        _cards.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<SavedCard>>(), Arg.Any<CancellationToken>())
            .Returns(new List<SavedCard>());
        _gateway.VaultCardAsync(TestCard, Arg.Is<string?>(s => s == null), BuyerId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new VaultedCardResult("TOKEN-1", "CUST-1", "VISA", "1111", "2030-01"));
        _cards.AddAsync(Arg.Any<SavedCard>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SavedCard>());

        var saved = await CreateService().SaveCardAsync(BuyerId, TestCard);

        Assert.Equal("TOKEN-1", saved.PayPalPaymentTokenId);
        Assert.Equal("VISA", saved.Brand);
        Assert.Equal("1111", saved.LastDigits);
        Assert.DoesNotContain("4111", saved.Brand + saved.LastDigits + saved.Expiry);
    }

    [Fact]
    public async Task SaveCardReusesExistingPayPalCustomer()
    {
        var existing = new SavedCard(BuyerId, "CUST-1", "TOKEN-0", "VISA", "1111", "2029-01");
        _cards.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<SavedCard>>(), Arg.Any<CancellationToken>())
            .Returns(new List<SavedCard> { existing });
        _gateway.VaultCardAsync(TestCard, "CUST-1", BuyerId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new VaultedCardResult("TOKEN-1", "CUST-1", "VISA", "1111", "2030-01"));
        _cards.AddAsync(Arg.Any<SavedCard>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SavedCard>());

        await CreateService().SaveCardAsync(BuyerId, TestCard);

        await _gateway.Received(1).VaultCardAsync(TestCard, "CUST-1", BuyerId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveCardRejectsMalformedExpiry()
    {
        var badCard = TestCard with { Expiry = "01/2030" };

        await Assert.ThrowsAsync<BadRequestException>(() => CreateService().SaveCardAsync(BuyerId, badCard));
    }

    [Fact]
    public async Task DeleteAnotherBuyersCardThrowsNotFound()
    {
        var otherPeoplesCard = new SavedCard("someone-else", "CUST-9", "TOKEN-9", "VISA", "1111", "2030-01");
        _cards.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(otherPeoplesCard);

        await Assert.ThrowsAsync<SavedCardNotFoundException>(() => CreateService().DeleteCardAsync(BuyerId, 5));
        await _gateway.DidNotReceive().DeleteVaultedCardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteCardConvergesWhenPayPalAlreadyRemovedIt()
    {
        var card = new SavedCard(BuyerId, "CUST-1", "TOKEN-1", "VISA", "1111", "2030-01");
        _cards.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(card);
        _gateway.DeleteVaultedCardAsync("TOKEN-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new PaymentGatewayException("PayPal returned HTTP 404.", 404)));

        await CreateService().DeleteCardAsync(BuyerId, 5);

        await _cards.Received(1).DeleteAsync(card, Arg.Any<CancellationToken>());
    }
}
