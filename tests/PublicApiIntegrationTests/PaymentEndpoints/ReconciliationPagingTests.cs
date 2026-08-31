using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class ReconciliationPagingTests
{
    [TestMethod]
    public async Task ExhaustsEveryPageAndSplitsRangesLongerThanPayPalMaximum()
    {
        await using var context = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase($"reconciliation-{Guid.NewGuid():N}").Options);
        var gateway = Substitute.For<IPayPalPaymentGateway>();
        var calls = new List<(DateTimeOffset From, DateTimeOffset To, int Page)>();
        gateway.SearchTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(), 500, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var from = call.ArgAt<DateTimeOffset>(0);
                var to = call.ArgAt<DateTimeOffset>(1);
                var page = call.ArgAt<int>(2);
                calls.Add((from, to, page));
                IReadOnlyList<PayPalTransaction> transactions = page == 1
                    ? new[]
                    {
                        new PayPalTransaction($"TXN-{from:MMdd}", null, null, "T0001", from, from, 1m,
                            0.1m, "USD", "S", null, null)
                    }
                    : Array.Empty<PayPalTransaction>();
                return new PayPalTransactionPage(transactions, page, 2, 1);
            });
        var workflow = new PaymentWorkflowService(context, gateway,
            Options.Create(new PayPalSettings { Currency = "USD" }), new PaymentOperationLock());
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var report = await workflow.ReconcileAsync(from, from.AddDays(65), default);

        Assert.AreEqual(6, calls.Count);
        Assert.IsTrue(calls.All(x => x.To - x.From <= TimeSpan.FromDays(30)));
        Assert.AreEqual(3, report.PayPalTransactions.Count);
    }
}
