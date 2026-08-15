using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentMethodServiceTests;

public class PaymentMethodServiceTests
{
    private readonly IRepository<PaymentMethod> _repo = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();
    private readonly IAppLogger<PaymentMethodService> _logger = Substitute.For<IAppLogger<PaymentMethodService>>();

    private PaymentMethodService CreateService() => new(_repo, _gateway, _logger);

    [Fact]
    public async Task SaveCard_VaultsAndStoresSafeDescriptor()
    {
        _gateway.VaultCardAsync(Arg.Any<CardDetails>(), Arg.Any<CancellationToken>())
            .Returns(new VaultedCardResult("vault-123", "VISA", "1111", "2027-01"));
        var service = CreateService();

        var method = await service.SaveCardAsync("buyer@test",
            new CardDetails("N", "4111111111111111", "2027-01", "123"));

        Assert.Equal("vault-123", method.VaultId);
        Assert.Equal("VISA", method.Brand);
        Assert.Equal("1111", method.LastDigits);
        // Full card number never lands on the stored entity.
        Assert.DoesNotContain("4111", method.LastDigits + method.Brand + method.Expiry + method.VaultId);
        await _repo.Received(1).AddAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteCard_RemovesTheOwnersCard()
    {
        var card = new PaymentMethod("buyer@test", "vault-1", "VISA", "1111", "2027-01");
        _repo.FirstOrDefaultAsync(Arg.Any<PaymentMethodByIdForBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(card);
        var service = CreateService();

        var deleted = await service.DeleteCardAsync("buyer@test", 1);

        Assert.True(deleted);
        await _repo.Received(1).DeleteAsync(card, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteCard_ThatIsNotTheCallers_ReturnsFalse_AndDeletesNothing()
    {
        // The owner-scoped spec finds nothing for a non-owner.
        _repo.FirstOrDefaultAsync(Arg.Any<PaymentMethodByIdForBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns((PaymentMethod?)null);
        var service = CreateService();

        var deleted = await service.DeleteCardAsync("intruder@test", 1);

        Assert.False(deleted);
        await _repo.DidNotReceive().DeleteAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>());
    }
}
