using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.ReconciliationServiceTests;

public class ReconciliationServiceTests
{
    private readonly IPayPalPaymentService _payPal = Substitute.For<IPayPalPaymentService>();
    private readonly IReadRepository<Payment> _payments = Substitute.For<IReadRepository<Payment>>();

    private static Payment PaymentThatReachedPayPal(int orderId, string invoiceRef)
    {
        var p = new Payment(orderId, "buyer", 20m, "USD", invoiceRef);
        p.RecordAuthorization($"O{orderId}", $"A{orderId}", "CREATED", null, null);
        return p;
    }

    [Fact]
    public async Task LinesUpPayPalTransactionsAgainstEShopPayments()
    {
        var matched = PaymentThatReachedPayPal(1, "INV-1");
        var eshopOnly = PaymentThatReachedPayPal(2, "INV-2"); // no PayPal txn for this one
        _payments.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<Payment> { matched, eshopOnly });

        _payPal.SearchTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<PayPalTransaction>
            {
                new("T-1", "S", 20m, "USD", 1m, "INV-1", null),          // matches INV-1
                new("T-X", "S", 99m, "USD", 3m, "INV-UNKNOWN", null),    // PayPal-only
            });

        var svc = new ReconciliationService(_payPal, _payments);
        var report = await svc.ReconcileAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        Assert.Equal(2, report.PayPalTransactionCount);
        Assert.Equal(1, report.MatchedCount);
        Assert.Equal(1, report.PayPalOnlyCount);
        Assert.Equal(1, report.EShopOnlyCount);

        var matchedLine = report.Lines.Single(l => l.Disposition == "Matched");
        Assert.Equal("INV-1", matchedLine.InvoiceReference);
        Assert.Equal(1, matchedLine.OrderId);
        Assert.Equal("T-1", matchedLine.PayPalTransactionId);
    }

    [Fact]
    public async Task RejectsInvertedRange()
    {
        var svc = new ReconciliationService(_payPal, _payments);
        await Assert.ThrowsAsync<PaymentFlowException>(() =>
            svc.ReconcileAsync(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1)));
    }
}
