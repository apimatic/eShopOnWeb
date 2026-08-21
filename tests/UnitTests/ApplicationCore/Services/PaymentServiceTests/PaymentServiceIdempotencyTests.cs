using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class PaymentServiceIdempotencyTests
{
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<OrderPayment> _paymentRepo = Substitute.For<IRepository<OrderPayment>>();
    private readonly IRepository<CatalogItem> _itemRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<SavedCard> _savedCardRepo = Substitute.For<IRepository<SavedCard>>();
    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();

    private PaymentService CreateService()
    {
        _gateway.Currency.Returns("USD");
        return new PaymentService(_orderRepo, _paymentRepo, _itemRepo, _savedCardRepo, _gateway, _uriComposer);
    }

    private static PaymentInstrument Card() =>
        PaymentInstrument.FromCard(new CardDetails("4111111111111111", "2030-01", "123", "Test", null));

    [Fact]
    public async Task AuthorizeIsIdempotent_DoesNotReauthorizeAnAlreadyAuthorizedOrder()
    {
        var payment = new OrderPayment(1, "buyer@test", 51m, "USD");
        payment.SetAuthorized("PPO-1", "AUTH-1", "CREATED", "VISA ****1111");
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>())
            .Returns(payment);

        var service = CreateService();
        var result = await service.AuthorizeAsync("buyer@test", 1, Card());

        Assert.Equal("AUTH-1", result.AuthorizationId);
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<decimal>(), Arg.Any<PaymentInstrument>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundIsIdempotent_SameKeyReturnsExistingRefundWithoutCallingPayPal()
    {
        var payment = new OrderPayment(1, "buyer@test", 51m, "USD");
        payment.SetAuthorized("PPO-1", "AUTH-1", "CREATED", null);
        payment.SetCaptured("CAP-1", "COMPLETED", 51m, 1.81m, 49.19m);
        payment.AddRefund("R-EXISTING", 10m, "dup-key", "COMPLETED");
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>())
            .Returns(payment);

        var service = CreateService();
        var refund = await service.RefundAsync("buyer@test", 1, 10m, "dup-key");

        Assert.Equal("R-EXISTING", refund.PayPalRefundId);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundRejectsAmountBeyondRemaining()
    {
        var payment = new OrderPayment(1, "buyer@test", 51m, "USD");
        payment.SetAuthorized("PPO-1", "AUTH-1", "CREATED", null);
        payment.SetCaptured("CAP-1", "COMPLETED", 51m, 1.81m, 49.19m);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>())
            .Returns(payment);

        var service = CreateService();
        var ex = await Assert.ThrowsAsync<PaymentException>(() => service.RefundAsync("buyer@test", 1, 1000m, "k"));

        Assert.Equal(PaymentErrorKind.BusinessRule, ex.Kind);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShopperCannotActOnAnotherShoppersOrder()
    {
        var payment = new OrderPayment(1, "owner@test", 51m, "USD");
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>())
            .Returns(payment);

        var service = CreateService();
        var ex = await Assert.ThrowsAsync<PaymentException>(() => service.AuthorizeAsync("intruder@test", 1, Card()));

        Assert.Equal(PaymentErrorKind.NotFound, ex.Kind);
    }
}
