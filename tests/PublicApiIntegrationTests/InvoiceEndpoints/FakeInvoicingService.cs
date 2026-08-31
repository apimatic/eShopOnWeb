using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace PublicApiIntegrationTests.InvoiceEndpoints;

/// <summary>
/// In-memory stand-in for the Visa/CyberSource provider so the endpoint tests exercise the ordering,
/// invoicing, ownership and state logic without touching the network. It hands out a unique provider
/// invoice id per raise, remembers what it raised so the reconciliation report can match it, and always
/// surfaces one "other activity" invoice the provider knows but eShop does not.
/// </summary>
public sealed class FakeInvoicingService : IInvoicingService
{
    public const string OtherActivityInvoiceId = "OTHER-ACTIVITY-1";

    private readonly List<(string Id, DateTimeOffset Created)> _raised = new();

    public Task<ProviderInvoice> RaiseInvoiceAsync(RaiseInvoiceCommand command, CancellationToken ct = default)
    {
        var id = "FAKE-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        _raised.Add((id, DateTimeOffset.UtcNow));
        return Task.FromResult(new ProviderInvoice { ProviderInvoiceId = id, Status = "DRAFT" });
    }

    public Task<ProviderInvoice> GetInvoiceAsync(string providerInvoiceId, CancellationToken ct = default) =>
        Task.FromResult(new ProviderInvoice
        {
            ProviderInvoiceId = providerInvoiceId,
            Status = "SENT",
            PaymentLink = $"https://pay.example.test/{providerInvoiceId}",
            History = new List<InvoiceHistoryEntry>
            {
                new() { Event = "CREATE", Date = DateTimeOffset.UtcNow.AddMinutes(-1) },
                new() { Event = "SEND", Date = DateTimeOffset.UtcNow }
            }
        });

    public Task<ProviderInvoice> AmendInvoiceAsync(string providerInvoiceId, AmendInvoiceCommand command, CancellationToken ct = default) =>
        Task.FromResult(new ProviderInvoice { ProviderInvoiceId = providerInvoiceId, Status = "CREATED" });

    public Task<ProviderInvoice> IssueInvoiceAsync(string providerInvoiceId, CancellationToken ct = default) =>
        Task.FromResult(new ProviderInvoice
        {
            ProviderInvoiceId = providerInvoiceId,
            Status = "SENT",
            PaymentLink = $"https://pay.example.test/{providerInvoiceId}"
        });

    public Task<ProviderInvoice> WithdrawInvoiceAsync(string providerInvoiceId, CancellationToken ct = default) =>
        Task.FromResult(new ProviderInvoice { ProviderInvoiceId = providerInvoiceId, Status = "CANCELED" });

    public Task<IReadOnlyList<ProviderInvoiceSummary>> ListInvoicesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var list = _raised
            .Where(r => r.Created >= from && r.Created <= to)
            .Select(r => new ProviderInvoiceSummary { ProviderInvoiceId = r.Id, Status = "SENT", CreatedDate = r.Created })
            .ToList();

        // A bill the provider account carries that is not this application's.
        list.Add(new ProviderInvoiceSummary
        {
            ProviderInvoiceId = OtherActivityInvoiceId,
            Status = "PAID",
            CreatedDate = DateTimeOffset.UtcNow
        });

        return Task.FromResult<IReadOnlyList<ProviderInvoiceSummary>>(list);
    }
}
