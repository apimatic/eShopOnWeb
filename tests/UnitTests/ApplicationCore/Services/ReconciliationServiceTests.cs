using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class ReconciliationServiceTests
{
    private readonly IPayPalClient _payPal = Substitute.For<IPayPalClient>();
    private readonly IReadRepository<Order> _orderRepo = Substitute.For<IReadRepository<Order>>();

    private static Order CapturedOrderWithCustomId(string customId)
    {
        var order = new OrderBuilder().WithDefaultValues();
        var payment = new Payment(order.Id, "USD", customId, 29m, "PPO", "AUTH",
            Payment.AuthCreated, DateTimeOffset.UtcNow.AddDays(29), "req", "VISA", "1111", null);
        order.SetAuthorizedPayment(payment);
        payment.MarkCaptured("CAP-123", "COMPLETED", 29m, 1m, 28m);
        order.MarkFulfilled();
        return order;
    }

    private static PayPalTransaction Txn(string id, string? customField, decimal amount) =>
        new(id, "S", amount, "USD", DateTimeOffset.UtcNow, "T0005", customField, customField, null);

    [Fact]
    public async Task MatchesPayPalTransactionToOrderByCustomId()
    {
        var order = CapturedOrderWithCustomId("ESHOP-1-abc");
        _orderRepo.ListAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Order> { order });
        _payPal.ListTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<PayPalTransaction> { Txn("PPTXN1", "ESHOP-1-abc", 29m) });

        var report = await new ReconciliationService(_payPal, _orderRepo)
            .BuildAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        Assert.Equal(1, report.MatchedCount);
        Assert.Equal(0, report.InPayPalNotInEShopCount);
        var matched = report.Lines.Single(l => l.MatchState == ReconciliationMatchState.Matched);
        Assert.Equal(order.Id, matched.OrderId);
    }

    [Fact]
    public async Task UnknownPayPalTransactionIsFlaggedPayPalOnly()
    {
        _orderRepo.ListAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Order>());
        _payPal.ListTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<PayPalTransaction> { Txn("STRANGE", "not-ours", 5m) });

        var report = await new ReconciliationService(_payPal, _orderRepo)
            .BuildAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        Assert.Equal(1, report.InPayPalNotInEShopCount);
        Assert.Equal(0, report.MatchedCount);
    }

    [Fact]
    public async Task CapturedOrderMissingFromPayPalReportIsFlaggedEShopOnly()
    {
        var order = CapturedOrderWithCustomId("ESHOP-1-xyz");
        _orderRepo.ListAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Order> { order });
        _payPal.ListTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<PayPalTransaction>()); // PayPal reporting has nothing yet (lag)

        var report = await new ReconciliationService(_payPal, _orderRepo)
            .BuildAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        Assert.Equal(1, report.InEShopNotInPayPalCount);
        var line = report.Lines.Single(l => l.MatchState == ReconciliationMatchState.InEShopNotInPayPal);
        Assert.Equal(order.Id, line.OrderId);
    }

    [Fact]
    public async Task PriorRunCustomIdDoesNotFalselyMatchDifferentReference()
    {
        var order = CapturedOrderWithCustomId("ESHOP-1-run-A");
        _orderRepo.ListAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Order> { order });
        // A leftover transaction from a prior run reused the bare "eshop-1"/"ESHOP-1" tag.
        _payPal.ListTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<PayPalTransaction> { Txn("OLD", "ESHOP-1", 12.34m), Txn("OLD2", "eshop-1", 5m) });

        var report = await new ReconciliationService(_payPal, _orderRepo)
            .BuildAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        Assert.Equal(0, report.MatchedCount);
        Assert.Equal(2, report.InPayPalNotInEShopCount);
    }
}
