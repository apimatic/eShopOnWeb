using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentMethodServiceTests;

public class SaveListDeleteCard
{
    private const string BuyerId = "buyer-1";

    private readonly IRepository<SavedPaymentMethod> _repository = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();
    private readonly IAppLogger<PaymentMethodService> _logger = Substitute.For<IAppLogger<PaymentMethodService>>();

    private PaymentMethodService CreateService() => new PaymentMethodService(_repository, _gateway, _logger);

    private static CardDetails Card() =>
        new CardDetails { Number = "4111111111111111", Expiry = "2030-01", SecurityCode = "123", CardholderName = "Test" };

    [Fact]
    public async Task SaveCard_VaultsAndStoresSafeDescriptor()
    {
        _gateway.VaultCardAsync(Arg.Any<string>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new VaultedCardResult { VaultId = "vault-1", Brand = "VISA", Last4 = "1111", Expiry = "2030-01" });

        var saved = await CreateService().SaveCardAsync(BuyerId, Card());

        Assert.Equal(BuyerId, saved.BuyerId);
        Assert.Equal("vault-1", saved.PayPalVaultId);
        Assert.Equal("1111", saved.Last4);
        Assert.Equal("VISA", saved.Brand);
        await _repository.Received(1).AddAsync(Arg.Any<SavedPaymentMethod>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteCard_NotOwned_Throws_PaymentMethodNotFound()
    {
        _repository.FirstOrDefaultAsync(Arg.Any<ISpecification<SavedPaymentMethod>>(), Arg.Any<CancellationToken>())
            .Returns((SavedPaymentMethod?)null);

        await Assert.ThrowsAsync<PaymentMethodNotFoundException>(
            () => CreateService().DeleteAsync(BuyerId, 42));

        await _repository.DidNotReceive().DeleteAsync(Arg.Any<SavedPaymentMethod>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteCard_Owned_RemovesFromVaultAndRepository()
    {
        var card = new SavedPaymentMethod(BuyerId, "vault-1", "VISA", "1111", "2030-01", "Test");
        _repository.FirstOrDefaultAsync(Arg.Any<ISpecification<SavedPaymentMethod>>(), Arg.Any<CancellationToken>())
            .Returns(card);

        await CreateService().DeleteAsync(BuyerId, 1);

        await _gateway.Received(1).DeleteVaultedCardAsync("vault-1", Arg.Any<CancellationToken>());
        await _repository.Received(1).DeleteAsync(card, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteCard_VaultFailure_StillRemovesLocalRecord()
    {
        var card = new SavedPaymentMethod(BuyerId, "vault-1", "VISA", "1111", "2030-01", "Test");
        _repository.FirstOrDefaultAsync(Arg.Any<ISpecification<SavedPaymentMethod>>(), Arg.Any<CancellationToken>())
            .Returns(card);
        _gateway.DeleteVaultedCardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new PaymentFailedException("provider down"));

        await CreateService().DeleteAsync(BuyerId, 1);

        // The local record must be removed even when the remote delete fails, so the card can no longer be used.
        await _repository.Received(1).DeleteAsync(card, Arg.Any<CancellationToken>());
    }
}
