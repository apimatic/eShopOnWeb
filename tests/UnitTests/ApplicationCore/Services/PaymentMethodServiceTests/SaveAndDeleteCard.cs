using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentMethodServiceTests;

public class SaveAndDeleteCard
{
    private readonly IRepository<PaymentMethod> _pmRepo = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IAppLogger<PaymentMethodService> _logger = Substitute.For<IAppLogger<PaymentMethodService>>();

    private readonly string _ownerId = "owner-1";

    private PaymentMethodService CreateService() => new(_pmRepo, _gateway, _logger);

    private static CardDetails ValidCard() => new()
    {
        Number = "4111111111111111",
        ExpiryMonthYear = "2030-01",
        SecurityCode = "123",
        CardholderName = "Demo User"
    };

    [Fact]
    public async Task SaveCard_VaultsCard_AndStoresSafeDescriptionOnly()
    {
        _gateway.VaultCardAsync(Arg.Any<VaultCardRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayVaultResult
            {
                Success = true,
                VaultToken = "VAULT-TOKEN",
                Last4 = "1111",
                Brand = "VISA",
                ExpiryMonthYear = "2030-01",
                CardholderName = "Demo User"
            });
        _pmRepo.AddAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<PaymentMethod>());

        var result = await CreateService().SaveCardAsync(_ownerId, ValidCard(), "my visa");

        Assert.Equal(SaveCardOutcome.Saved, result.Outcome);
        Assert.Equal(_ownerId, result.PaymentMethod!.OwnerId);
        Assert.Equal("VAULT-TOKEN", result.PaymentMethod!.VaultToken);
        Assert.Equal("1111", result.PaymentMethod!.Last4);
        Assert.Equal("VISA", result.PaymentMethod!.Brand);
        // Full PAN is never stored.
        Assert.DoesNotContain("4111111111111111", result.PaymentMethod!.VaultToken);
        await _pmRepo.Received(1).AddAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveCard_ReturnsGatewayError_WhenVaultingFails()
    {
        _gateway.VaultCardAsync(Arg.Any<VaultCardRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayVaultResult { Success = false, ErrorMessage = "boom" });

        var result = await CreateService().SaveCardAsync(_ownerId, ValidCard(), null);

        Assert.Equal(SaveCardOutcome.GatewayError, result.Outcome);
        await _pmRepo.DidNotReceive().AddAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteCard_ReturnsNotFound_ForAnotherShoppersCard()
    {
        _pmRepo.FirstOrDefaultAsync(Arg.Any<PaymentMethodByIdForOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns((PaymentMethod?)null);

        var result = await CreateService().DeleteAsync(_ownerId, 42);

        Assert.Equal(DeleteCardOutcome.NotFound, result.Outcome);
        await _pmRepo.DidNotReceive().DeleteAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>());
        await _gateway.DidNotReceive().DeleteVaultedCardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteCard_RemovesFromVaultAndStore_WhenOwned()
    {
        var card = new PaymentMethod(_ownerId, "VAULT-TOKEN", "1111", "VISA", "2030-01", "Demo User", "my visa");
        _pmRepo.FirstOrDefaultAsync(Arg.Any<PaymentMethodByIdForOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(card);
        _gateway.DeleteVaultedCardAsync("VAULT-TOKEN", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().DeleteAsync(_ownerId, 1);

        Assert.Equal(DeleteCardOutcome.Deleted, result.Outcome);
        await _gateway.Received(1).DeleteVaultedCardAsync("VAULT-TOKEN", Arg.Any<CancellationToken>());
        await _pmRepo.Received(1).DeleteAsync(card, Arg.Any<CancellationToken>());
    }
}
