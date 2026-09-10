using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class PaymentApplicationServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<OrderPayment> _payments = Substitute.For<IRepository<OrderPayment>>();
    private readonly IRepository<SavedPaymentMethod> _cards = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IRepository<CatalogItem> _catalog = Substitute.For<IRepository<CatalogItem>>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IPaymentProcessor _processor = Substitute.For<IPaymentProcessor>();
    private readonly IPaymentSettings _settings = Substitute.For<IPaymentSettings>();
    private readonly IAppLogger<PaymentApplicationService> _logger = Substitute.For<IAppLogger<PaymentApplicationService>>();

    private PaymentApplicationService CreateService()
    {
        _settings.Currency.Returns("USD");
        return new PaymentApplicationService(_orders, _payments, _cards, _catalog, _uri, _processor, _settings, _logger);
    }

    private void PaymentInStore(OrderPayment payment) =>
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>())
            .Returns(payment);

    private static OrderPayment Authorized(string buyer = "buyer@test")
    {
        var p = new OrderPayment(1, buyer, 50m, "USD");
        p.MarkAuthorized("PP-ORDER", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "VISA ending 1111");
        return p;
    }

    private static OrderPayment Fulfilled(string buyer = "buyer@test", decimal captured = 50m)
    {
        var p = new OrderPayment(1, buyer, captured, "USD");
        p.MarkAuthorized("PP-ORDER", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "VISA ending 1111");
        p.MarkFulfilled("CAP-1", "COMPLETED", captured, 2m, captured - 2m);
        return p;
    }

    [Fact]
    public async Task Pay_whenAlreadyAuthorized_returnsExisting_withoutCallingProcessor()
    {
        PaymentInStore(Authorized());
        var svc = CreateService();

        var result = await svc.PayAsync("buyer@test", 1,
            new PaymentInstrumentInput(new CardDetails("4111111111111111", "2028-12", "123", "X"), null, null),
            CancellationToken.None);

        Assert.Equal("AUTH-1", result.AuthorizationId);
        await _processor.DidNotReceive().AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_whenOrderBelongsToAnotherShopper_throwsNotFound()
    {
        PaymentInStore(Authorized("owner@test"));
        var svc = CreateService();

        await Assert.ThrowsAsync<PaymentResourceNotFoundException>(() =>
            svc.PayAsync("intruder@test", 1,
                new PaymentInstrumentInput(new CardDetails("4111111111111111", "2028-12", "123", "X"), null, null),
                CancellationToken.None));
    }

    [Fact]
    public async Task Refund_repeatingSameIdempotencyKey_returnsStoredRefund_withoutCallingProcessor()
    {
        var payment = Fulfilled();
        payment.AddRefund("K1", "REFUND-1", 5m, "COMPLETED");
        PaymentInStore(payment);
        var svc = CreateService();

        var outcome = await svc.RefundAsync("buyer@test", 1, 5m, "K1", CancellationToken.None);

        Assert.Equal("REFUND-1", outcome.Refund.RefundId);
        await _processor.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_exceedingRefundableRemaining_throws()
    {
        PaymentInStore(Fulfilled(captured: 50m));
        var svc = CreateService();

        await Assert.ThrowsAsync<RefundExceedsCaptureException>(() =>
            svc.RefundAsync("buyer@test", 1, 999m, "K9", CancellationToken.None));
    }

    [Fact]
    public async Task Fulfil_whenNotAuthorized_throwsStateException()
    {
        PaymentInStore(new OrderPayment(1, "buyer@test", 50m, "USD")); // AwaitingPayment
        var svc = CreateService();

        await Assert.ThrowsAsync<PaymentStateException>(() => svc.FulfilAsync(1, CancellationToken.None));
    }
}
