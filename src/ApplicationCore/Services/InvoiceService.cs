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

/// <summary>
/// Orchestrates the invoicing flows. What is billed always derives from the order — never from anything a
/// caller restates — so the amount and line items are re-hydrated from the order on both raise and correct.
/// Shopper-scoped methods refuse to act on another shopper's data by answering as though it does not exist.
/// </summary>
public class InvoiceService : IInvoiceService
{
    private const string MerchantCustomerIdPrefix = "eShopOnWeb-order-";

    private readonly IRepository<Invoice> _invoiceRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IInvoicingProvider _provider;
    private readonly InvoicingSettings _settings;

    public InvoiceService(
        IRepository<Invoice> invoiceRepository,
        IRepository<Order> orderRepository,
        IInvoicingProvider provider,
        InvoicingSettings settings)
    {
        _invoiceRepository = invoiceRepository;
        _orderRepository = orderRepository;
        _provider = provider;
        _settings = settings;
    }

    public async Task<Invoice?> RaiseInvoiceForOrderAsync(int orderId, string buyerId, DateOnly dueDate,
        InvoiceCustomerDetails? customer, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            return null; // not the caller's order — indistinguishable from "no such order"

        var existing = await _invoiceRepository.ListAsync(new InvoicesByOrderSpecification(orderId), cancellationToken);
        if (existing.Any(i => i.State != InvoiceState.Withdrawn))
            throw new DuplicateException($"An active invoice already exists for order {orderId}.");

        var due = ToDueDate(dueDate);
        var merchantCustomerId = MerchantCustomerIdPrefix + orderId;
        var name = FirstNonEmpty(customer?.Name, buyerId);
        var email = FirstNonEmpty(customer?.Email, buyerId);
        var lines = BuildLines(order);
        var total = order.Total();

        var command = new RaiseInvoiceCommand(
            Description: DescriptionFor(orderId),
            DueDate: due,
            TotalAmount: total,
            Currency: _settings.Currency,
            Customer: new InvoiceCustomer(name, email, merchantCustomerId),
            Lines: lines);

        var provider = await _provider.RaiseAsync(command, cancellationToken);
        if (string.IsNullOrEmpty(provider.Id))
            throw new InvoicingProviderException("The provider raised the bill but returned no identifier.");

        var invoice = new Invoice(orderId, buyerId, provider.Id, provider.Status, due, total,
            _settings.Currency, name, email, merchantCustomerId);
        await _invoiceRepository.AddAsync(invoice, cancellationToken);
        return invoice;
    }

    public async Task<InvoiceDetails?> GetInvoiceForBuyerAsync(string providerInvoiceId, string buyerId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await FindOwnedAsync(providerInvoiceId, buyerId, cancellationToken);
        if (invoice is null)
            return null;

        var provider = await _provider.GetAsync(providerInvoiceId, cancellationToken);
        invoice.SyncProviderState(provider.Status, provider.PaymentLink);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        return new InvoiceDetails(invoice, provider);
    }

    public async Task<Invoice?> CorrectDraftInvoiceAsync(string providerInvoiceId, string buyerId, DateOnly? dueDate,
        InvoiceCustomerDetails? customer, CancellationToken cancellationToken = default)
    {
        var invoice = await FindOwnedAsync(providerInvoiceId, buyerId, cancellationToken);
        if (invoice is null)
            return null;

        // Fail before touching the provider when the bill can no longer be corrected.
        if (invoice.State != InvoiceState.Draft)
            throw new InvoiceNotModifiableException(providerInvoiceId, invoice.State, "corrected");

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(invoice.OrderId), cancellationToken);
        Guard.Against.Null(order, nameof(order), "The order backing this bill could not be found.");

        var newDue = dueDate.HasValue ? ToDueDate(dueDate.Value) : invoice.DueDate;
        var name = FirstNonEmpty(customer?.Name, invoice.CustomerName);
        var email = customer?.Email ?? invoice.CustomerEmail;
        var lines = BuildLines(order);
        var total = order.Total();

        var command = new UpdateInvoiceCommand(
            Description: DescriptionFor(invoice.OrderId),
            DueDate: newDue,
            TotalAmount: total, // amount is never correctable here — it comes from the order
            Currency: invoice.Currency,
            Customer: new InvoiceCustomer(name, email, invoice.MerchantCustomerId),
            Lines: lines);

