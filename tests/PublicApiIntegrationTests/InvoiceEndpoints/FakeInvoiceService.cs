using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace PublicApiIntegrationTests.InvoiceEndpoints;

/// <summary>
/// A stand-in for the Visa-backed service so the endpoint tests exercise routing, JWT auth, role
/// gating and response shape without touching the provider. Records the operator actions it receives.
/// </summary>
public sealed class FakeInvoiceService : IInvoiceService
{
    public List<string> Issued { get; } = new();
    public List<string> Withdrawn { get; } = new();
    public int ReconcileCalls { get; private set; }

    private static InvoiceDetails Details(string invoiceId, string localStatus, string? paymentLink = null) => new()
    {
        InvoiceId = invoiceId,
        OrderId = 1,
        LocalStatus = localStatus,
        ProviderStatus = localStatus.ToUpperInvariant(),
        DueDate = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
        CustomerName = "Test Customer",
        CustomerEmail = "customer@example.com",
        Currency = "USD",
        Amount = 42m,
        PaymentLink = paymentLink,
        History = Array.Empty<InvoiceHistoryEntry>()
    };

    public Task<ServiceResult<InvoiceDetails>> RaiseInvoiceAsync(int orderId, string buyerId, DateTimeOffset dueDate,
        string? customerName, string? customerEmail, CancellationToken cancellationToken) =>
        Task.FromResult(ServiceResult<InvoiceDetails>.Ok(Details("INV-NEW", "Draft")));

    public Task<ServiceResult<InvoiceDetails>> GetInvoiceAsync(string invoiceId, string buyerId,
        CancellationToken cancellationToken) =>
        Task.FromResult(ServiceResult<InvoiceDetails>.Ok(Details(invoiceId, "Issued", "https://pay.example/" + invoiceId)));

    public Task<ServiceResult<InvoiceDetails>> CorrectInvoiceAsync(string invoiceId, string buyerId,
        DateTimeOffset? dueDate, string? customerName, string? customerEmail, CancellationToken cancellationToken) =>
        Task.FromResult(ServiceResult<InvoiceDetails>.Ok(Details(invoiceId, "Draft")));

    public Task<ServiceResult<InvoiceDetails>> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken)
    {
        Issued.Add(invoiceId);
        return Task.FromResult(ServiceResult<InvoiceDetails>.Ok(Details(invoiceId, "Issued", "https://pay.example/" + invoiceId)));
    }

    public Task<ServiceResult<InvoiceDetails>> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken)
    {
        Withdrawn.Add(invoiceId);
        return Task.FromResult(ServiceResult<InvoiceDetails>.Ok(Details(invoiceId, "Withdrawn")));
    }

    public Task<IReadOnlyList<InvoiceSummary>> GetInvoicesForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        IReadOnlyList<InvoiceSummary> list = new List<InvoiceSummary>
        {
            new() { InvoiceId = "INV-1", OrderId = 1, LocalStatus = "Issued", DueDate = DateTimeOffset.UtcNow, Amount = 42m, Currency = "USD" }
        };
        return Task.FromResult(list);
    }

    public Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        ReconcileCalls++;
        var entries = new List<ReconciliationEntry>
        {
            new() { InvoiceId = "INV-1", Presence = ReconciliationPresence.Matched, ProviderStatus = "SENT", OrderId = 1, LocalStatus = "Issued", Amount = 42m, Currency = "USD" },
            new() { InvoiceId = "OTHER-9", Presence = ReconciliationPresence.ProviderOnly, ProviderStatus = "SENT", ProviderCreatedDate = from }
        };
        return Task.FromResult(new ReconciliationReport
        {
            From = from,
            To = to,
            ProviderInvoiceCount = 2,
            EShopInvoiceCount = 1,
            MatchedCount = 1,
            ProviderOnlyCount = 1,
            EShopOnlyCount = 0,
            Entries = entries
        });
    }
}
