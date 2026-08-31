using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class InvoicingService : IInvoicingService
{
    /// <summary>The account bills in USD; every bill eShop raises uses it.</summary>
    public const string BillingCurrency = "USD";

    /// <summary>
    /// Stamped onto each bill's provider-side merchantCustomerId so a reconciliation scan can tell eShop's
    /// bills apart from bills raised by other activity on the shared provider account.
    /// </summary>
    public const string MerchantReferencePrefix = "eShopOnWeb-Order-";

    // The provider list has no server-side date filter, so reconciliation pages the whole set. These bound
    // that scan so a large/looping provider result can never hang the request (see dotnet-configuration-resilience).
    private const int ReconciliationPageSize = 100;
    private const int ReconciliationMaxPages = 50;

    private readonly IRepository<Invoice> _invoiceRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IInvoiceProvider _provider;

    public InvoicingService(IRepository<Invoice> invoiceRepository,
        IRepository<Order> orderRepository,
        IInvoiceProvider provider)
    {
        _invoiceRepository = invoiceRepository;
        _orderRepository = orderRepository;
        _provider = provider;
    }

    public async Task<string> RaiseInvoiceAsync(int orderId, string buyerId, DateTimeOffset dueDate,
        string? customerName, string? customerEmail, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        // Unknown order and another shopper's order are deliberately indistinguishable.
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new InvoiceNotFoundException($"Order {orderId} was not found.");
        }

        var existing = await _invoiceRepository.ListAsync(new InvoicesByOrderSpecification(orderId), cancellationToken);
        if (existing.Any(i => i.State != InvoiceState.Withdrawn))
        {
            throw new InvoiceStateException($"Order {orderId} already has an active bill.");
        }

        var amount = order.Total();
        var merchantReference = MerchantReferenceFor(orderId);
        var description = DescriptionFor(orderId);
        var name = Fallback(customerName, buyerId);
        var email = Fallback(customerEmail, buyerId);

        var lines = order.OrderItems
            .Select(oi => new InvoiceLine(
                oi.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture),
                oi.ItemOrdered.ProductName, oi.Units, oi.UnitPrice, oi.UnitPrice * oi.Units))
            .ToList();

        var command = new RaiseInvoiceCommand(merchantReference, description, amount, BillingCurrency,
            dueDate, name, email, lines);

        var providerInvoice = await _provider.RaiseAsync(command, cancellationToken);
        if (string.IsNullOrEmpty(providerInvoice.Id))
        {
            throw new InvoiceProviderException("The provider did not return an invoice identifier.");
        }

        var invoice = new Invoice(orderId, buyerId, providerInvoice.Id, merchantReference, amount,
            BillingCurrency, dueDate, name, email, providerInvoice.Status);
        await _invoiceRepository.AddAsync(invoice, cancellationToken);

        return providerInvoice.Id;
    }

    public async Task<InvoiceDetails> GetInvoiceForShopperAsync(string invoiceId, string buyerId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await LoadOwnedAsync(invoiceId, buyerId, cancellationToken);

        var providerInvoice = await _provider.GetAsync(invoiceId, cancellationToken);
        invoice.SyncProviderSnapshot(providerInvoice.Status, providerInvoice.PaymentLink);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        return MapDetails(invoice, providerInvoice.History);
    }

    public async Task<InvoiceDetails> CorrectInvoiceAsync(string invoiceId, string buyerId, DateTimeOffset? dueDate,
        string? customerName, string? customerEmail, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadOwnedAsync(invoiceId, buyerId, cancellationToken);

        if (!invoice.CanCorrect)
        {
            throw new InvoiceStateException(
                $"This bill has been {invoice.State.ToString().ToLowerInvariant()} and can no longer be corrected.");
        }

        var newDueDate = dueDate ?? invoice.DueDate;
        var newName = Fallback(customerName, invoice.CustomerName);
        var newEmail = Fallback(customerEmail, invoice.CustomerEmail);

        // The provider's update is not a partial patch: the amount and description come from the order and
        // are re-sent unchanged so only the due date and customer details actually change.
        var command = new CorrectInvoiceCommand(invoice.MerchantReference, DescriptionFor(invoice.OrderId),
            invoice.Amount, invoice.Currency, newDueDate, newName, newEmail);

        var providerInvoice = await _provider.CorrectAsync(invoiceId, command, cancellationToken);

        invoice.Correct(newDueDate, newName, newEmail);
        invoice.SyncProviderSnapshot(providerInvoice.Status, providerInvoice.PaymentLink);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        return MapDetails(invoice, providerInvoice.History);
    }

    public async Task<InvoiceDetails> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadAsync(invoiceId, cancellationToken);

        if (invoice.State != InvoiceState.Draft)
        {
            throw new InvoiceStateException(
                $"This bill has already been {invoice.State.ToString().ToLowerInvariant()} and cannot be issued.");
        }

        var providerInvoice = await _provider.IssueAsync(invoiceId, cancellationToken);
        invoice.MarkIssued(providerInvoice.Status, providerInvoice.PaymentLink);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        return MapDetails(invoice, providerInvoice.History);
    }

    public async Task<InvoiceDetails> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadAsync(invoiceId, cancellationToken);

        if (invoice.State == InvoiceState.Withdrawn)
        {
            throw new InvoiceStateException("This bill has already been withdrawn.");
        }

        var providerInvoice = await _provider.WithdrawAsync(invoiceId, cancellationToken);
        invoice.MarkWithdrawn(providerInvoice.Status);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        return MapDetails(invoice, providerInvoice.History);
    }

    public async Task<IReadOnlyList<InvoiceSummaryView>> ListInvoicesForShopperAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var invoices = await _invoiceRepository.ListAsync(new InvoicesByBuyerSpecification(buyerId), cancellationToken);
        return invoices.Select(i => new InvoiceSummaryView
        {
            InvoiceId = i.ProviderInvoiceId,
            OrderId = i.OrderId,
            Amount = i.Amount,
            Currency = i.Currency,
            DueDate = i.DueDate,
            State = i.State.ToString(),
            ProviderStatus = i.ProviderStatus,
            CreatedDate = i.CreatedDate
        }).ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("'to' must be on or after 'from'.", nameof(to));
        }

        // Page the whole provider list (no server-side date filter), bounded by a page cap so a large or
        // looping result can never hang the request.
        var providerItems = new List<ProviderInvoiceSummary>();
        var pages = 0;
        var offset = 0;
        var total = int.MaxValue;
        var truncated = false;

        while (true)
        {
            var page = await _provider.ListAsync(offset, ReconciliationPageSize, cancellationToken);
            providerItems.AddRange(page.Items);
            total = page.TotalInvoices;
            pages++;
            offset += ReconciliationPageSize;

            if (page.Items.Count == 0 || offset >= total)
            {
                break;
            }
            if (pages >= ReconciliationMaxPages)
            {
                truncated = offset < total;
                break;
            }
        }

        // This account's provider list supplies no per-bill created date (the field comes back null). When
        // that is the case the range is applied to eShop's own records and the provider's bills are lined up
        // by identifier; where the provider does supply dates, its rows are date-filtered too.
        var providerCreatedDatesAvailable = providerItems.Any(i => TryParseDate(i.CreatedDateRaw, out _));

        bool ProviderRowInRange(ProviderInvoiceSummary row)
        {
            if (!providerCreatedDatesAvailable)
            {
                return true;
            }
            return TryParseDate(row.CreatedDateRaw, out var created) && created >= from && created <= to;
        }

        var providerById = providerItems
            .Where(i => !string.IsNullOrEmpty(i.Id))
            .GroupBy(i => i.Id!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var localAll = await _invoiceRepository.ListAsync(cancellationToken);
        var localById = localAll
            .GroupBy(i => i.ProviderInvoiceId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var localInRange = localAll.Where(i => i.CreatedDate >= from && i.CreatedDate <= to).ToList();

        var entries = new List<ReconciliationEntry>();

        // 1. eShop's own record of the bills it raised in the range, each lined up against the provider by
        //    identifier — showing matches and bills eShop raised that the provider has no record of.
        foreach (var local in localInRange)
        {
            providerById.TryGetValue(local.ProviderInvoiceId, out var row);
            entries.Add(new ReconciliationEntry
            {
                InvoiceId = local.ProviderInvoiceId,
                Origin = "eShop",
                PresentAtProvider = row is not null,
                PresentInEShop = true,
                Status = row?.Status ?? local.ProviderStatus,
                TotalAmount = row?.TotalAmount ?? FormatAmount(local.Amount),
                Currency = row?.Currency ?? local.Currency,
                CustomerName = local.CustomerName,
                CreatedDate = local.CreatedDate,
                OrderId = local.OrderId
            });
        }

        // 2. eShop-stamped bills the provider knows about that eShop has no record of (the reverse gap —
        //    e.g. a bill from a run whose in-memory store has since been lost).
        foreach (var row in providerItems.Where(r => IsMine(r) && ProviderRowInRange(r)))
        {
            if (string.IsNullOrEmpty(row.Id) || localById.ContainsKey(row.Id))
            {
                continue;
            }
            entries.Add(new ReconciliationEntry
            {
                InvoiceId = row.Id,
                Origin = "eShop",
                PresentAtProvider = true,
                PresentInEShop = false,
                Status = row.Status,
                TotalAmount = row.TotalAmount,
                Currency = row.Currency,
                CustomerName = row.CustomerName,
                CreatedDate = ParseDateOrNull(row.CreatedDateRaw),
                OrderId = null
            });
        }

        // 3. Bills that are not this application's — surfaced and labelled External so the provider's record
        //    is never presented as though it were all eShop's.
        foreach (var row in providerItems.Where(r => !IsMine(r) && ProviderRowInRange(r)))
        {
            entries.Add(new ReconciliationEntry
            {
                InvoiceId = row.Id ?? "(unknown)",
                Origin = "External",
                PresentAtProvider = true,
                PresentInEShop = false,
                Status = row.Status,
                TotalAmount = row.TotalAmount,
                Currency = row.Currency,
                CustomerName = row.CustomerName,
                CreatedDate = ParseDateOrNull(row.CreatedDateRaw),
                OrderId = null
            });
        }

        var summaryCounts = new ReconciliationSummary
        {
            Matched = entries.Count(e => e.Origin == "eShop" && e.PresentAtProvider && e.PresentInEShop),
            EShopMissingAtProvider = entries.Count(e => e.Origin == "eShop" && !e.PresentAtProvider && e.PresentInEShop),
            ProviderMissingInEShop = entries.Count(e => e.Origin == "eShop" && e.PresentAtProvider && !e.PresentInEShop),
            ExternalAtProvider = entries.Count(e => e.Origin == "External")
        };

        return new ReconciliationReport
        {
            From = from,
            To = to,
            ProviderInvoicesScanned = providerItems.Count,
            Truncated = truncated,
            ProviderCreatedDatesAvailable = providerCreatedDatesAvailable,
            Summary = summaryCounts,
            Entries = entries
        };
    }

    private static bool IsMine(ProviderInvoiceSummary row) =>
        !string.IsNullOrEmpty(row.MerchantReference)
        && row.MerchantReference.StartsWith(MerchantReferencePrefix, StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset? ParseDateOrNull(string? raw) =>
        TryParseDate(raw, out var value) ? value : null;

    private async Task<Invoice> LoadAsync(string invoiceId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(
            new InvoiceByProviderIdSpecification(invoiceId), cancellationToken);
        if (invoice is null)
        {
            throw new InvoiceNotFoundException($"Bill {invoiceId} was not found.");
        }
        return invoice;
    }

    private async Task<Invoice> LoadOwnedAsync(string invoiceId, string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var invoice = await LoadAsync(invoiceId, cancellationToken);
        if (!string.Equals(invoice.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Never reveal that another shopper's bill exists.
            throw new InvoiceNotFoundException($"Bill {invoiceId} was not found.");
        }
        return invoice;
    }

    private static InvoiceDetails MapDetails(Invoice invoice, IReadOnlyList<ProviderInvoiceEvent> history) => new()
    {
        InvoiceId = invoice.ProviderInvoiceId,
        OrderId = invoice.OrderId,
        Amount = invoice.Amount,
        Currency = invoice.Currency,
        DueDate = invoice.DueDate,
        CustomerName = invoice.CustomerName,
        CustomerEmail = invoice.CustomerEmail,
        State = invoice.State.ToString(),
        ProviderStatus = invoice.ProviderStatus,
        PaymentLink = invoice.PaymentLink,
        CreatedDate = invoice.CreatedDate,
        History = history.Select(h => new InvoiceHistoryEntry(h.Event, h.Date)).ToList()
    };

    private static string MerchantReferenceFor(int orderId) => $"{MerchantReferencePrefix}{orderId}";

    private static string DescriptionFor(int orderId) => $"eShopOnWeb order #{orderId}";

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static bool TryParseDate(string? raw, out DateTimeOffset value)
    {
        if (!string.IsNullOrWhiteSpace(raw) &&
            DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value))
        {
            return true;
        }
        value = default;
        return false;
    }
}
