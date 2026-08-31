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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Turns orders into bills held with the provider and owns the bill lifecycle. All shopper-facing
/// reads and corrections are scoped to the owning buyer; operator actions (issue, withdraw, reconcile)
/// are not. What is billed always comes from the order — the caller never restates the amount.
/// </summary>
public class InvoiceService : IInvoiceService
{
    // This provider account bills in USD; every bill this integration raises uses it.
    private const string CurrencyCode = "USD";

    private readonly IRepository<Invoice> _invoiceRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IInvoiceProvider _provider;
    private readonly IAppLogger<InvoiceService> _logger;

    public InvoiceService(
        IRepository<Invoice> invoiceRepository,
        IRepository<Order> orderRepository,
        IInvoiceProvider provider,
        IAppLogger<InvoiceService> logger)
    {
        _invoiceRepository = invoiceRepository;
        _orderRepository = orderRepository;
        _provider = provider;
        _logger = logger;
    }

    public async Task<Invoice> RaiseInvoiceAsync(int orderId, string buyerId, DateOnly dueDate, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await LoadOwnedOrderAsync(orderId, buyerId);

        // One live bill per order: a bill already raised (and not withdrawn) is not raised again.
        var existing = await _invoiceRepository.ListAsync(new InvoicesByOrderSpecification(orderId), cancellationToken);
        if (existing.Any(i => i.Status != InvoiceStatus.Withdrawn))
        {
            throw new InvoiceStateException($"Order {orderId} has already been invoiced.");
        }

        var lineItems = BuildLineItems(order);
        var amount = order.Total();
        var invoiceNumber = GenerateInvoiceNumber(orderId);
        var customerName = buyerId;
        var customerEmail = ResolveEmail(buyerId);
        var merchantReference = MerchantReference(orderId);

        var draft = new ProviderInvoiceDraft(
            invoiceNumber,
            DescriptionFor(orderId),
            dueDate,
            customerName,
            customerEmail,
            merchantReference,
            amount,
            CurrencyCode,
            lineItems);

        var providerRef = await _provider.CreateDraftAsync(draft, cancellationToken);

        var invoice = new Invoice(
            orderId,
            buyerId,
            providerRef.ProviderInvoiceId,
            invoiceNumber,
            customerName,
            customerEmail,
            amount,
            CurrencyCode,
            dueDate);

        await _invoiceRepository.AddAsync(invoice, cancellationToken);

        _logger.LogInformation(
            "Raised invoice {InvoiceId} (provider {ProviderInvoiceId}) for order {OrderId}, status {Status}.",
            invoice.Id, providerRef.ProviderInvoiceId, orderId, providerRef.Status);

        return invoice;
    }

    public async Task<InvoiceDetailView> GetInvoiceAsync(int invoiceId, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var invoice = await LoadOwnedInvoiceAsync(invoiceId, buyerId);

        var details = await _provider.GetAsync(invoice.ProviderInvoiceId, cancellationToken);

        // A way to pay is only ever handed out for a bill that has been put to the shopper and not
        // withdrawn. The provider itself stops returning the link once a bill is withdrawn; we belt-
        // and-brace it against eShop's own lifecycle too.
        var paymentLink = invoice.Status == InvoiceStatus.Withdrawn ? null : details.PaymentLink;

        return new InvoiceDetailView(
            invoice.Id,
            invoice.OrderId,
            invoice.ProviderInvoiceId,
            invoice.InvoiceNumber,
            invoice.Status,
            details.Status,
            invoice.Amount,
            invoice.CurrencyCode,
            invoice.DueDate,
            invoice.CustomerName,
            invoice.CustomerEmail,
            paymentLink,
            invoice.CreatedAt,
            invoice.IssuedAt,
            invoice.WithdrawnAt,
            details.History);
    }

    public async Task<Invoice> CorrectInvoiceAsync(
        int invoiceId,
        string buyerId,
        DateOnly? dueDate,
        string? customerName,
        string? customerEmail,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var invoice = await LoadOwnedInvoiceAsync(invoiceId, buyerId);

        // Refuse the correction up front if the bill has moved on, so the caller is told rather than
        // the change silently doing nothing.
        if (invoice.Status != InvoiceStatus.Draft)
        {
            throw new InvoiceStateException(
                $"Invoice {invoiceId} cannot be corrected because it has already been {invoice.Status.ToString().ToLowerInvariant()}.");
        }

        // Candidate values — the amount is deliberately absent; it always comes from the order.
        var newDueDate = dueDate ?? invoice.DueDate;
        var newName = customerName ?? invoice.CustomerName;
        var newEmail = customerEmail ?? invoice.CustomerEmail;

        var order = await LoadOwnedOrderAsync(invoice.OrderId, buyerId);
        var lineItems = BuildLineItems(order);

        var update = new ProviderInvoiceUpdate(
            invoice.InvoiceNumber,
            DescriptionFor(invoice.OrderId),
            newDueDate,
            newName,
            newEmail,
            MerchantReference(invoice.OrderId),
            invoice.Amount,
            invoice.CurrencyCode,
            lineItems);

        await _provider.UpdateAsync(invoice.ProviderInvoiceId, update, cancellationToken);

        // Only mutate + persist eShop's own record once the provider has accepted the correction.
        invoice.ApplyCorrection(dueDate, customerName, customerEmail);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        _logger.LogInformation("Corrected invoice {InvoiceId} (provider {ProviderInvoiceId}).", invoice.Id, invoice.ProviderInvoiceId);

        return invoice;
    }

