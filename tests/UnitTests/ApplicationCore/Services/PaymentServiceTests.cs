using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class PaymentServiceTests
{
    private const string BuyerId = "buyer@example.com";

    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<OrderPayment> _payments = Substitute.For<IRepository<OrderPayment>>();
    private readonly IRepository<PaymentMethod> _methods = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IAppLogger<PaymentService> _logger = Substitute.For<IAppLogger<PaymentService>>();

    private PaymentService CreateService() =>
        new(_orders, _payments, _methods, _items, _gateway, _uri, _logger);

    private OrderPayment CapturedPayment()
    {
        var payment = new OrderPayment(1, BuyerId, 17.00m, "USD");
        payment.MarkAuthorized("PP-ORDER", "AUTH-1", "CREATED");
        payment.MarkCaptured("CAP-1", "COMPLETED", 17.00m, 0.93m, 16.07m);
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>())
            .Returns(payment);
        return payment;
    }

    [Fact]
    public async Task Refund_beyond_captured_amount_is_rejected()
    {
        CapturedPayment();
        _gateway.Currency.Returns("USD");
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentStateException>(() =>
            service.RefundAsync(1, BuyerId, 100.00m, "key-1", CancellationToken.None));

        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_replay_under_same_key_does_not_refund_twice()
    {
        CapturedPayment();
        _gateway.RefundAsync("CAP-1", 5.00m, "key-1", Arg.Any<CancellationToken>())
            .Returns(new RefundResult { Success = true, RefundId = "R-1", Status = "COMPLETED", Amount = 5.00m });
        var service = CreateService();

        var first = await service.RefundAsync(1, BuyerId, 5.00m, "key-1", CancellationToken.None);
        var second = await service.RefundAsync(1, BuyerId, 5.00m, "key-1", CancellationToken.None);

        Assert.Equal("R-1", first.Refund.PayPalRefundId);
        Assert.Equal(first.Refund.PayPalRefundId, second.Refund.PayPalRefundId);
        await _gateway.Received(1).RefundAsync("CAP-1", 5.00m, "key-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Two_distinct_partial_refunds_are_both_applied()
    {
        var payment = CapturedPayment();
        _gateway.RefundAsync("CAP-1", 5.00m, "key-1", Arg.Any<CancellationToken>())
            .Returns(new RefundResult { Success = true, RefundId = "R-1", Status = "COMPLETED", Amount = 5.00m });
        _gateway.RefundAsync("CAP-1", 4.00m, "key-2", Arg.Any<CancellationToken>())
            .Returns(new RefundResult { Success = true, RefundId = "R-2", Status = "COMPLETED", Amount = 4.00m });
        var service = CreateService();

        await service.RefundAsync(1, BuyerId, 5.00m, "key-1", CancellationToken.None);
        await service.RefundAsync(1, BuyerId, 4.00m, "key-2", CancellationToken.None);

        Assert.Equal(9.00m, payment.RefundedAmount());
        Assert.Equal(PaymentState.PartiallyRefunded, payment.State);
    }

    [Fact]
    public async Task Pay_on_already_authorized_order_places_no_second_hold()
    {
        var payment = new OrderPayment(1, BuyerId, 17.00m, "USD");
        payment.MarkAuthorized("PP-ORDER", "AUTH-1", "CREATED");
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>())
            .Returns(payment);
        var service = CreateService();

        var result = await service.PayAsync(1, BuyerId,
            new PayInstruction { Card = SampleCard() }, CancellationToken.None);

        Assert.Equal(PaymentState.Authorized, result.State);
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<AuthorizeCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_before_capture_is_rejected()
    {
        var payment = new OrderPayment(1, BuyerId, 17.00m, "USD");
        payment.MarkAuthorized("PP-ORDER", "AUTH-1", "CREATED"); // authorized, not captured
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>())
            .Returns(payment);
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentStateException>(() =>
            service.RefundAsync(1, BuyerId, 5.00m, "key-1", CancellationToken.None));
    }

    [Fact]
    public async Task Another_shoppers_order_is_not_visible()
    {
        CapturedPayment(); // owned by BuyerId
        var service = CreateService();

        await Assert.ThrowsAsync<OrderNotFoundException>(() =>
            service.RefundAsync(1, "someone-else@example.com", 1.00m, "key-1", CancellationToken.None));
    }

    private static CardDetails SampleCard() => new()
    {
        Number = "4111111111111111",
        Expiry = "2030-01",
        SecurityCode = "123",
        CardholderName = "Test Buyer"
    };
}
