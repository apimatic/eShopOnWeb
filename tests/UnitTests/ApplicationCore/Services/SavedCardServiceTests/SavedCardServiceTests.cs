using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SavedCardServiceTests;

public class SavedCardServiceTests
{
    private readonly IRepository<PaymentMethod> _paymentMethods = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();

    private SavedCardService NewService() => new(_paymentMethods, _gateway);

    [Fact]
    public async Task SaveVaultsTheCardAndStoresOnlyASafeDescriptor()
    {
        _gateway.VaultCardAsync(Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new VaultedCardResult { VaultTokenId = "VT-1", CardBrand = "VISA", Last4 = "1111", ExpiryMonth = "12", ExpiryYear = "2030" });
        _paymentMethods.AddAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<PaymentMethod>());

        var service = NewService();
        var card = new CardDetails("4111111111111111", "12", "2030", "123", "Test Shopper", null);

        var saved = await service.SaveCardAsync("buyer-1", card, "My Visa");

        Assert.Equal("VT-1", saved.CardId); // vault token, not the number
        Assert.Equal("VISA", saved.CardBrand);
        Assert.Equal("1111", saved.Last4);
        Assert.DoesNotContain("4111", saved.CardId); // the PAN is never persisted
    }

    [Fact]
    public async Task DeleteRejectsACardOwnedByAnotherShopperAndDoesNotTouchTheVault()
    {
        var othersCard = new PaymentMethod("owner-shopper", "VT-1", "VISA", "1111", "12", "2030", "Theirs");
        _paymentMethods.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(othersCard);
        var service = NewService();

        await Assert.ThrowsAsync<PaymentMethodNotFoundException>(() => service.DeleteAsync(5, "intruder-shopper"));

        await _gateway.DidNotReceive().DeleteVaultedCardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _paymentMethods.DidNotReceive().DeleteAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteRemovesAnOwnedCardFromTheVaultAndTheStore()
    {
        var myCard = new PaymentMethod("me", "VT-9", "VISA", "1111", "12", "2030", "Mine");
        _paymentMethods.GetByIdAsync(9, Arg.Any<CancellationToken>()).Returns(myCard);
        var service = NewService();

        await service.DeleteAsync(9, "me");

        await _gateway.Received(1).DeleteVaultedCardAsync("VT-9", Arg.Any<CancellationToken>());
        await _paymentMethods.Received(1).DeleteAsync(myCard, Arg.Any<CancellationToken>());
    }
}
