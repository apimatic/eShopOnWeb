using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.ReconciliationServiceTests;

public class BuildReportAsync
{
    private readonly IRepository<Order> _mockOrderRepo = Substitute.For<IRepository<Order>>();
    private readonly IPaymentGateway _mockGateway = Substitute.For<IPaymentGateway>();

    private static Order CreateFulfilledOrder(string captureId, decimal capturedAmount)
    {
        var order = new Order("buyer@test.com", new Address("1 St", "City", "ST", "USA", "00000"),
            new List<OrderItem> { new(new CatalogItemOrdered(1, "Item", "pic.png"), capturedAmount, 1) });

        var payment = new OrderPayment(order.Id, "USD", capturedAmount, null, "paypal-order-1", "auth-1", "CREATED",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3));
        order.AttachPayment(payment);
        payment.RecordCapture(captureId, "COMPLETED", capturedAmount, 1.00m, capturedAmount - 1.00m, DateTimeOffset.UtcNow);
        order.MarkFulfilled();
        return order;
    }

    [Fact]
    public async Task MatchesOrderWhenTransactionIdEqualsCaptureId()
    {
        // PayPal's Transaction Search reports the capture under transaction_id == the Payments v2 capture id -
        // this reflects what was directly verified against a live PayPal sandbox account.
        var order = CreateFulfilledOrder("CAPTURE-123", 17.00m);
        _mockOrderRepo.ListAsync(Arg.Any<OrdersWithPaymentInDateRangeSpecification>(), default)
            .Returns(new List<Order> { order });

        var transaction = new GatewayTransaction("CAPTURE-123", "SOME-OTHER-ID", "TXN", "SUCCESS", "T0005",
            17.00m, "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _mockGateway.SearchTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), default)
            .Returns(new List<GatewayTransaction> { transaction });

        var sut = new ReconciliationService(_mockOrderRepo, _mockGateway);
        var report = await sut.BuildReportAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);

        Assert.Single(report.Matched);
        Assert.Equal(order.Id, report.Matched[0].OrderId);
        Assert.False(report.Matched[0].AmountMismatch);
        Assert.Empty(report.PayPalOnly);
        Assert.Empty(report.EShopOnly);
    }

    [Fact]
    public async Task DoesNotMatchOnPayPalReferenceIdOfTypeOdr()
    {
        // Regression guard: an earlier version of this matcher joined on paypal_reference_id_type=="ODR",
        // which live sandbox data proved never occurs for card captures/refunds (reference type is "TXN",
        // pointing at a preceding transaction, not the Orders v2 id). That version silently matched nothing.
        var order = CreateFulfilledOrder("CAPTURE-123", 17.00m);
        _mockOrderRepo.ListAsync(Arg.Any<OrdersWithPaymentInDateRangeSpecification>(), default)
            .Returns(new List<Order> { order });

        var transaction = new GatewayTransaction("SOME-UNRELATED-TXN-ID", "paypal-order-1", "ODR", "SUCCESS",
            "T0005", 17.00m, "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _mockGateway.SearchTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), default)
            .Returns(new List<GatewayTransaction> { transaction });

        var sut = new ReconciliationService(_mockOrderRepo, _mockGateway);
        var report = await sut.BuildReportAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);

        Assert.Empty(report.Matched);
        Assert.Single(report.PayPalOnly);
        Assert.Single(report.EShopOnly);
    }

    [Fact]
    public async Task FlagsAmountMismatchWhenCapturedAmountDiffersFromReportedAmount()
    {
        var order = CreateFulfilledOrder("CAPTURE-123", 17.00m);
        _mockOrderRepo.ListAsync(Arg.Any<OrdersWithPaymentInDateRangeSpecification>(), default)
            .Returns(new List<Order> { order });

        var transaction = new GatewayTransaction("CAPTURE-123", null, null, "SUCCESS", "T0005",
            25.00m, "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _mockGateway.SearchTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), default)
            .Returns(new List<GatewayTransaction> { transaction });

        var sut = new ReconciliationService(_mockOrderRepo, _mockGateway);
        var report = await sut.BuildReportAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);

        Assert.Single(report.Matched);
        Assert.True(report.Matched[0].AmountMismatch);
    }

    [Fact]
    public async Task ReportsEShopOnlyWhenPayPalHasNotYetReportedTheCapture()
    {
        // Expected sandbox result per the task: PayPal's own reporting lags live activity by up to ~3 hours.
        var order = CreateFulfilledOrder("CAPTURE-123", 17.00m);
        _mockOrderRepo.ListAsync(Arg.Any<OrdersWithPaymentInDateRangeSpecification>(), default)
            .Returns(new List<Order> { order });
        _mockGateway.SearchTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), default)
            .Returns(new List<GatewayTransaction>());

        var sut = new ReconciliationService(_mockOrderRepo, _mockGateway);
        var report = await sut.BuildReportAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);

        Assert.Empty(report.Matched);
        Assert.Empty(report.PayPalOnly);
        Assert.Single(report.EShopOnly);
        Assert.Equal(order.Id, report.EShopOnly[0].Id);
    }
}
