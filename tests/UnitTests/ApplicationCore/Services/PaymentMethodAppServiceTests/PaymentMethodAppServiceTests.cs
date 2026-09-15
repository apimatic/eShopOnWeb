using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentMethodAppServiceTests;

public class PaymentMethodAppServiceTests
{
    private readonly IRepository<PaymentMethod> _repo = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPayPalPaymentService _payPal = Substitute.For<IPayPalPaymentService>();

    private PaymentMethodAppService NewService() => new(_repo, _payPal);

    [Fact]
    public async Task SaveCard_VaultsAndStoresSafeDescriptor()
    {
        _payPal.VaultCardAsync(Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new VaultCardResult("VAULT-1", "VISA", "1111", "2030-01"));
        _repo.AddAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<PaymentMethod>());

        var card = new CardDetails("4111111111111111", "2030-01", "123", "Jo", "US");
        var saved = await NewService().SaveCardAsync("buyer", card);

        Assert.Equal("VAULT-1", saved.PayPalVaultId);
        Assert.Equal("1111", saved.LastFourDigits);
        Assert.Equal("VISA", saved.CardBrand);
    }

    [Fact]
    public async Task Delete_OtherBuyersCard_ReturnsFalse_AndDoesNotCallPayPal()
    {
        var othersCard = new PaymentMethod("other-buyer", "VAULT-9", "VISA", "1111", "Jo", "2030-01");
        _repo.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(othersCard);

        var removed = await NewService().DeleteAsync(5, "buyer");

        Assert.False(removed);
        await _payPal.DidNotReceive().DeleteVaultedCardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().DeleteAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_OwnCard_RemovesFromVaultAndStore()
    {
        var card = new PaymentMethod("buyer", "VAULT-1", "VISA", "1111", "Jo", "2030-01");
        _repo.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(card);

        var removed = await NewService().DeleteAsync(1, "buyer");

        Assert.True(removed);
        await _payPal.Received(1).DeleteVaultedCardAsync("VAULT-1", Arg.Any<CancellationToken>());
        await _repo.Received(1).DeleteAsync(card, Arg.Any<CancellationToken>());
    }
}