    public async Task<IReadOnlyList<Invoice>> GetMyInvoicesAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var invoices = await _invoiceRepository.ListAsync(new InvoicesByBuyerSpecification(buyerId), cancellationToken);
        return invoices;
    }

    public async Task<Invoice> IssueInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadInvoiceAsync(invoiceId);

        if (invoice.Status == InvoiceStatus.Withdrawn)
        {
            throw new InvoiceStateException($"Invoice {invoiceId} has been withdrawn and cannot be put to the shopper.");
        }
        if (invoice.Status == InvoiceStatus.Issued)
        {
            throw new InvoiceStateException($"Invoice {invoiceId} has already been put to the shopper.");
        }

        await _provider.IssueAsync(invoice.ProviderInvoiceId, cancellationToken);

        invoice.MarkIssued();
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        _logger.LogInformation("Issued invoice {InvoiceId} (provider {ProviderInvoiceId}).", invoice.Id, invoice.ProviderInvoiceId);

        return invoice;
    }

    public async Task<Invoice> WithdrawInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadInvoiceAsync(invoiceId);

        if (invoice.Status == InvoiceStatus.Withdrawn)
        {
            throw new InvoiceStateException($"Invoice {invoiceId} has already been withdrawn.");
        }

        await _provider.WithdrawAsync(invoice.ProviderInvoiceId, cancellationToken);

        invoice.MarkWithdrawn();
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        _logger.LogInformation("Withdrew invoice {InvoiceId} (provider {ProviderInvoiceId}).", invoice.Id, invoice.ProviderInvoiceId);

        return invoice;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var providerSummaries = await _provider.ListRaisedBetweenAsync(from, to, cancellationToken);
        var localInvoices = await _invoiceRepository.ListAsync(new InvoicesRaisedBetweenSpecification(from, to), cancellationToken);

        var localByProviderId = localInvoices
            .GroupBy(i => i.ProviderInvoiceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var entries = new List<ReconciliationEntry>();
        var matchedProviderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int matched = 0, providerOnly = 0;

        foreach (var summary in providerSummaries)
        {
            if (localByProviderId.TryGetValue(summary.ProviderInvoiceId, out var local))
            {
                matched++;
                matchedProviderIds.Add(summary.ProviderInvoiceId);
                entries.Add(new ReconciliationEntry(
                    summary.ProviderInvoiceId,
                    summary.InvoiceNumber,
                    ReconciliationSource.Matched,
                    IsEShopInvoice: true,
                    summary.Status,
                    local.Status,
                    local.Id,
                    local.OrderId,
                    summary.Amount ?? local.Amount,
                    summary.CurrencyCode ?? local.CurrencyCode,
                    summary.CustomerName ?? local.CustomerName,
                    summary.RaisedAt ?? local.CreatedAt));
            }
            else
            {
                // The provider account carries bills that are not this application's. Flag them plainly.
                providerOnly++;
                entries.Add(new ReconciliationEntry(
                    summary.ProviderInvoiceId,
                    summary.InvoiceNumber,
                    ReconciliationSource.ProviderOnly,
                    IsEShopInvoice: false,
                    summary.Status,
                    EShopStatus: null,
                    EShopInvoiceId: null,
                    OrderId: null,
                    summary.Amount,
                    summary.CurrencyCode,
                    summary.CustomerName,
                    summary.RaisedAt));
            }
        }

        var eShopOnly = 0;
        foreach (var local in localInvoices)
        {
            if (matchedProviderIds.Contains(local.ProviderInvoiceId))
            {
                continue;
            }

            eShopOnly++;
            entries.Add(new ReconciliationEntry(
                local.ProviderInvoiceId,
                local.InvoiceNumber,
                ReconciliationSource.EShopOnly,
                IsEShopInvoice: true,
                ProviderStatus: null,
                local.Status,
                local.Id,
                local.OrderId,
                local.Amount,
                local.CurrencyCode,
                local.CustomerName,
                local.CreatedAt));
        }

        return new ReconciliationReport(
            from,
            to,
            providerSummaries.Count,
            localInvoices.Count,
            matched,
            providerOnly,
            eShopOnly,
            entries);
    }

    private async Task<Invoice> LoadInvoiceAsync(int invoiceId)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByIdSpecification(invoiceId));
        if (invoice is null)
        {
            throw new InvoiceNotFoundException(invoiceId);
        }
        return invoice;
    }

    private async Task<Invoice> LoadOwnedInvoiceAsync(int invoiceId, string buyerId)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByIdSpecification(invoiceId));
        // A missing bill and another shopper's bill are indistinguishable to the caller.
        if (invoice is null || !string.Equals(invoice.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new InvoiceNotFoundException(invoiceId);
        }
        return invoice;
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private static IReadOnlyList<ProviderLineItem> BuildLineItems(Order order) =>
        order.OrderItems
            .Select(oi => new ProviderLineItem(
                oi.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture),
                oi.ItemOrdered.ProductName,
                oi.Units,
                oi.UnitPrice))
            .ToList();

    private static string DescriptionFor(int orderId) => $"Invoice for eShopOnWeb order #{orderId}";

    private static string MerchantReference(int orderId) => $"eShopOnWeb-order-{orderId}";

    private static string GenerateInvoiceNumber(int orderId)
    {
        // Provider requires an alphanumeric invoice number no longer than 20 characters.
        var suffix = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var number = $"ESH{orderId}X{suffix}";
        return number.Length <= 20 ? number : number.Substring(0, 20);
    }

    private static string ResolveEmail(string buyerId)
    {
        if (buyerId.Contains('@'))
        {
            return buyerId;
        }

        var local = new string(buyerId.Where(char.IsLetterOrDigit).ToArray());
        if (local.Length == 0)
        {
            local = "shopper";
        }
        return $"{local}@eshoponweb.test";
    }
}
