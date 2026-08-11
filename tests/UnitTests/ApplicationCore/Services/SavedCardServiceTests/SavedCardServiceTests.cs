using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SavedCardServiceTests;

public class SavedCardServiceTests
{
    private const string Buyer = "shopper@example.com";
    private const string Other = "someone-else@example.com";

    private readonly IRepository<SavedPaymentMethod> _repo = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IAppLogger<SavedCardService> _logger = Substitute.For<IAppLogger<SavedCardService>>();

    private SavedCardService Sut() => new(_repo, _gateway, _logger);

    [Fact]
    public async Task SaveCard_StoresSafeDescriptor_AndNeverThePan()
    {
        var card = new CardDetails("4111111111111111", "12", "2030", "123", "Test Shopper");
        _gateway.VaultCardAsync(Arg.Any<CardDetails>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayVaultedCard("vault-1", "VISA", "1111", "2030-12", "Test Shopper"));
        _repo.AddAsync(Arg.Any<SavedPaymentMethod>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<SavedPaymentMethod>());

        var saved = await Sut().SaveCardAsync(Buyer, card, "my visa");

        Assert.Equal("vault-1", saved.VaultId);
        Assert.Equal("1111", saved.Last4);
        Assert.Equal("VISA", saved.Brand);
        // The stored descriptor must not contain the full card number anywhere.
        Assert.DoesNotContain("4111111111111111", saved.VaultId + saved.Last4 + saved.Brand + (saved.Expiry ?? ""));
    }

    [Fact]
    public async Task DeleteCard_Throws_WhenNotOwnedByCaller_AndDoesNotTouchVault()
    {
        var othersCard = new SavedPaymentMethod(Other, "vault-9", "VISA", "4242", "2030-01", null, null);
        _repo.FirstOrDefaultAsync(Arg.Any<SavedPaymentMethodByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(othersCard);

        await Assert.ThrowsAsync<PaymentNotFoundException>(() => Sut().DeleteCardAsync(Buyer, 9));

        await _gateway.DidNotReceive().DeleteVaultedCardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().DeleteAsync(Arg.Any<SavedPaymentMethod>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteCard_RemovesFromVaultAndStore_WhenOwned()
    {
        var card = new SavedPaymentMethod(Buyer, "vault-7", "VISA", "1111", "2030-12", null, null);
        _repo.FirstOrDefaultAsync(Arg.Any<SavedPaymentMethodByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(card);

        await Sut().DeleteCardAsync(Buyer, 7);

        await _gateway.Received(1).DeleteVaultedCardAsync("vault-7", Arg.Any<CancellationToken>());
        await _repo.Received(1).DeleteAsync(card, Arg.Any<CancellationToken>());
    }
}
