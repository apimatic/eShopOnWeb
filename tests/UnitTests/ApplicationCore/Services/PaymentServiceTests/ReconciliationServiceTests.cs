using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class ReconciliationServiceTests
{
    private readonly IReadRepository<Payment> _paymentRepo = Substitute.For<IReadRepository<Payment>>();
    private readonly FakePaymentGateway _gateway = new();

    [Fact]
    public async Task LinesUpMatchesAndSurfacesBothKindsOfMismatch()
    {
        // MarkCaptured / AddRefund stamp UtcNow, so anchor the range around now.
        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow.AddHours(1);
        var mid = DateTimeOffset.UtcNow;

        // Local payment captured as CAP-1 (known to both) plus a refund PayPal has not reported yet.
        var payment = new Payment(1, "b@test.com", "USD", 50m, "PP", "AUTH", "CAPTURED", null, null);
        payment.MarkCaptured("CAP-1", "COMPLETED", 50m, 2m, 48m);
        payment.AddRefund("REF-LOCAL-ONLY", 10m, "COMPLETED", "k1");
        _paymentRepo.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<Payment>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Payment> { payment });

        // PayPal reports the capture (match) and a transaction eShop has never seen.
        _gateway.Transactions.Add(new GatewayTransaction("CAP-1", "S", 50m, "USD", mid));
        _gateway.Transactions.Add(new GatewayTransaction("PAYPAL-ONLY", "S", 99m, "USD", mid));

        var report = await new ReconciliationService(_paymentRepo, _gateway).ReconcileAsync(from, to);

        Assert.Contains(report.Matched, l => l.TransactionId == "CAP-1");
        Assert.Contains(report.OnlyInPayPal, l => l.TransactionId == "PAYPAL-ONLY");
        Assert.Contains(report.OnlyInEShop, l => l.TransactionId == "REF-LOCAL-ONLY");
        Assert.DoesNotContain(report.OnlyInEShop, l => l.TransactionId == "CAP-1");
    }

    [Fact]
    public async Task EmptyPayPalRangeIsNotAnError()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero);
        _paymentRepo.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<Payment>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Payment>());

        var report = await new ReconciliationService(_paymentRepo, _gateway).ReconcileAsync(from, to);

        Assert.Empty(report.Matched);
        Assert.Empty(report.OnlyInPayPal);
        Assert.Empty(report.OnlyInEShop);
    }
}
