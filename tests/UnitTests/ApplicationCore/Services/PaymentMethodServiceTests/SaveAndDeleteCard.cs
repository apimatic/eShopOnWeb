using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentMethodServiceTests;

public class SaveAndDeleteCard
{
    private readonly IRepository<PaymentMethod> _paymentMethodRepo = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPayPalGateway _payPal = Substitute.For<IPayPalGateway>();

    private PaymentMethodService CreateSut() => new(_paymentMethodRepo, _payPal);

    private static readonly PayPalCardDetails Card = new()
    {
        Number = "4111111111111111",
        Expiry = "2030-01",
        SecurityCode = "123",
        CardholderName = "Test Buyer",
        AddressLine1 = "1 Test St",
        City = "Testville",
        PostalCode = "12345",
        CountryCode = "US"
    };

    [Fact]
    public async Task SaveCardVaultsAndPersistsSafeDetailsOnly()
    {
        _payPal.SaveCardAsync(Card, default).Returns(new PayPalVaultCardResult
        {
            PaymentTokenId = "TOKEN-1",
            CustomerId = "CUST-1",
            CardBrand = "VISA",
            LastDigits = "1111",
            Expiry = "2030-01"
        });
        _paymentMethodRepo.AddAsync(Arg.Any<PaymentMethod>(), default).Returns(call => call.Arg<PaymentMethod>());

        var sut = CreateSut();
        var result = await sut.SaveCardAsync("buyer-1", Card);

        Assert.Equal("VISA", result.CardBrand);
        Assert.Equal("1111", result.LastDigits);
        Assert.Equal("buyer-1", result.BuyerId);
    }

    [Fact]
    public async Task DeletingAnotherBuyersCardThrowsForbidden()
    {
        var paymentMethod = new PaymentMethod("owner", "cust-1", "token-1", "VISA", "1111", "2030-01");
        _paymentMethodRepo.GetByIdAsync(1, default).Returns(paymentMethod);

        var sut = CreateSut();
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => sut.DeleteSavedCardAsync("someone-else", 1));
    }

    [Fact]
    public async Task DeletingOwnCardInvalidatesVaultTokenThenDeletesLocally()
    {
        var paymentMethod = new PaymentMethod("owner", "cust-1", "token-1", "VISA", "1111", "2030-01");
        _paymentMethodRepo.GetByIdAsync(1, default).Returns(paymentMethod);

        var sut = CreateSut();
        await sut.DeleteSavedCardAsync("owner", 1);

        await _payPal.Received(1).DeleteVaultedCardAsync("token-1", default);
        await _paymentMethodRepo.Received(1).DeleteAsync(paymentMethod, default);
    }

    [Fact]
    public async Task DeletingMissingCardThrowsNotFound()
    {
        _paymentMethodRepo.GetByIdAsync(1, default).Returns((PaymentMethod?)null);

        var sut = CreateSut();
        await Assert.ThrowsAsync<PaymentMethodNotFoundException>(() => sut.DeleteSavedCardAsync("owner", 1));
    }
}
