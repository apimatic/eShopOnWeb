using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class BuildReconciliationReport
{
    private readonly IReadRepository<Payment> _payments = Substitute.For<IReadRepository<Payment>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();

    private static readonly DateTimeOffset From = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);

    public BuildReconciliationReport()
    {
        _gateway.CurrencyCode.Returns("USD");
    }

    private ReconciliationService Build() =>
        new(_payments, _gateway, Substitute.For<IAppLogger<ReconciliationService>>());

    private void GivenPayments(params Payment[] payments) =>
        _payments.ListAsync(Arg.Any<PaymentsOverlappingRangeSpecification>(), Arg.Any<CancellationToken>())
            .Returns(payments.ToList());

    private void GivenProviderTransactions(params GatewayTransaction[] transactions) =>
        _gateway.ListTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayTransactionPage(transactions, new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero)));

    private static Payment CapturedPayment(int orderId = 1, decimal amount = 17m,
        string invoiceId = "eshop-1-20260810120000")
    {
        var payment = new Payment(orderId, "buyer@example.com", amount, "USD", invoiceId);
        payment.BeginAuthorizationAttempt();
        payment.RecordAuthorization("PP-ORDER", "PP-AUTH", "CREATED", null);
        payment.RecordCapture("PP-CAPTURE", "COMPLETED", amount, 0.93m, amount - 0.93m);
        return payment;
    }

    private static GatewayTransaction Transaction(string id, decimal? amount = null,
        string? invoiceId = null, string? customField = null) =>
        new(id, "S", "T0006", amount, "USD", 0.93m, new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero),
            invoiceId, customField);

    [Fact]
    public async Task ACaptureIsMatchedByItsTransactionId()
    {
        GivenPayments(CapturedPayment());
        GivenProviderTransactions(Transaction("PP-CAPTURE", 17m));

        var report = await Build().BuildReportAsync(From, To, default);

        Assert.Equal(1, report.MatchedCount);
        Assert.Equal(0, report.OnlyAtPayPalCount);
        Assert.Equal(0, report.OnlyInEShopCount);

        var match = report.Matched.Single();
        Assert.Equal("transaction id", match.MatchedOn);
        Assert.Equal(1, match.OrderId);
        Assert.True(match.AmountsAgree);
    }

    [Fact]
    public async Task ATransactionWithAnUnfamiliarIdStillMatchesOnOurInvoiceReference()
    {
        GivenPayments(CapturedPayment());
        GivenProviderTransactions(Transaction("SOME-OTHER-ID", 17m, invoiceId: "eshop-1-20260810120000"));

        var report = await Build().BuildReportAsync(From, To, default);

        Assert.Equal("invoice id", report.Matched.Single().MatchedOn);
    }

    [Fact]
    public async Task ATransactionMatchesOnTheCustomFieldWhenNeitherIdLinesUp()
    {
        GivenPayments(CapturedPayment());
        GivenProviderTransactions(Transaction("SOME-OTHER-ID", 17m, customField: "eshop-1-20260810120000"));

        var report = await Build().BuildReportAsync(From, To, default);

        Assert.Equal("custom field", report.Matched.Single().MatchedOn);
    }

    [Fact]
    public async Task APaymentPayPalKnowsAboutAndEShopDoesNotIsReported()
    {
        GivenPayments();
        GivenProviderTransactions(Transaction("UNKNOWN-TXN", 42m));

        var report = await Build().BuildReportAsync(From, To, default);

        Assert.Equal(1, report.OnlyAtPayPalCount);
        Assert.Equal("UNKNOWN-TXN", report.OnlyAtPayPal.Single().TransactionId);
    }

    [Fact]
    public async Task APaymentEShopKnowsAboutAndPayPalDoesNotIsReportedWithTheLagCaveat()
    {
        GivenPayments(CapturedPayment());
        GivenProviderTransactions();

        var report = await Build().BuildReportAsync(From, To, default);

        Assert.Equal(1, report.OnlyInEShopCount);
        var orphan = report.OnlyInEShop.Single();
        Assert.Equal("PP-CAPTURE", orphan.CaptureId);
        // Reporting lags live activity, and the report says so rather than crying discrepancy.
        Assert.Contains("lags", orphan.Note);
    }

    [Fact]
    public async Task AVoidedPaymentIsNotReportedAsMissingBecauseNoMoneyEverMoved()
    {
        var payment = new Payment(2, "buyer@example.com", 12m, "USD", "eshop-2-20260810120000");
        payment.BeginAuthorizationAttempt();
        payment.RecordAuthorization("PP-ORDER-2", "PP-AUTH-2", "CREATED", null);
        payment.MarkVoided("VOIDED");

        GivenPayments(payment);
        GivenProviderTransactions();

        var report = await Build().BuildReportAsync(From, To, default);

        Assert.Equal(0, report.OnlyInEShopCount);
    }

    [Fact]
    public async Task ARefundIsMatchedAgainstItsOwnAmount_NotTheCapture()
    {
        var payment = CapturedPayment();
        payment.AddRefund("k", "PP-REFUND", "COMPLETED", 5m);
        GivenPayments(payment);

        // The processor reports a refund as a negative amount; magnitudes are what must agree.
        GivenProviderTransactions(Transaction("PP-REFUND", -5m));

        var report = await Build().BuildReportAsync(From, To, default);

        var match = report.Matched.Single();
        Assert.Equal(5m, match.EShopAmount);
        Assert.True(match.AmountsAgree);
    }

    [Fact]
    public async Task AnAmountDisagreementIsFlaggedRatherThanHidden()
    {
        GivenPayments(CapturedPayment());
        GivenProviderTransactions(Transaction("PP-CAPTURE", 16m));

        var report = await Build().BuildReportAsync(From, To, default);

        Assert.False(report.Matched.Single().AmountsAgree);
    }

    [Fact]
    public async Task AnInvertedRangeIsRejectedBeforeTheProcessorIsCalled()
    {
        await Assert.ThrowsAsync<PaymentValidationException>(() => Build().BuildReportAsync(To, From, default));

        await _gateway.DidNotReceive().ListTransactionsAsync(Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
