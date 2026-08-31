using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.ReconciliationServiceTests;

public class GetReport
{
    private readonly ITransactionSearch _transactionSearch = Substitute.For<ITransactionSearch>();
    private readonly IRepository<Payment> _paymentRepo = Substitute.For<IRepository<Payment>>();

    private ReconciliationService CreateService() => new ReconciliationService(_transactionSearch, _paymentRepo);

    private static Payment CreateCapturedPayment(int id, int orderId, string captureId)
    {
        var payment = new PaymentWithId(orderId, "buyer-1", 25m, "USD", id);
        payment.MarkAuthorized("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        payment.MarkCaptured(captureId, 25m, 1.15m, 23.85m, "COMPLETED");
        return payment;
    }

    private static GatewayTransaction CreateTransaction(string transactionId)
        => new GatewayTransaction(transactionId, null, null, null, null, null,
            DateTimeOffset.UtcNow, 25m, "USD", -1.15m, "S");

    [Fact]
    public async Task MatchesPayPalTransactionToLocalPaymentByCaptureId()
    {
        var payment = CreateCapturedPayment(11, 7, "CAP-1");
        _transactionSearch.SearchAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<GatewayTransaction> { CreateTransaction("CAP-1") });
        _paymentRepo.ListAsync(Arg.Any<PaymentsCreatedInRangeSpec>(), Arg.Any<CancellationToken>())
            .Returns(new List<Payment> { payment });

        var report = await CreateService().GetReportAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, default);

        var row = Assert.Single(report.Transactions);
        Assert.Equal("Matched", row.MatchState);
        Assert.Equal(7, row.MatchedOrderId);
        Assert.Equal(11, row.MatchedPaymentId);
        Assert.Empty(report.UnmatchedLocalPayments);
    }

    [Fact]
    public async Task FlagsTransactionOnlyPayPalKnowsAbout()
    {
        _transactionSearch.SearchAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<GatewayTransaction> { CreateTransaction("CAP-UNKNOWN") });
        _paymentRepo.ListAsync(Arg.Any<PaymentsCreatedInRangeSpec>(), Arg.Any<CancellationToken>())
            .Returns(new List<Payment>());

        var report = await CreateService().GetReportAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, default);

        var row = Assert.Single(report.Transactions);
        Assert.Equal("OnlyInPayPal", row.MatchState);
        Assert.Null(row.MatchedOrderId);
    }

    [Fact]
    public async Task FlagsPaymentOnlyEShopKnowsAbout()
    {
        var payment = CreateCapturedPayment(11, 7, "CAP-1");
        _transactionSearch.SearchAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<GatewayTransaction>());
        _paymentRepo.ListAsync(Arg.Any<PaymentsCreatedInRangeSpec>(), Arg.Any<CancellationToken>())
            .Returns(new List<Payment> { payment });

        var report = await CreateService().GetReportAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, default);

        Assert.Empty(report.Transactions);
        var unmatched = Assert.Single(report.UnmatchedLocalPayments);
        Assert.Equal("OnlyInEShop", unmatched.MatchState);
        Assert.Equal(7, unmatched.OrderId);
        Assert.Equal("CAP-1", unmatched.CaptureId);
    }

    private class PaymentWithId : Payment
    {
        public PaymentWithId(int orderId, string buyerId, decimal amount, string currency, int id)
            : base(orderId, buyerId, amount, currency)
        {
            Id = id;
        }
    }
}
