using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the billing lifecycle across eShop's own records and the external invoicing
/// provider. eShop owns the link between an order and its bill and the bill's lifecycle stage; the
/// provider remains authoritative for settlement state and the payment link.
/// </summary>
public class InvoiceService : IInvoiceService
{
    /// <summary>eShop's catalog is priced without a currency; this provider account bills in USD.</summary>
    public const string Currency = "USD";

    private readonly IRepository<Invoice> _invoiceRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IInvoiceProvider _provider;

    public InvoiceService(
        IRepository<Invoice> invoiceRepository,
        IRepository<Order> orderRepository,
        IInvoiceProvider provider)
    {
        _invoiceRepository = invoiceRepository;
        _orderRepository = orderRepository;
        _provider = provider;
    }

    public async Task<string?> RaiseForOrderAsync(
        int orderId, string buyerId, DateOnly dueDate, CustomerDetails? customer, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !OwnedBy(order, buyerId))
        {
            return null; // Not the shopper's order (or no such order): indistinguishable on purpose.
        }

        var resolvedCustomer = ResolveCustomer(customer, buyerId);
        var newInvoice = BuildNewInvoice(order, dueDate, resolvedCustomer);

        var providerInvoice = await _provider.CreateDraftInvoiceAsync(newInvoice, cancellationToken);

        var invoice = new Invoice(
            order.Id,
            buyerId,
            providerInvoice.Id,
            dueDate,
            order.Total(),
            Currency,
            resolvedCustomer.Name,
            resolvedCustomer.Email,
            providerInvoice.Status ?? "DRAFT");

