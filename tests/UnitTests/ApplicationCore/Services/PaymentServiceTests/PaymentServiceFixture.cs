using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

/// <summary>
/// Wires a <see cref="PaymentService"/> over substituted repositories and a substituted gateway, so
/// the tests assert what the service decides rather than what any SDK does.
/// </summary>
public class PaymentServiceFixture
{
    public const string BuyerId = "12345";
    public const string OtherBuyerId = "someone-else@example.com";

    public IRepository<Order> OrderRepository { get; } = Substitute.For<IRepository<Order>>();
    public IRepository<Payment> PaymentRepository { get; } = Substitute.For<IRepository<Payment>>();
    public IReadRepository<CatalogItem> CatalogItemRepository { get; } = Substitute.For<IReadRepository<CatalogItem>>();
    public IReadRepository<SavedCard> SavedCardRepository { get; } = Substitute.For<IReadRepository<SavedCard>>();
    public IPaymentGateway Gateway { get; } = Substitute.For<IPaymentGateway>();
    public IUriComposer UriComposer { get; } = Substitute.For<IUriComposer>();
    public IAppLogger<PaymentService> Logger { get; } = Substitute.For<IAppLogger<PaymentService>>();

    public PaymentServiceFixture()
    {
        Gateway.CurrencyCode.Returns("USD");
        PaymentRepository.AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<Payment>()));
        OrderRepository.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<Order>()));
    }

    public PaymentService Build() => new(
        OrderRepository, PaymentRepository, CatalogItemRepository, SavedCardRepository,
        Gateway, UriComposer, new OrderLockProvider(), Logger);

    public Order GivenOrder(OrderLifecycleStatus status = OrderLifecycleStatus.AwaitingPayment,
        string buyerId = BuyerId)
    {
        var order = buyerId == BuyerId
            ? new OrderBuilder().WithDefaultValues()
            : new Order(buyerId, new AddressBuilder().WithDefaultValues(),
                new List<OrderItem> { new(new CatalogItemOrdered(1, "Item", "uri"), 1.23m, 3) });

        switch (status)
        {
            case OrderLifecycleStatus.Authorized:
                order.MarkAuthorized();
                break;
            case OrderLifecycleStatus.Fulfilled:
                order.MarkAuthorized();
                order.MarkFulfilled();
                break;
            case OrderLifecycleStatus.Cancelled:
                order.MarkCancelled();
                break;
        }

        OrderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(order);

        return order;
    }

    public Payment GivenPayment(Payment? payment)
    {
        PaymentRepository.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(payment);
        return payment!;
    }

    public static Payment AuthorizedPayment(decimal amount = 3.69m, string? expiresAt = null)
    {
        var payment = new Payment(0, BuyerId, amount, "USD", "eshop-0-20260828120000");
        payment.BeginAuthorizationAttempt();
        payment.RecordAuthorization("PP-ORDER", "PP-AUTH-1", "CREATED",
            expiresAt is null ? null : System.DateTimeOffset.Parse(expiresAt));
        return payment;
    }

    public static Payment CapturedPayment(decimal amount = 3.69m)
    {
        var payment = AuthorizedPayment(amount);
        payment.RecordCapture("PP-CAPTURE", "COMPLETED", amount, 0.30m, amount - 0.30m);
        return payment;
    }
}
