using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class ReconciliationServiceTests
{
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IReadRepository<Payment> _payments = Substitute.For<IReadRepository<Payment>>();
    private readonly ICurrencyProvider _currency = Substitute.For<ICurrencyProvider>();

    public ReconciliationServiceTests()
    {
        _currency.CurrencyCode.Returns("USD");
    }

    private static Payment AuthorizedPayment(string invoiceId, string orderPpId)
    {
        var payment = new Payment(1, "buyer@example.com", 25m, "USD");
        payment.AssignInvoiceId(invoiceId);
        payment.MarkAuthorized(orderPpId, "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), "VISA", "1111");
        return payment;
    }

    [Fact]
    public async Task LinesUpPayPalTransactionsAgainstEShopPaymentsOverRangeWithData()
    {
        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow.AddDays(1);

        // eShop knows one payment.
        var payment = AuthorizedPayment("ESHOP-run-1", "PP-ORDER-1");
        _payments.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<Payment> { payment });

        // PayPal reports two transactions: one that correlates (same invoice), one that does not.
        _gateway.SearchTransactionsAsync(from, to, Arg.Any<CancellationToken>()).Returns(new List<PayPalTransaction>
        {
            new("TXN-MATCH", "ESHOP-run-1", null, "S", "T0006", new Money("USD", 25m), new Money("USD", 1m), DateTimeOffset.UtcNow),
            new("TXN-STRANGER", "SOMEONE-ELSE", null, "S", "T0006", new Money("USD", 9m), null, DateTimeOffset.UtcNow)
        });

        var service = new ReconciliationService(_gateway, _payments, _currency);
        var report = await service.ReconcileAsync(from, to);

        Assert.Equal(2, report.PayPalTransactionCount);
        Assert.Equal(1, report.EShopPaymentCount);
        Assert.Single(report.Matched);
        Assert.Equal("TXN-MATCH", report.Matched[0].PayPalTransactionId);
        Assert.Equal(1, report.Matched[0].OrderId);
        Assert.Single(report.MissingInEShop);
        Assert.Equal("TXN-STRANGER", report.MissingInEShop[0].PayPalTransactionId);
        Assert.Empty(report.MissingInPayPal);
    }

    [Fact]
    public async Task FlagsEShopPaymentPayPalHasNotReported()
    {
        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow.AddDays(1);

        var payment = AuthorizedPayment("ESHOP-run-2", "PP-ORDER-2");
        _payments.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<Payment> { payment });
        _gateway.SearchTransactionsAsync(from, to, Arg.Any<CancellationToken>())
            .Returns(new List<PayPalTransaction>()); // reporting lag: nothing yet

        var service = new ReconciliationService(_gateway, _payments, _currency);
        var report = await service.ReconcileAsync(from, to);

        Assert.Empty(report.Matched);
        Assert.Single(report.MissingInPayPal);
        Assert.Equal("ESHOP-run-2", report.MissingInPayPal[0].InvoiceId);
    }
}
