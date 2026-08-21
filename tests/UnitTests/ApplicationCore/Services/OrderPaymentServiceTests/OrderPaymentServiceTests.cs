using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class OrderPaymentServiceTests
{
    private const string Buyer = "buyer@x.com";
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _payments = Substitute.For<IRepository<Payment>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<PaymentMethod> _methods = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPayPalPaymentService _payPal = Substitute.For<IPayPalPaymentService>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();

    private OrderPaymentService NewService()
        => new(_orders, _payments, _items, _methods, _payPal, _uri,
            Options.Create(new PayPalSettings { Currency = "USD" }));

    private void PaymentReturns(Payment payment)
        => _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpec>(), Arg.Any<CancellationToken>()).Returns(payment);

    private static Payment Authorized()
    {
        var p = new Payment(1, Buyer, 50m, "USD", "inv");
        p.RecordAuthorization("O1", "A1", "CREATED", null, null);
        return p;
    }

    private static Payment Captured()
    {
        var p = Authorized();
        p.RecordCapture("C1", "COMPLETED", 50m, 2m, 48m);
        return p;
    }

    [Fact]
    public async Task Authorize_WhenAlreadyAuthorized_IsIdempotent_NoPayPalCall()
    {
        PaymentReturns(Authorized());
        var svc = NewService();

        var result = await svc.AuthorizeAsync(1, Buyer, new PayInstruction(
            new CardDetails("4111111111111111", "2030-01", "123", "N", "US"), null));

        Assert.Equal(PaymentStatus.Authorized, result.Status);
        await _payPal.DidNotReceive().AuthorizeAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<PaymentSourceInput>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_OtherBuyer_IsHiddenAsNotFound()
    {
        PaymentReturns(Authorized()); // owned by Buyer
        var svc = NewService();

        await Assert.ThrowsAsync<PaymentFlowException>(() =>
            svc.AuthorizeAsync(1, "someone-else@x.com",
                new PayInstruction(new CardDetails("4111111111111111", "2030-01", "1", "N", "US"), null)));
    }

    [Fact]
    public async Task Fulfil_Captures_And_RecordsRenewedAuthorization()
    {
        var payment = Authorized();
        PaymentReturns(payment);
        // capture reports a *different* (renewed) authorization id
        _payPal.CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("A2-renewed", "C1", "COMPLETED", false, 50m, 2m, 48m, "USD", null));
        var svc = NewService();

        var result = await svc.FulfilAsync(1);

        Assert.Equal(PaymentStatus.Fulfilled, result.Status);
        Assert.Equal("A2-renewed", result.AuthorizationId);
        Assert.Equal(48m, result.CapturedNet);
    }

    [Fact]
    public async Task Fulfil_WhenNotAuthorized_Throws()
    {
        PaymentReturns(new Payment(1, Buyer, 50m, "USD", "inv")); // AwaitingPayment
        var svc = NewService();
        await Assert.ThrowsAsync<PaymentFlowException>(() => svc.FulfilAsync(1));
    }

    [Fact]
    public async Task Cancel_AwaitingPayment_VoidsWithoutCallingPayPal()
    {
        PaymentReturns(new Payment(1, Buyer, 50m, "USD", "inv"));
        var svc = NewService();

        var result = await svc.CancelAsync(1);

        Assert.Equal(PaymentStatus.Cancelled, result.Status);
        await _payPal.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_OverRemaining_Throws()
    {
        PaymentReturns(Captured());
        var svc = NewService();
        await Assert.ThrowsAsync<PaymentFlowException>(() => svc.RefundAsync(1, Buyer, 1000m, "k1"));
    }

    [Fact]
    public async Task Refund_SameIdempotencyKey_DoesNotCallPayPalTwice()
    {
        var payment = Captured();
        payment.AddRefund("R1", 10m, "COMPLETED", "dup"); // a prior refund under key "dup"
        PaymentReturns(payment);
        var svc = NewService();

        var result = await svc.RefundAsync(1, Buyer, 10m, "dup");

        await _payPal.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Single(result.Refunds);
    }

    [Fact]
    public async Task Refund_NewPartial_CallsPayPalAndRecordsRefund()
    {
        PaymentReturns(Captured());
        _payPal.RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new RefundResult("R-new", "COMPLETED"));
        var svc = NewService();

        var result = await svc.RefundAsync(1, Buyer, 15m, "fresh-key");

        Assert.Equal(PaymentStatus.PartiallyRefunded, result.Status);
        Assert.Equal(35m, result.RefundableRemaining());
    }
}
