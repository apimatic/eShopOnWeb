using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class ReconciliationServiceTests
{
    private readonly IPayPalReportingGateway _reporting = Substitute.For<IPayPalReportingGateway>();
    private readonly IReadRepository<Order> _orderRepository = Substitute.For<IReadRepository<Order>>();

    private static Order CapturedOrder(string reference, decimal gross)
    {
        var items = new List<OrderItem> { new OrderItem(new CatalogItemOrdered(1, "Item", "pic.png"), gross, 1) };
        var order = new Order("buyer", new Address("s", "c", "st", "US", "00000"), items);
        var payment = new Payment(reference, gross, "USD", "PPO", "auth");
        payment.SetAuthorization("AUTH", "CREATED", DateTimeOffset.UtcNow.AddDays(29));
        payment.SetCapture("CAP", "COMPLETED", gross, 1m, gross - 1m);
        order.AttachPayment(payment);
        order.MarkPaid();
        return order;
    }

    private static ReportedTransaction Txn(string id, string? invoiceId, string? customField, decimal amount) =>
        new(id, "S", amount, "USD", DateTimeOffset.UtcNow, invoiceId, customField, "REF");

    [Fact]
    public async Task Matches_by_invoice_reference_and_flags_both_kinds_of_discrepancy()
    {
        var orderA = CapturedOrder("ESHOP-run-1", 29m);
        var orderB = CapturedOrder("ESHOP-run-2", 12m); // no matching PayPal transaction

        _orderRepository.ListAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Order> { orderA, orderB });

        _reporting.SearchTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<ReportedTransaction>
            {
                Txn("T1", "ESHOP-run-1", null, 29m),        // matches orderA
                Txn("T2", "UNRELATED-INVOICE", "0", 100m),  // PayPal knows, eShop doesn't
            });

        var service = new ReconciliationService(_reporting, _orderRepository);
        var report = await service.ReconcileAsync(DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow);

        Assert.Equal(1, report.MatchedCount);
        Assert.Equal(1, report.MissingInEShopCount);   // T2
        Assert.Equal(1, report.MissingInPayPalCount);  // orderB

        var matched = report.Lines.Single(l => l.Status == ReconciliationStatus.Matched);
        Assert.Equal("ESHOP-run-1", matched.Reference);
        Assert.Equal("T1", matched.PayPalTransactionId);
    }

    [Fact]
    public async Task Custom_field_is_not_used_as_a_match_key()
    {
        // orderA's id is 0 in a unit context; a PayPal transaction whose custom_field equals that id
        // must NOT be treated as a match — only the unique invoice reference matches.
        var orderA = CapturedOrder("ESHOP-run-1", 29m);

        _orderRepository.ListAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Order> { orderA });

        _reporting.SearchTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<ReportedTransaction>
            {
                Txn("T9", invoiceId: null, customField: "0", amount: 500m),
            });

        var service = new ReconciliationService(_reporting, _orderRepository);
        var report = await service.ReconcileAsync(DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow);

        Assert.Equal(0, report.MatchedCount);
        Assert.Equal(1, report.MissingInEShopCount);   // the stray transaction
        Assert.Equal(1, report.MissingInPayPalCount);  // orderA still unmatched
    }

    [Fact]
    public async Task Empty_paypal_range_reports_captured_orders_as_missing_in_paypal()
    {
        // A lagged/empty PayPal range is expected, not a gap: eShop captures still surface.
        var orderA = CapturedOrder("ESHOP-run-1", 29m);
        _orderRepository.ListAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Order> { orderA });
        _reporting.SearchTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<ReportedTransaction>());

        var service = new ReconciliationService(_reporting, _orderRepository);
        var report = await service.ReconcileAsync(DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow);

        Assert.Equal(0, report.MatchedCount);
        Assert.Equal(0, report.MissingInEShopCount);
        Assert.Equal(1, report.MissingInPayPalCount);
    }
}
