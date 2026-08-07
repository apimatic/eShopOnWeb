using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class PaymentServiceTests
{
    private readonly IRepository<Order> _orderRepository = Substitute.For<IRepository<Order>>();
    private readonly IReadRepository<SavedPaymentMethod> _savedMethodRepository = Substitute.For<IReadRepository<SavedPaymentMethod>>();
    private readonly IPayPalClient _payPalClient = Substitute.For<IPayPalClient>();
    private readonly IAppLogger<PaymentService> _logger = Substitute.For<IAppLogger<PaymentService>>();

    private readonly OrderBuilder _orderBuilder = new();
    private const string BuyerId = "12345"; // matches OrderBuilder.TestBuyerId

    private PaymentService CreateService() =>
        new(_orderRepository, _savedMethodRepository, _payPalClient, _logger);

    private void GivenOrder(Order order) =>
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(order);

    [Fact]
    public async Task PayWithCard_CapturesAndMarksOrderPaid()
    {
        var order = _orderBuilder.WithDefaultValues();
        GivenOrder(order);
        _payPalClient.CreateCardOrderAsync(Arg.Any<decimal>(), "USD", Arg.Any<CardPaymentDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalPaymentResult("PPO-1", "CAP-1", "COMPLETED"));

        var result = await CreateService().PayOrderWithCardAsync(1, BuyerId, new CardPaymentDetails());

        Assert.Equal(OrderPaymentStatus.Paid, result.PaymentStatus);
        Assert.Equal("CAP-1", result.PaymentCaptureId);
        await _orderRepository.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayWithCard_WhenAlreadyPaid_DoesNotChargeAgain()
    {
        var order = _orderBuilder.WithDefaultValues();
        order.SetPaid("PPO-1", "CAP-1");
        GivenOrder(order);

        var result = await CreateService().PayOrderWithCardAsync(1, BuyerId, new CardPaymentDetails());

        Assert.Equal(OrderPaymentStatus.Paid, result.PaymentStatus);
        await _payPalClient.DidNotReceive().CreateCardOrderAsync(
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardPaymentDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_WhenOrderNotOwnedByCaller_ThrowsNotFound_AndDoesNotCharge()
    {
        var order = _orderBuilder.WithDefaultValues(); // BuyerId = "12345"
        GivenOrder(order);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            CreateService().PayOrderWithCardAsync(1, "someone-else", new CardPaymentDetails()));

        await _payPalClient.DidNotReceive().CreateCardOrderAsync(
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardPaymentDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayWithSavedMethod_WhenMethodNotOwned_ThrowsNotFound()
    {
        var order = _orderBuilder.WithDefaultValues();
        GivenOrder(order);
        var othersCard = new SavedPaymentMethod("another-buyer", "vault-1", "cust-1", "VISA", "1111", "2030-01", "Someone");
        _savedMethodRepository.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(othersCard);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            CreateService().PayOrderWithSavedMethodAsync(1, BuyerId, 7));

        await _payPalClient.DidNotReceive().CreateVaultedCardOrderAsync(
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_WhenOrderNotPaid_Throws()
    {
        var order = _orderBuilder.WithDefaultValues(); // AwaitingPayment
        GivenOrder(order);

        await Assert.ThrowsAsync<OrderPaymentException>(() =>
            CreateService().RefundOrderAsync(1, BuyerId));
    }

    [Fact]
    public async Task Refund_WhenAlreadyRefunded_DoesNotRefundAgain()
    {
        var order = _orderBuilder.WithDefaultValues();
        order.SetPaid("PPO-1", "CAP-1");
        order.SetRefunded("REF-1");
        GivenOrder(order);

        var result = await CreateService().RefundOrderAsync(1, BuyerId);

        Assert.Equal(OrderPaymentStatus.Refunded, result.PaymentStatus);
        await _payPalClient.DidNotReceive().RefundCaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_WhenPaid_RefundsAndMarksRefunded()
    {
        var order = _orderBuilder.WithDefaultValues();
        order.SetPaid("PPO-1", "CAP-1");
        GivenOrder(order);
        _payPalClient.RefundCaptureAsync("CAP-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalRefundResult("REF-1", "COMPLETED"));

        var result = await CreateService().RefundOrderAsync(1, BuyerId);

        Assert.Equal(OrderPaymentStatus.Refunded, result.PaymentStatus);
        Assert.Equal("REF-1", result.PaymentRefundId);
        await _orderRepository.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }
}
