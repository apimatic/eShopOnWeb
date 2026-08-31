using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace PublicApiIntegrationTests.Invoicing;

/// <summary>
/// A deterministic in-memory stand-in for the real Visa provider, so the invoicing endpoints can be
/// tested without calling the sandbox. It mirrors the provider's observable behavior: bills are raised
/// in DRAFT with no payment link, sending yields SENT plus a payment link, and cancelling clears it.
/// It also carries pre-seeded "foreign" bills (not raised by eShop) so reconciliation can be exercised.
/// </summary>
public class FakeInvoiceProvider : IInvoiceProvider
{
    private sealed class Record
    {
        public string Id = string.Empty;
        public string Status = "DRAFT";
        public string? PaymentLink;
        public DateOnly? DueDate;
        public DateTimeOffset CreatedDate;
        public decimal? TotalAmount;
        public string? Currency;
        public string? CustomerName;
        public string? CustomerEmail;
        public List<ProviderInvoiceEvent> History = new();
        public bool EShopRaised;
    }

    private readonly ConcurrentDictionary<string, Record> _store = new();

    /// <summary>A fixed instant the seeded foreign bills are dated at, for range assertions.</summary>
    public static readonly DateTimeOffset SeedInstant = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    public FakeInvoiceProvider()
    {
        // Two bills that belong to other activity on the shared account (not eShop's).
        foreach (var id in new[] { "FOREIGN-A", "FOREIGN-B" })
        {
            _store[id] = new Record
            {
                Id = id,
                Status = "SENT",
                CreatedDate = SeedInstant,
                TotalAmount = 9.99m,
                Currency = "USD",
                CustomerName = "Someone Else",
                EShopRaised = false,
                History = { new ProviderInvoiceEvent("DRAFT", SeedInstant) }
            };
        }
    }

    public Task<ProviderInvoice> CreateInvoiceAsync(CreateProviderInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var record = new Record
        {
            Id = request.InvoiceNumber,
            Status = "DRAFT",
            DueDate = request.DueDate,
            CreatedDate = now,
            TotalAmount = request.TotalAmount,
            Currency = request.Currency,
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail,
            EShopRaised = true,
            History = { new ProviderInvoiceEvent("DRAFT", now) }
        };
        _store[record.Id] = record;
        return Task.FromResult(ToProviderInvoice(record));
    }

    public Task<ProviderInvoice> GetInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var record = Find(providerInvoiceId);
        return Task.FromResult(ToProviderInvoice(record));
    }

    public Task<ProviderInvoice> UpdateInvoiceAsync(string providerInvoiceId, UpdateProviderInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var record = Find(providerInvoiceId);
        if (record.Status is "CANCELED" or "PAID")
        {
            throw new InvoiceProviderException($"cannot update an invoice in status {record.Status}", 400, 400);
        }

        record.DueDate = request.DueDate;
        record.CustomerName = request.CustomerName;
        record.CustomerEmail = request.CustomerEmail;
        record.TotalAmount = request.TotalAmount;
        record.Currency = request.Currency;
        record.History.Insert(0, new ProviderInvoiceEvent("UPDATE", DateTimeOffset.UtcNow));
        return Task.FromResult(ToProviderInvoice(record));
    }

    public Task<ProviderInvoice> IssueInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var record = Find(providerInvoiceId);
        if (record.Status == "CANCELED")
        {
            throw new InvoiceProviderException("cannot send a canceled invoice", 400, 400);
        }

        record.Status = "SENT";
        record.PaymentLink = $"https://sandbox.invoicing.example/pay/{record.Id}";
        record.History.Insert(0, new ProviderInvoiceEvent("SEND", DateTimeOffset.UtcNow));
        return Task.FromResult(ToProviderInvoice(record));
    }

    public Task<ProviderInvoice> WithdrawInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var record = Find(providerInvoiceId);
        if (record.Status == "PAID")
        {
            throw new InvoiceProviderException("cannot cancel a paid invoice", 400, 400);
        }

        record.Status = "CANCELED";
        record.PaymentLink = null;
        record.History.Insert(0, new ProviderInvoiceEvent("CANCEL", DateTimeOffset.UtcNow));
        return Task.FromResult(ToProviderInvoice(record));
    }

    public Task<IReadOnlyList<ProviderInvoiceSummary>> ListInvoicesCreatedBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var summaries = _store.Values
            .Where(r => r.CreatedDate >= from && r.CreatedDate <= to)
            .Select(r => new ProviderInvoiceSummary
            {
                Id = r.Id,
                Status = r.Status,
                CreatedDate = r.CreatedDate,
                TotalAmount = r.TotalAmount,
                Currency = r.Currency,
                CustomerName = r.CustomerName,
                DueDate = r.DueDate
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<ProviderInvoiceSummary>>(summaries);
    }

    private Record Find(string id) =>
        _store.TryGetValue(id, out var record)
            ? record
            : throw new InvoiceProviderException($"invoice {id} was not found", 404, 404);

    private static ProviderInvoice ToProviderInvoice(Record r) => new()
    {
        Id = r.Id,
        Status = r.Status,
        PaymentLink = r.PaymentLink,
        DueDate = r.DueDate,
        CreatedDate = r.CreatedDate,
        TotalAmount = r.TotalAmount,
        Currency = r.Currency,
        CustomerName = r.CustomerName,
        CustomerEmail = r.CustomerEmail,
        History = r.History.ToList()
    };
}
