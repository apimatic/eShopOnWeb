using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentMethodServiceTests;

public class SaveAndDeleteCard
{
    private const string BuyerId = "buyer-1";

    private readonly IRepository<PaymentMethod> _repo = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPayPalPaymentGateway _gateway = Substitute.For<IPayPalPaymentGateway>();
    private readonly IAppLogger<PaymentMethodService> _logger = Substitute.For<IAppLogger<PaymentMethodService>>();

    private PaymentMethodService CreateService() => new(_repo, _gateway, _logger);

    private readonly CardDetails _card = new("Demo User", "4111111111111111", 12, 2030, "123", null);

    [Fact]
    public async Task SaveCardVaultsAndPersistsSafeReference()
    {
        _gateway.VaultCardAsync(Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new VaultedCard("VAULT-1", new CardDisplay("VISA", "1111", 12, 2030)));
        _repo.AddAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<PaymentMethod>());

        var saved = await CreateService().SaveCardAsync(BuyerId, _card);

        Assert.Equal(BuyerId, saved.BuyerId);
        Assert.Equal("VAULT-1", saved.CardId);
        Assert.Equal("1111", saved.Last4);
        Assert.Equal("VISA", saved.CardBrand);
        // Full PAN is never stored.
        Assert.DoesNotContain("4111111111111111", saved.CardId);
        await _repo.Received(1).AddAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteReturnsFalseAndSkipsVaultWhenCardNotOwned()
    {
        _repo.FirstOrDefaultAsync(Arg.Any<PaymentMethodByIdAndBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns((PaymentMethod?)null);

        var deleted = await CreateService().DeleteAsync(BuyerId, 42);

        Assert.False(deleted);
        await _gateway.DidNotReceive().DeleteVaultedCardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().DeleteAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteRemovesFromVaultAndRepositoryWhenOwned()
    {
        var pm = new PaymentMethod(BuyerId, "VAULT-1", "VISA", "1111", 12, 2030, "Demo User");
        _repo.FirstOrDefaultAsync(Arg.Any<PaymentMethodByIdAndBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(pm);

        var deleted = await CreateService().DeleteAsync(BuyerId, 1);

        Assert.True(deleted);
        await _gateway.Received(1).DeleteVaultedCardAsync("VAULT-1", Arg.Any<CancellationToken>());
        await _repo.Received(1).DeleteAsync(pm, Arg.Any<CancellationToken>());
    }
}
