using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentMethodServiceTests;

public class PaymentMethodServiceTests
{
    private const string Buyer = "buyer@test.com";

    private readonly IRepository<SavedPaymentMethod> _repo = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IPayPalPaymentService _payPal = Substitute.For<IPayPalPaymentService>();
    private readonly IAppLogger<PaymentMethodService> _logger = Substitute.For<IAppLogger<PaymentMethodService>>();

    private PaymentMethodService NewService() => new(_repo, _payPal, _logger);

    private static CardDetails Card() =>
        new("4111111111111111", "2027-12", "123", "Test", null, null, null, null, null, "US");

    [Fact]
    public async Task SaveCard_VaultsAndPersistsSafeDescriptor()
    {
        _payPal.VaultCardAsync(Arg.Any<CardDetails>(), Arg.Any<CancellationToken>())
            .Returns(new VaultCardResult("VAULT1", "VISA", "1111", "2027-12", "Test"));

        var saved = await NewService().SaveCardAsync(Buyer, Card(), CancellationToken.None);

        Assert.Equal("VAULT1", saved.VaultId);
        Assert.Equal("VISA", saved.CardBrand);
        Assert.Equal("1111", saved.LastFourDigits);
        await _repo.Received(1).AddAsync(Arg.Is<SavedPaymentMethod>(s => s.BuyerId == Buyer), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteCard_WhenOwned_RemovesVaultTokenAndLocalRecord()
    {
        var card = new SavedPaymentMethod(Buyer, "VAULT1", "VISA", "1111", "2027-12", "Test");
        _repo.FirstOrDefaultAsync(Arg.Any<SavedPaymentMethodsByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(card);

        await NewService().DeleteCardAsync(Buyer, 1, CancellationToken.None);

        await _payPal.Received(1).DeleteVaultedCardAsync("VAULT1", Arg.Any<CancellationToken>());
        await _repo.Received(1).DeleteAsync(card, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteCard_WhenNotOwnedOrMissing_ThrowsNotFoundAndDoesNotTouchPayPal()
    {
        // The spec is scoped to (buyer, id); a card owned by someone else resolves to null here.
        _repo.FirstOrDefaultAsync(Arg.Any<SavedPaymentMethodsByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns((SavedPaymentMethod?)null);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            NewService().DeleteCardAsync(Buyer, 42, CancellationToken.None));
        await _payPal.DidNotReceive().DeleteVaultedCardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
