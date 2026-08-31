using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.eShopWeb.Infrastructure.Payments.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Payments;

public class ReconciliationServiceTests
{
    private static readonly DateTimeOffset From = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 8, 31, 23, 59, 59, TimeSpan.Zero);

    private readonly IPayPalClient _payPalClient = Substitute.For<IPayPalClient>();
    private readonly IReadRepository<OrderPayment> _paymentRepository = Substitute.For<IReadRepository<OrderPayment>>();
    private readonly ReconciliationService _service;

    public ReconciliationServiceTests()
    {
        _service = new ReconciliationService(_payPalClient, _paymentRepository,
            NullLogger<ReconciliationService>.Instance);

        _payPalClient
            .SearchTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalTransactionSearchResponse
            {
                TransactionDetails = new List<PayPalTransactionDetail>(),
                TotalPages = 1
            });
        _paymentRepository
            .ListAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderPayment>());
    }

    private static PayPalTransactionDetail Transaction(string id, string amount = "10.00")
        => new()
        {
            TransactionInfo = new PayPalTransactionInfo
            {
                TransactionId = id,
                TransactionEventCode = "T0006",
                TransactionStatus = "S",
                TransactionInitiationDate = From.AddDays(5),
                TransactionAmount = new PayPalMoney { CurrencyCode = "USD", Value = amount },
                FeeAmount = new PayPalMoney { CurrencyCode = "USD", Value = "-0.59" }
            }
        };

    private static OrderPayment CapturedPayment(int orderId, string authorizationId, string captureId)
    {
        var payment = new OrderPayment(orderId, "buyer@example.com", 10m, "USD");
        payment.RecordAuthorization("PP-ORDER-" + orderId, authorizationId, "CREATED", From.AddDays(30));
        payment.RecordCapture(captureId, "COMPLETED", 10m, 0.59m, 9.41m, From.AddDays(6));
        return payment;
    }

    private void GivenLocalPayments(params OrderPayment[] payments)
        => _paymentRepository
            .ListAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>())
            .Returns(payments.ToList());

    private void GivenPayPalTransactions(params PayPalTransactionDetail[] transactions)
        => _payPalClient
            .SearchTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalTransactionSearchResponse
            {
                TransactionDetails = transactions.ToList(),
                TotalPages = 1
            });

    [Fact]
    public async Task MatchesTransactionToLocalAuthorization()
    {
        GivenLocalPayments(CapturedPayment(1, "AUTH-1", "CAP-1"));
        GivenPayPalTransactions(Transaction("AUTH-1"));

        var report = await _service.GetReconciliationAsync(From, To);

        var entry = Assert.Single(report.Transactions);
        Assert.Equal(1, entry.OrderId);
        Assert.Equal("authorization", entry.MatchedWith);
        Assert.Empty(report.MissingInEShop);
    }

    [Fact]
    public async Task MatchesTransactionToLocalCapture()
    {
        GivenLocalPayments(CapturedPayment(2, "AUTH-2", "CAP-2"));
        GivenPayPalTransactions(Transaction("CAP-2"));

        var report = await _service.GetReconciliationAsync(From, To);

        var entry = Assert.Single(report.Transactions);
        Assert.Equal(2, entry.OrderId);
        Assert.Equal("capture", entry.MatchedWith);
    }

    [Fact]
    public async Task MatchesTransactionToLocalRefund()
    {
        var payment = CapturedPayment(3, "AUTH-3", "CAP-3");
        payment.AddRefund("REF-3", "COMPLETED", 4m, "key-1", null);
        GivenLocalPayments(payment);
        GivenPayPalTransactions(Transaction("REF-3"));

        var report = await _service.GetReconciliationAsync(From, To);

        var entry = Assert.Single(report.Transactions);
        Assert.Equal(3, entry.OrderId);
        Assert.Equal("refund", entry.MatchedWith);
    }

    [Fact]
    public async Task PayPalTransactionWithNoLocalMatchIsMissingInEShop()
    {
        GivenPayPalTransactions(Transaction("CAP-UNKNOWN"));

        var report = await _service.GetReconciliationAsync(From, To);

        var entry = Assert.Single(report.MissingInEShop);
        Assert.Equal("CAP-UNKNOWN", entry.TransactionId);
        Assert.Null(entry.OrderId);
        Assert.Null(entry.MatchedWith);
    }

    [Fact]
    public async Task LocalActivityNotReportedByPayPalIsMissingInPayPal()
    {
        var payment = CapturedPayment(4, "AUTH-4", "CAP-4");
        payment.AddRefund("REF-4", "COMPLETED", 4m, "key-1", null);
        GivenLocalPayments(payment);

        var report = await _service.GetReconciliationAsync(From, To);

        Assert.Equal(3, report.MissingInPayPal.Count);
        Assert.Contains(report.MissingInPayPal, r => r.RecordType == "authorization" && r.ProcessorId == "AUTH-4");
        Assert.Contains(report.MissingInPayPal, r => r.RecordType == "capture" && r.ProcessorId == "CAP-4");
        Assert.Contains(report.MissingInPayPal, r => r.RecordType == "refund" && r.ProcessorId == "REF-4");
        Assert.All(report.MissingInPayPal, r => Assert.Equal(4, r.OrderId));
    }

    [Fact]
    public async Task ReportedTransactionsAreNotListedAsMissingInPayPal()
    {
        GivenLocalPayments(CapturedPayment(5, "AUTH-5", "CAP-5"));
        GivenPayPalTransactions(Transaction("AUTH-5"), Transaction("CAP-5"));

        var report = await _service.GetReconciliationAsync(From, To);

        Assert.Empty(report.MissingInPayPal);
        Assert.Empty(report.MissingInEShop);
        Assert.Equal(2, report.Transactions.Count);
    }

    [Fact]
    public async Task WalksEveryPageOfTheSearchResults()
    {
        _payPalClient
            .SearchTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
                1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalTransactionSearchResponse
            {
                TransactionDetails = new List<PayPalTransactionDetail> { Transaction("CAP-A") },
                TotalPages = 2
            });
        _payPalClient
            .SearchTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
                2, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalTransactionSearchResponse
            {
                TransactionDetails = new List<PayPalTransactionDetail> { Transaction("CAP-B") },
                TotalPages = 2
            });

        var report = await _service.GetReconciliationAsync(From, To);

        Assert.Equal(2, report.Transactions.Count);
        Assert.Contains(report.Transactions, t => t.TransactionId == "CAP-A");
        Assert.Contains(report.Transactions, t => t.TransactionId == "CAP-B");
        await _payPalClient.Received(1).SearchTransactionsAsync(From, To, 2, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayPalFailureSurfacesAsPaymentException()
    {
        _payPalClient
            .SearchTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Throws(new PayPalApiException(System.Net.HttpStatusCode.BadRequest, "VALIDATION_ERROR", "Invalid range", "dbg-1"));

        await Assert.ThrowsAsync<PaymentException>(() => _service.GetReconciliationAsync(From, To));
    }
}
