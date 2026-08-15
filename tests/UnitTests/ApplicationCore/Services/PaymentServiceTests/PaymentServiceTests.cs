using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.VaultAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class PaymentServiceTests
{
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _paymentRepo = Substitute.For<IRepository<Payment>>();
    private readonly IReadRepository<VaultedCard> _vaultRepo = Substitute.For<IReadRepository<VaultedCard>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IPaymentConfiguration _config = Substitute.For<IPaymentConfiguration>();

    private const string BuyerId = "12345"; // matches OrderBuilder.TestBuyerId

    private PaymentService CreateService() => new(_orderRepo, _paymentRepo, _vaultRepo, _gateway, _config);

    private static Order AuthorizedOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized();
        return order;
    }

    public PaymentServiceTests()
    {
        _config.Currency.Returns("USD");
    }

    private void OrderReturns(Order order) =>
        _orderRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(order);

    private void PaymentReturns(Payment? payment) =>
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<Payment>>(), Arg.Any<CancellationToken>()).Returns(payment);

    [Fact]
    public async Task Authorize_WithCard_CreatesAuthorizedPaymentAndMarksOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        OrderReturns(order);
        PaymentReturns(null);
        _gateway.AuthorizeWithCardAsync(Arg.Any<decimal>(), "USD", Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PPO", "AUTH1", "CREATED"));

        var instruction = new PaymentInstruction { Card = SampleCard() };
        var result = await CreateService().AuthorizeAsync(1, BuyerId, instruction);

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        Assert.NotNull(result.Payment);
        Assert.Equal(PaymentStatus.Authorized, result.Payment!.Status);
        Assert.Equal("AUTH1", result.Payment.AuthorizationId);
        await _paymentRepo.Received(1).AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_WrongBuyer_ThrowsNotFound()
    {
        OrderReturns(new OrderBuilder().WithDefaultValues());
        var instruction = new PaymentInstruction { Card = SampleCard() };

        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateService().AuthorizeAsync(1, "someone-else", instruction));
        await _gateway.DidNotReceive().AuthorizeWithCardAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_AlreadyAuthorized_ReturnsExistingWithoutCallingGateway()
    {
        var order = AuthorizedOrder();
        OrderReturns(order);
        var existing = new Payment(1, BuyerId, "USD", 29m, "PPO");
        existing.SetAuthorized("AUTH1", "CREATED");
        PaymentReturns(existing);

        var result = await CreateService().AuthorizeAsync(1, BuyerId, new PaymentInstruction { Card = SampleCard() });

        Assert.Same(existing, result.Payment);
        await _gateway.DidNotReceive().AuthorizeWithCardAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_WithBothCardAndSavedCard_IsInvalid()
    {
        var instruction = new PaymentInstruction { Card = SampleCard(), SavedCardId = 5 };
        await Assert.ThrowsAsync<OrderRequestInvalidException>(
            () => CreateService().AuthorizeAsync(1, BuyerId, instruction));
    }

    [Fact]
    public async Task Fulfil_WhenAuthorizationStale_ReauthorizesThenCaptures()
    {
        var order = AuthorizedOrder();
        OrderReturns(order);
        var payment = new Payment(1, BuyerId, "USD", order.Total(), "PPO");
        payment.SetAuthorized("AUTH1", "CREATED");
        PaymentReturns(payment);

        _gateway.CaptureAuthorizationAsync(Arg.Any<string>(), Arg.Any<decimal>(), "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new AuthorizationExpiredException("stale"),
                _ => new CaptureResult("CAP1", "COMPLETED", order.Total(), 1m, order.Total() - 1m, "USD"));
        _gateway.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), "USD", Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("", "AUTH2", "CREATED"));

        var result = await CreateService().FulfilAsync(1);

        await _gateway.Received(1).ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), "USD", Arg.Any<CancellationToken>());
        await _gateway.Received(2).CaptureAuthorizationAsync(Arg.Any<string>(), Arg.Any<decimal>(), "USD", Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(PaymentStatus.Captured, result.Payment!.Status);
        Assert.Equal("AUTH2", payment.AuthorizationId);
        Assert.Equal("CAP1", payment.CaptureId);
    }

    [Fact]
    public async Task Refund_SameIdempotencyKey_ReturnsExistingWithoutCallingGateway()
    {
        var order = AuthorizedOrder();
        order.MarkFulfilled();
        OrderReturns(order);

        var payment = new Payment(1, BuyerId, "USD", 29m, "PPO");
        payment.SetAuthorized("AUTH1", "CREATED");
        payment.SetCaptured("CAP1", "COMPLETED", 29m, 1m, 28m);
        var existingRefund = new PaymentRefund("key-1", "R1", 5m, "USD", "COMPLETED");
        payment.AddRefund(existingRefund);
        PaymentReturns(payment);

        var refund = await CreateService().RefundAsync(1, BuyerId, 5m, "key-1");

        Assert.Same(existingRefund, refund);
        await _gateway.DidNotReceive().RefundCaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_VoidsAuthorization()
    {
        var order = AuthorizedOrder();
        OrderReturns(order);
        var payment = new Payment(1, BuyerId, "USD", 29m, "PPO");
        payment.SetAuthorized("AUTH1", "CREATED");
        PaymentReturns(payment);

        var result = await CreateService().CancelAsync(1);

        await _gateway.Received(1).VoidAuthorizationAsync("AUTH1", Arg.Any<CancellationToken>());
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentStatus.Voided, result.Payment!.Status);
    }

    private static CardDetails SampleCard() =>
        new("4111111111111111", "2030-12", "123", "Demo User", null);
}