        await _invoiceRepository.AddAsync(invoice, cancellationToken);
        return invoice.ProviderInvoiceId;
    }

    public async Task<InvoiceView?> GetForShopperAsync(string invoiceId, string buyerId, CancellationToken cancellationToken = default)
    {
        var invoice = await FindLocalAsync(invoiceId, cancellationToken);
        if (invoice is null || !string.Equals(invoice.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return null;
        }

        var providerInvoice = await _provider.GetInvoiceAsync(invoiceId, cancellationToken);
        await SyncStatusAsync(invoice, providerInvoice.Status, cancellationToken);
        return BuildView(invoice, providerInvoice);
    }

    public async Task<InvoiceView?> CorrectForShopperAsync(
        string invoiceId, string buyerId, DateOnly? dueDate, CustomerDetails? customer, CancellationToken cancellationToken = default)
    {
        var invoice = await FindLocalAsync(invoiceId, cancellationToken);
        if (invoice is null || !string.Equals(invoice.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return null;
        }

        if (!invoice.CanBeCorrected)
        {
            var reason = invoice.State == InvoiceState.Withdrawn
                ? "it has been withdrawn"
                : "it has already been put to the shopper";
            throw new InvoiceStateConflictException($"This bill can no longer be corrected because {reason}.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(invoice.OrderId), cancellationToken);
        if (order is null)
        {
            throw new InvoiceProviderException("The order backing this bill could not be found.");
        }

        var newDueDate = dueDate ?? invoice.DueDate;
        // A blank field means "leave unchanged" rather than "clear".
        var newCustomer = new CustomerDetails(
            string.IsNullOrWhiteSpace(customer?.Name) ? invoice.CustomerName : customer!.Name,
            string.IsNullOrWhiteSpace(customer?.Email) ? invoice.CustomerEmail : customer!.Email);

        // The billed amount always comes from the order, never from the caller, so it is restated
        // from the order here rather than accepted as input.
        var amendment = new InvoiceAmendment(
            Description: DescriptionFor(order.Id),
            DueDate: newDueDate,
            Currency: Currency,
            TotalAmount: order.Total(),
            Customer: newCustomer,
            Lines: BuildLines(order));

        var providerInvoice = await _provider.UpdateInvoiceAsync(invoiceId, amendment, cancellationToken);

        invoice.ApplyCorrection(newDueDate, newCustomer.Name, newCustomer.Email);
        invoice.SyncProviderStatus(providerInvoice.Status);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        return BuildView(invoice, providerInvoice);
    }

    public async Task<IReadOnlyList<MyInvoiceView>> ListForShopperAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var invoices = await _invoiceRepository.ListAsync(new CustomerInvoicesSpecification(buyerId), cancellationToken);
        return invoices
            .Select(i => new MyInvoiceView(
                i.ProviderInvoiceId, i.OrderId, i.State.ToString(), i.ProviderStatus, i.Amount, i.Currency, i.DueDate))
            .ToList();
    }

    public async Task<InvoiceView?> IssueAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await FindLocalAsync(invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null; // eShop has no record of this bill.
        }

        var providerInvoice = await _provider.PublishInvoiceAsync(invoiceId, cancellationToken);

        invoice.MarkIssued(providerInvoice.Status ?? "CREATED");
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        return BuildView(invoice, providerInvoice);
    }

    public async Task<InvoiceView?> WithdrawAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await FindLocalAsync(invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var providerInvoice = await _provider.CancelInvoiceAsync(invoiceId, cancellationToken);

        invoice.MarkWithdrawn(providerInvoice.Status ?? "CANCELED");
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        return BuildView(invoice, providerInvoice);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var providerInvoices = await _provider.ListInvoicesCreatedBetweenAsync(fromUtc, toUtc, cancellationToken);

        var localInvoices = await _invoiceRepository.ListAsync(cancellationToken);
        var localByProviderId = localInvoices
            .GroupBy(i => i.ProviderInvoiceId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var entries = new List<ReconciliationEntry>();
        var providerIds = new HashSet<string>(StringComparer.Ordinal);

        // Everything the provider reports in the range, flagged as eShop's or not.
        foreach (var p in providerInvoices)
        {
            providerIds.Add(p.Id);
            localByProviderId.TryGetValue(p.Id, out var local);
            var belongs = local is not null;
            entries.Add(new ReconciliationEntry(
                InvoiceId: p.Id,
                Match: belongs ? ReconciliationMatch.Matched : ReconciliationMatch.ProviderOnly,
                BelongsToEShop: belongs,
                ProviderStatus: p.Status,
                CreatedDate: p.CreatedDate,
                Amount: p.Amount,
                Currency: p.Currency,
                CustomerName: p.CustomerName,
                OrderId: local?.OrderId,
                BuyerId: local?.BuyerId,
                EShopState: local?.State.ToString()));
        }

        // Anything eShop believes it raised in the range that the provider did not return.
        foreach (var local in localInvoices)
        {
            if (providerIds.Contains(local.ProviderInvoiceId))
            {
                continue;
            }
            if (local.CreatedDate < fromUtc || local.CreatedDate > toUtc)
            {
                continue;
            }
            entries.Add(new ReconciliationEntry(
                InvoiceId: local.ProviderInvoiceId,
                Match: ReconciliationMatch.EShopOnly,
                BelongsToEShop: true,
                ProviderStatus: local.ProviderStatus,
                CreatedDate: local.CreatedDate,
                Amount: local.Amount,
                Currency: local.Currency,
                CustomerName: local.CustomerName,
                OrderId: local.OrderId,
                BuyerId: local.BuyerId,
                EShopState: local.State.ToString()));
        }

        var ordered = entries.OrderBy(e => e.CreatedDate ?? DateTimeOffset.MaxValue).ToList();
        return new ReconciliationReport(
            From: fromUtc,
            To: toUtc,
            ProviderCount: providerInvoices.Count,
            EShopCount: ordered.Count(e => e.BelongsToEShop),
            MatchedCount: ordered.Count(e => e.Match == ReconciliationMatch.Matched),
            ProviderOnlyCount: ordered.Count(e => e.Match == ReconciliationMatch.ProviderOnly),
            EShopOnlyCount: ordered.Count(e => e.Match == ReconciliationMatch.EShopOnly),
            Entries: ordered);
    }

    // ---- helpers ----

    private Task<Invoice?> FindLocalAsync(string invoiceId, CancellationToken cancellationToken) =>
        _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), cancellationToken);

    private async Task SyncStatusAsync(Invoice invoice, string? providerStatus, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(providerStatus) || string.Equals(invoice.ProviderStatus, providerStatus, StringComparison.Ordinal))
        {
            return;
        }
        invoice.SyncProviderStatus(providerStatus);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
    }

    private static bool OwnedBy(Order order, string buyerId) =>
        string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal);

    private static CustomerDetails ResolveCustomer(CustomerDetails? customer, string buyerId)
    {
        var name = string.IsNullOrWhiteSpace(customer?.Name) ? buyerId : customer!.Name;
        var email = string.IsNullOrWhiteSpace(customer?.Email) ? buyerId : customer!.Email;
        return new CustomerDetails(name, email);
    }

    private static NewInvoice BuildNewInvoice(Order order, DateOnly dueDate, CustomerDetails customer) =>
        new(
            OrderId: order.Id,
            Description: DescriptionFor(order.Id),
            ReferenceNumber: ReferenceFor(order.Id),
            DueDate: dueDate,
            Currency: Currency,
            TotalAmount: order.Total(),
            Customer: customer,
            Lines: BuildLines(order));

    private static IReadOnlyList<NewInvoiceLine> BuildLines(Order order) =>
        order.OrderItems
            .Select(oi => new NewInvoiceLine(
                ProductName: oi.ItemOrdered.ProductName,
                Sku: oi.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture),
                Quantity: oi.Units,
                UnitPrice: oi.UnitPrice,
                TotalAmount: oi.UnitPrice * oi.Units))
            .ToList();

    private static string DescriptionFor(int orderId) => $"eShopOnWeb order #{orderId}";

    // Only letters and numbers are permitted in the provider's transaction reference number.
    private static string ReferenceFor(int orderId) =>
        $"ESHOPORDER{orderId}T{DateTime.UtcNow:yyyyMMddHHmmss}";

    private static InvoiceView BuildView(Invoice invoice, ProviderInvoice provider)
    {
        // A payment link is only handed out while the bill is payable (issued and not withdrawn).
        var paymentLink = invoice.IsPayable ? provider.PaymentLink : null;
        var history = provider.History
            .Select(h => new InvoiceHistoryEntry(h.Event, h.Date))
            .ToList();

        return new InvoiceView(
            InvoiceId: invoice.ProviderInvoiceId,
            OrderId: invoice.OrderId,
            State: invoice.State.ToString(),
            ProviderStatus: provider.Status ?? invoice.ProviderStatus,
            Amount: invoice.Amount,
            Currency: invoice.Currency,
            DueDate: invoice.DueDate,
            CustomerName: invoice.CustomerName,
            CustomerEmail: invoice.CustomerEmail,
            Description: provider.Description ?? DescriptionFor(invoice.OrderId),
            PaymentLink: paymentLink,
            History: history);
    }
}
