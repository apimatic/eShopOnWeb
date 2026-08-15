using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class PaymentServiceTests
{
    private const string Buyer = "buyer@test.com";

    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _paymentRepo = Substitute.For<IRepository<Payment>>();
    private readonly IReadRepository<SavedPaymentMethod> _savedRepo = Substitute.For<IReadRepository<SavedPaymentMethod>>();
    private readonly FakePaymentGateway _gateway = new();
    private readonly PayPalSettings _settings = new() { Currency = "USD" };

    private PaymentService CreateService() => new(_orderRepo, _paymentRepo, _savedRepo, _gateway, _settings);

    private static Order OrderWithTotal(decimal total, string buyer = Buyer)
    {
        var address = new Address("123 Main", "Kent", "OH", "US", "44240");
        var item = new OrderItem(new CatalogItemOrdered(1, "Widget", "pic.png"), total, 1);
        return new Order(buyer, address, new List<OrderItem> { item });
    }

    private static CardDetails SampleCard() => new("4111111111111111", "12", "2030", "123", "Test", null);

    // ---------- Authorize ----------

    [Fact]
    public async Task AuthorizeWithCard_CreatesPaymentAndMarksOrderAuthorized()
    {
        var order = OrderWithTotal(100m);
        _orderRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Payment>>(), Arg.Any<CancellationToken>()).Returns((Payment?)null);
        _paymentRepo.AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Payment>());

        var payment = await CreateService().AuthorizeOrderAsync(1, Buyer, SampleCard(), null);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal(100m, payment.Amount);
        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        Assert.Equal(1, _gateway.AuthorizeCardCalls);
    }

    [Fact]
    public async Task Authorize_IsIdempotent_WhenPaymentAlreadyExists()
    {
        var order = OrderWithTotal(100m);
        var existing = MakeAuthorizedPayment(100m);
        _orderRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Payment>>(), Arg.Any<CancellationToken>()).Returns(existing);

        var payment = await CreateService().AuthorizeOrderAsync(1, Buyer, SampleCard(), null);

        Assert.Same(existing, payment);
        Assert.Equal(0, _gateway.AuthorizeCardCalls); // gateway never called again
    }

    [Fact]
    public async Task Authorize_RejectsCardAndSavedCardTogether()
    {
        await Assert.ThrowsAsync<PaymentException>(() =>
            CreateService().AuthorizeOrderAsync(1, Buyer, SampleCard(), 5));
    }

    [Fact]
    public async Task Authorize_RejectsNeitherCardNorSavedCard()
    {
        await Assert.ThrowsAsync<PaymentException>(() =>
            CreateService().AuthorizeOrderAsync(1, Buyer, null, null));
    }

    [Fact]
    public async Task Authorize_ForAnotherBuyersOrder_IsNotFound()
    {
        var order = OrderWithTotal(100m, "someone-else@test.com");
        _orderRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(order);

        await Assert.ThrowsAsync<PaymentNotFoundException>(() =>
            CreateService().AuthorizeOrderAsync(1, Buyer, SampleCard(), null));
    }

    [Fact]
    public async Task Authorize_WithSavedCard_UsesVaultAndRecordsCardId()
    {
        var order = OrderWithTotal(50m);
        var card = new SavedPaymentMethod(Buyer, "VAULT-1", "VISA", "1111", "12", "2030", "Test");
        _orderRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Payment>>(), Arg.Any<CancellationToken>()).Returns((Payment?)null);
        _paymentRepo.AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Payment>());
        _savedRepo.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(card);

        var payment = await CreateService().AuthorizeOrderAsync(1, Buyer, null, 7);

        Assert.Equal(1, _gateway.AuthorizeVaultCalls);
        Assert.Equal(card.Id, payment.SavedPaymentMethodId);
    }

    [Fact]
    public async Task Authorize_WithAnotherBuyersSavedCard_IsNotFound()
    {
        var order = OrderWithTotal(50m);
        var card = new SavedPaymentMethod("other@test.com", "VAULT-1", "VISA", "1111", "12", "2030", "Test");
        _orderRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Payment>>(), Arg.Any<CancellationToken>()).Returns((Payment?)null);
        _savedRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(card);

        await Assert.ThrowsAsync<PaymentNotFoundException>(() =>
            CreateService().AuthorizeOrderAsync(1, Buyer, null, 7));
    }

    // ---------- Fulfil ----------

    [Fact]
    public async Task Fulfil_CapturesAndMarksFulfilled()
    {
        var order = OrderWithTotal(100m);
        order.MarkPaymentAuthorized();
        var payment = MakeAuthorizedPayment(100m);
        _orderRepo.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Payment>>(), Arg.Any<CancellationToken>()).Returns(payment);

        var result = await CreateService().FulfilOrderAsync(1);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(3m, result.PayPalFee);
        Assert.Equal(97m, result.NetAmount);
    }

    [Fact]
    public async Task Fulfil_WhenAuthorizationStale_ReauthorizesThenCaptures()
    {
        var order = OrderWithTotal(100m);
        order.MarkPaymentAuthorized();
        var payment = MakeAuthorizedPayment(100m, expiry: DateTimeOffset.UtcNow.AddMinutes(-5)); // stale
        _orderRepo.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Payment>>(), Arg.Any<CancellationToken>()).Returns(payment);

        var result = await CreateService().FulfilOrderAsync(1);

        Assert.Equal(1, _gateway.ReauthorizeCalls);
        Assert.Equal(PaymentStatus.Captured, result.Status);
        Assert.Equal("AUTH-2", result.AuthorizationId);
    }

    [Fact]
    public async Task Fulfil_WhenAuthorizationCannotBeRenewed_ThrowsOperatorActionable()
    {
        var order = OrderWithTotal(100m);
        order.MarkPaymentAuthorized();
        var payment = MakeAuthorizedPayment(100m, expiry: DateTimeOffset.UtcNow.AddMinutes(-5));
        _gateway.ReauthorizeShouldFail = true;
        _orderRepo.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Payment>>(), Arg.Any<CancellationToken>()).Returns(payment);

        var ex = await Assert.ThrowsAsync<PaymentException>(() => CreateService().FulfilOrderAsync(1));
        Assert.Contains("can no longer be renewed", ex.Message);
    }

    [Fact]
    public async Task Fulfil_IsIdempotent_WhenAlreadyCaptured()
    {
        var order = OrderWithTotal(100m);
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();
        var payment = MakeCapturedPayment(100m);
        _orderRepo.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Payment>>(), Arg.Any<CancellationToken>()).Returns(payment);

        var result = await CreateService().FulfilOrderAsync(1);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        Assert.Equal(0, _gateway.CaptureCalls); // not captured again
    }

    // ---------- Cancel ----------

    [Fact]
    public async Task Cancel_VoidsAuthorizedHold()
    {
        var order = OrderWithTotal(100m);
        order.MarkPaymentAuthorized();
        var payment = MakeAuthorizedPayment(100m);
        _orderRepo.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Payment>>(), Arg.Any<CancellationToken>()).Returns(payment);

        var result = await CreateService().CancelOrderAsync(1);

        Assert.Equal(1, _gateway.VoidCalls);
        Assert.Equal(PaymentStatus.Voided, result!.Status);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public async Task Cancel_AfterFulfilment_IsRejected()
    {
        var order = OrderWithTotal(100m);
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();
        var payment = MakeCapturedPayment(100m);
        _orderRepo.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Payment>>(), Arg.Any<CancellationToken>()).Returns(payment);

        await Assert.ThrowsAsync<PaymentException>(() => CreateService().CancelOrderAsync(1));
    }

    // ---------- Refund ----------

    [Fact]
    public async Task Refund_PartialThenSecond_AccumulatesAndFullyRefunds()
    {
        var order = OrderWithTotal(100m);
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();
        var payment = MakeCapturedPayment(100m);
        _orderRepo.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Payment>>(), Arg.Any<CancellationToken>()).Returns(payment);

        var svc = CreateService();
        await svc.RefundOrderAsync(1, Buyer, 40m, "k1");
        var after = await svc.RefundOrderAsync(1, Buyer, 60m, "k2");

        Assert.Equal(100m, after.TotalRefunded());
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(2, _gateway.Refunds.Count);
    }

    [Fact]
    public async Task Refund_OverCap_IsRejected()
    {
        var order = OrderWithTotal(100m);
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();
        var payment = MakeCapturedPayment(100m);
        _orderRepo.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Payment>>(), Arg.Any<CancellationToken>()).Returns(payment);

        await Assert.ThrowsAsync<PaymentException>(() => CreateService().RefundOrderAsync(1, Buyer, 150m, "k1"));
        Assert.Empty(_gateway.Refunds); // never sent to the processor
    }

    [Fact]
    public async Task Refund_SameIdempotencyKeyTwice_RefundsOnceOnly()
    {
        var order = OrderWithTotal(100m);
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();
        var payment = MakeCapturedPayment(100m);
        _orderRepo.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Payment>>(), Arg.Any<CancellationToken>()).Returns(payment);

        var svc = CreateService();
        await svc.RefundOrderAsync(1, Buyer, 30m, "dup");
        await svc.RefundOrderAsync(1, Buyer, 30m, "dup");

        Assert.Single(_gateway.Refunds); // only one refund actually issued
        Assert.Equal(30m, payment.TotalRefunded());
    }

    [Fact]
    public async Task Refund_ForAnotherBuyersOrder_IsNotFound()
    {
        var order = OrderWithTotal(100m, "other@test.com");
        _orderRepo.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(order);

        await Assert.ThrowsAsync<PaymentNotFoundException>(() => CreateService().RefundOrderAsync(1, Buyer, 10m, "k"));
    }

    // ---------- helpers ----------

    private static Payment MakeAuthorizedPayment(decimal amount, DateTimeOffset? expiry = null) =>
        new(1, Buyer, "USD", amount, "PP-ORDER", "AUTH-1", "CREATED", expiry ?? DateTimeOffset.UtcNow.AddDays(3), null);

    private static Payment MakeCapturedPayment(decimal amount)
    {
        var p = MakeAuthorizedPayment(amount);
        p.MarkCaptured("CAP-1", "COMPLETED", amount, 3m, amount - 3m);
        return p;
    }
}
