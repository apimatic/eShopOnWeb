using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentMethodServiceTests;

public class SaveAndDelete
{
    private const string BuyerId = "12345";

    private readonly IRepository<PaymentMethod> _pmRepo = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();

    private PaymentMethodService CreateService() => new(_pmRepo, _gateway);

    private static CardDetails ValidCard() => new("4111111111111111", "2030-01", "123", "Tester", null);

    [Fact]
    public async Task SaveVaultsCardAndPersistsSafeDescriptor()
    {
        _gateway.VaultCardAsync(Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new VaultedCard("VAULT-1", "VISA", "1111", "Tester", "2030-01"));
        _pmRepo.FirstOrDefaultAsync(Arg.Any<PaymentMethodByVaultIdSpecification>(), Arg.Any<CancellationToken>()).Returns((PaymentMethod?)null);
        _pmRepo.AddAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>()).Returns(ci => (PaymentMethod)ci[0]);

        var pm = await CreateService().SaveCardAsync(BuyerId, ValidCard());

        Assert.Equal(BuyerId, pm.BuyerId);
        Assert.Equal("VAULT-1", pm.VaultId);
        Assert.Equal("VISA", pm.CardBrand);
        Assert.Equal("1111", pm.LastFourDigits);
        await _pmRepo.Received(1).AddAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveRejectsInvalidCardBeforeCallingGateway()
    {
        var badCard = new CardDetails("nope", "2030-01", "123", "Tester", null);

        await Assert.ThrowsAsync<PaymentInputException>(() => CreateService().SaveCardAsync(BuyerId, badCard));
        await _gateway.DidNotReceive().VaultCardAsync(Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteRemovesFromVaultAndRepositoryWhenOwned()
    {
        var pm = new PaymentMethod(BuyerId, "VAULT-1", "VISA", "1111", "Tester", "2030-01");
        _pmRepo.FirstOrDefaultAsync(Arg.Any<PaymentMethodForBuyerSpecification>(), Arg.Any<CancellationToken>()).Returns(pm);

        var deleted = await CreateService().DeleteAsync(BuyerId, 1);

        Assert.True(deleted);
        await _gateway.Received(1).RemoveVaultedCardAsync("VAULT-1", Arg.Any<CancellationToken>());
        await _pmRepo.Received(1).DeleteAsync(pm, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteReturnsFalseAndNoGatewayCallWhenNotOwned()
    {
        _pmRepo.FirstOrDefaultAsync(Arg.Any<PaymentMethodForBuyerSpecification>(), Arg.Any<CancellationToken>()).Returns((PaymentMethod?)null);

        var deleted = await CreateService().DeleteAsync(BuyerId, 99);

        Assert.False(deleted);
        await _gateway.DidNotReceive().RemoveVaultedCardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _pmRepo.DidNotReceive().DeleteAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>());
    }
}
