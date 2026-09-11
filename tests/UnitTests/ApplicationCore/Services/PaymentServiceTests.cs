using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class PaymentServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<OrderPayment> _payments = Substitute.For<IRepository<OrderPayment>>();
    private readonly IRepository<SavedPaymentMethod> _cards = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IPaymentProcessor _processor = Substitute.For<IPaymentProcessor>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IAppLogger<PaymentService> _logger = Substitute.For<IAppLogger<PaymentService>>();

    private PaymentService CreateService() =>
        new(_orders, _items, _payments, _cards, _processor, _uri,
            new PaymentOptions { CurrencyCode = "USD" }, _logger);

    private void SetupPayment(OrderPayment payment) =>
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>())
            .Returns(payment);

    [Fact]
    public async Task Pay_WhenAlreadyAuthorized_DoesNotAuthorizeAgain()
    {
        var payment = new OrderPayment(1, "buyer", "USD", 20m);
        payment.MarkAuthorized("PP", "AUTH", "CREATED", null);
        SetupPayment(payment);

        var result = await CreateService().PayAsync(1, "buyer",
            new CardDetails("4111111111111111", "2030-01", "123"), null);

        Assert.Equal(PaymentStatus.Authorized, result.Status);
        await _processor.DidNotReceive().AuthorizeAsync(Arg.Any<AuthorizeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_ForAnotherBuyer_IsNotFound()
    {
        var payment = new OrderPayment(1, "owner", "USD", 20m);
        SetupPayment(payment);

        await Assert.ThrowsAsync<PaymentNotFoundException>(() =>
            CreateService().PayAsync(1, "intruder", new CardDetails("4111111111111111", "2030-01", "123"), null));
    }

    [Fact]
    public async Task Refund_RepeatedIdempotencyKey_DoesNotRefundTwice()
    {
        var payment = new OrderPayment(1, "buyer", "USD", 100m);
        payment.MarkAuthorized("PP", "AUTH", "CREATED", null);
        payment.MarkCaptured("CAP", "COMPLETED", 100m, 3m, 97m);
        payment.AddRefund(new PaymentRefund("R1", 40m, "key-1", "COMPLETED"));
        SetupPayment(payment);

        var refund = await CreateService().RefundAsync(1, "buyer", 40m, "key-1");

        Assert.Equal("R1", refund.PayPalRefundId);
        await _processor.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_NewKey_CallsProcessorAndRecords()
    {
        var payment = new OrderPayment(1, "buyer", "USD", 100m);
        payment.MarkAuthorized("PP", "AUTH", "CREATED", null);
        payment.MarkCaptured("CAP", "COMPLETED", 100m, 3m, 97m);
        SetupPayment(payment);
        _processor.RefundAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult { RefundId = "R-NEW", Status = "COMPLETED", Amount = 25m });

        var refund = await CreateService().RefundAsync(1, "buyer", 25m, "fresh-key");

        Assert.Equal("R-NEW", refund.PayPalRefundId);
        Assert.Equal(25m, payment.TotalRefunded());
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
    }
}