        var updated = await _provider.UpdateAsync(providerInvoiceId, command, cancellationToken);
        invoice.ApplyDraftCorrection(newDue, name, email);
        invoice.SyncProviderState(updated.Status, updated.PaymentLink);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        return invoice;
    }

    public async Task<Invoice?> IssueInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(providerInvoiceId), cancellationToken);
        if (invoice is null)
            return null;

        if (invoice.State != InvoiceState.Draft)
            throw new InvoiceNotModifiableException(providerInvoiceId, invoice.State, "issued");

        var issued = await _provider.IssueAsync(providerInvoiceId, cancellationToken);
        var status = issued.Status;
        var paymentLink = issued.PaymentLink;

        // The deliver response may not carry the pay link yet; read it back so the shopper can be handed one.
        if (string.IsNullOrWhiteSpace(paymentLink))
        {
            var fresh = await _provider.GetAsync(providerInvoiceId, cancellationToken);
            status = fresh.Status ?? status;
            paymentLink = fresh.PaymentLink;
        }

        invoice.MarkIssued(status, paymentLink);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        return invoice;
    }

    public async Task<Invoice?> WithdrawInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(providerInvoiceId), cancellationToken);
        if (invoice is null)
            return null;

        if (invoice.State == InvoiceState.Withdrawn)
            throw new InvoiceNotModifiableException(providerInvoiceId, invoice.State, "withdrawn again");

        var withdrawn = await _provider.WithdrawAsync(providerInvoiceId, cancellationToken);
        invoice.MarkWithdrawn(withdrawn.Status);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        return invoice;
    }

    public async Task<IReadOnlyList<Invoice>> GetInvoicesForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _invoiceRepository.ListAsync(new CustomerInvoicesSpecification(buyerId), cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerSummaries = await _provider.ListRaisedBetweenAsync(from, to, cancellationToken);
        var eShopInvoices = await _invoiceRepository.ListAsync(new InvoicesCreatedBetweenSpecification(from, to), cancellationToken);

        var eShopByProviderId = eShopInvoices
            .GroupBy(i => i.ProviderInvoiceId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var providerIds = new HashSet<string>(providerSummaries.Select(s => s.Id), StringComparer.Ordinal);

        var entries = new List<ReconciliationEntry>();

        foreach (var s in providerSummaries)
        {
            var matched = eShopByProviderId.TryGetValue(s.Id, out var eShop);
            var belongsToEShop = matched ||
                (s.MerchantCustomerId?.StartsWith("eShopOnWeb", StringComparison.OrdinalIgnoreCase) ?? false);

            entries.Add(new ReconciliationEntry(
                InvoiceId: s.Id,
                Source: matched ? ReconciliationSource.MatchedBoth : ReconciliationSource.ProviderOnly,
                BelongsToEShop: belongsToEShop,
                ProviderStatus: s.Status,
                EShopState: matched ? eShop!.State.ToString() : null,
                Amount: s.TotalAmount,
                Currency: s.Currency,
                CreatedDate: s.CreatedDate,
                DueDate: s.DueDate?.ToString("O", CultureInfo.InvariantCulture),
                CustomerName: s.CustomerName,
                MerchantCustomerId: s.MerchantCustomerId));
        }

        foreach (var e in eShopInvoices.Where(i => !providerIds.Contains(i.ProviderInvoiceId)))
        {
            entries.Add(new ReconciliationEntry(
                InvoiceId: e.ProviderInvoiceId,
                Source: ReconciliationSource.EShopOnly,
                BelongsToEShop: true,
                ProviderStatus: e.ProviderStatus,
                EShopState: e.State.ToString(),
                Amount: FormatMoney(e.TotalAmount),
                Currency: e.Currency,
                CreatedDate: e.CreatedDate,
                DueDate: e.DueDate.ToString("O", CultureInfo.InvariantCulture),
                CustomerName: e.CustomerName,
                MerchantCustomerId: e.MerchantCustomerId));
        }

        return new ReconciliationReport(
            From: from,
            To: to,
            ProviderCount: providerSummaries.Count,
            EShopCount: eShopInvoices.Count,
            MatchedCount: entries.Count(x => x.Source == ReconciliationSource.MatchedBoth),
            ProviderOnlyCount: entries.Count(x => x.Source == ReconciliationSource.ProviderOnly),
            EShopOnlyCount: entries.Count(x => x.Source == ReconciliationSource.EShopOnly),
            Entries: entries);
    }

    private async Task<Invoice?> FindOwnedAsync(string providerInvoiceId, string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(providerInvoiceId), cancellationToken);
        if (invoice is null || !string.Equals(invoice.BuyerId, buyerId, StringComparison.Ordinal))
            return null;
        return invoice;
    }

    private static IReadOnlyList<InvoiceLineItem> BuildLines(Order order) =>
        order.OrderItems
            .Select(oi => new InvoiceLineItem(
                oi.ItemOrdered.ProductName,
                oi.UnitPrice,
                oi.Units,
                oi.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture)))
            .ToList();

    private static DateTimeOffset ToDueDate(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static string DescriptionFor(int orderId) => $"Invoice for eShopOnWeb order #{orderId}";

    private static string FormatMoney(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static string FirstNonEmpty(string? preferred, string fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred!;
}
