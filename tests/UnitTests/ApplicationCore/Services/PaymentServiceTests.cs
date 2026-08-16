using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class PaymentServiceTests
{
    private const string Buyer = "12345"; // matches OrderBuilder.TestBuyerId

    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<SavedCard> _savedCardRepo = Substitute.For<IRepository<SavedCard>>();
    private readonly IPayPalClient _payPal = Substitute.For<IPayPalClient>();
    private readonly IPaymentSettings _settings = Substitute.For<IPaymentSettings>();
    private readonly IAppLogger<PaymentService> _logger = Substitute.For<IAppLogger<PaymentService>>();

    private PaymentService CreateService()
    {
        _settings.Currency.Returns("USD");
        return new PaymentService(_orderRepo, _savedCardRepo, _payPal, new NoOpLock(), _settings, _logger);
    }

    private static Order CapturedOrder(decimal captured, params Refund[] refunds)
    {
        var order = new OrderBuilder().WithDefaultValues();
        var payment = new Payment(order.Id, "USD", "ESHOP-1-x", captured, "PPO", "AUTH",
            Payment.AuthCreated, DateTimeOffset.UtcNow.AddDays(29), "req", "VISA", "1111", null);
        order.SetAuthorizedPayment(payment);
        payment.MarkCaptured("CAP", "COMPLETED", captured, 1m, captured - 1m);
        order.MarkFulfilled();
        foreach (var r in refunds) payment.AddRefund(r);
        return order;
    }

    private void StubOrder(Order? order) =>
        _orderRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(order);

    [Fact]
    public async Task AuthorizeRejectsWhenBothCardAndSavedCardGiven()
    {
        var service = CreateService();
        var instruction = new PaymentInstruction(
            new CardDetails("4111111111111111", "2027-01", "123", "N", null), SavedCardId: 5, SaveCard: false);

        await Assert.ThrowsAsync<PaymentException>(() => service.AuthorizeAsync(Buyer, 1, instruction));
    }

    [Fact]
    public async Task AuthorizeThrowsNotFoundWhenOrderNotOwnedByBuyer()
    {
        StubOrder(null); // scoped spec returns nothing for a different buyer
        var service = CreateService();
        var instruction = new PaymentInstruction(
            new CardDetails("4111111111111111", "2027-01", "123", "N", null), null, false);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.AuthorizeAsync(Buyer, 1, instruction));
    }

    [Fact]
    public async Task RefundIsIdempotentPerKeyAndDoesNotCallPayPalTwice()
    {
        var existing = new Refund("dup-key", "R-EXISTING", 5m, "USD", Refund.StatusCompleted);
        StubOrder(CapturedOrder(29m, existing));
        var service = CreateService();

        var (refund, _) = await service.RefundAsync(Buyer, 1, 5m, "dup-key");

        Assert.Equal("R-EXISTING", refund.PayPalRefundId);
        await _payPal.DidNotReceive().RefundCaptureAsync(
            Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundRejectsAmountOverRefundableBalance()
    {
        // Captured 10, already refunded 5 -> only 5 remains.
        StubOrder(CapturedOrder(10m, new Refund("k1", "R1", 5m, "USD", Refund.StatusCompleted)));
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentException>(() => service.RefundAsync(Buyer, 1, 8m, "k2"));
        await _payPal.DidNotReceive().RefundCaptureAsync(
            Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundThrowsWhenOrderNotFulfilled()
    {
        var order = new OrderBuilder().WithDefaultValues(); // awaiting payment, no capture
        StubOrder(order);
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentException>(() => service.RefundAsync(Buyer, 1, null, "k"));
    }

    private sealed class NoOpLock : IPaymentOperationLock
    {
        public Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<IDisposable>(new Noop());

        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
}
