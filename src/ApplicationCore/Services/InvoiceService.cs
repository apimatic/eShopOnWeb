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

public class InvoiceService : IInvoiceService
{
    /// <summary>This account bills in USD; every bill eShop raises uses it (never a per-call choice).</summary>
    public const string BillingCurrency = "USD";

    /// <summary>Prefix on the provider-side merchant-customer id that marks a bill as eShop's during reconciliation.</summary>
    public const string EShopMerchantCustomerPrefix = "eshop";

    private readonly IRepository<Invoice> _invoiceRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IInvoicingProvider _provider;

    public InvoiceService(IRepository<Invoice> invoiceRepository,
        IRepository<Order> orderRepository,
        IInvoicingProvider provider)
    {
        _invoiceRepository = invoiceRepository;
        _orderRepository = orderRepository;
        _provider = provider;
    }

    public async Task<Invoice> RaiseInvoiceForOrderAsync(int orderId, string buyerId, DateTimeOffset dueDate, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);

        // An order that does not exist, or belongs to another shopper, is indistinguishable to this caller.
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new OrderNotFoundException(orderId);
        }

        var existing = await _invoiceRepository.CountAsync(new ActiveInvoiceForOrderSpecification(orderId), cancellationToken);
        if (existing > 0)
        {
            throw new InvoiceAlreadyExistsException(orderId);
        }

        var lineItems = order.OrderItems
            .Select(item => new InvoiceLineItem(
                ProductName: item.ItemOrdered.ProductName,
                Sku: item.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture),
                Quantity: item.Units,
                UnitPrice: item.UnitPrice,
                TotalAmount: item.UnitPrice * item.Units))
            .ToList();

        var amount = order.Total();
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        var merchantCustomerId = BuildMerchantCustomerId(buyerId);
        var description = $"eShopOnWeb order #{order.Id}";

        // Customer details are invented fixtures for the sandbox: the shopper's own login is used, which is
        // an email in eShop. They can be corrected later while the bill is still a draft.
        var request = new RaiseInvoiceRequest(
            Description: description,
            Amount: amount,
            Currency: BillingCurrency,
            DueDate: dueDate,
            CustomerName: buyerId,
            CustomerEmail: buyerId,
            MerchantCustomerId: merchantCustomerId,
            LineItems: lineItems);

        var result = await _provider.RaiseAsync(request, cancellationToken);

        var invoice = new Invoice(
            orderId: order.Id,
            buyerId: buyerId,
            providerInvoiceId: result.ProviderInvoiceId,
            merchantCustomerId: merchantCustomerId,
            description: description,
            amount: amount,
            currency: BillingCurrency,
            dueDate: dueDate,
            customer: new InvoiceCustomer(buyerId, buyerId),
            providerStatus: result.Status);

        return await _invoiceRepository.AddAsync(invoice, cancellationToken);
    }

    public async Task<InvoiceDetails> GetInvoiceForShopperAsync(int invoiceId, string buyerId, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadOwnedInvoiceAsync(invoiceId, buyerId, cancellationToken);

        var providerState = await _provider.GetAsync(invoice.ProviderInvoiceId, cancellationToken);

        invoice.SyncProviderStatus(providerState.Status);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        // A payment link is only ever handed out for a bill that has been put to the shopper and not withdrawn.
        var paymentLink = invoice.IsPayable ? providerState.PaymentLink : null;

        return new InvoiceDetails(invoice, providerState.Status, paymentLink, providerState.History);
    }

    public async Task<Invoice> CorrectInvoiceAsync(int invoiceId, string buyerId, InvoiceCorrectionRequest correction, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(correction, nameof(correction));

        var invoice = await LoadOwnedInvoiceAsync(invoiceId, buyerId, cancellationToken);

        // Refuse up front rather than let the change silently do nothing.
        if (!invoice.IsDraft)
        {
            throw new InvoiceNotCorrectableException(invoice.Id, invoice.Status);
        }

        var newDueDate = correction.DueDate ?? invoice.DueDate;
        var newName = string.IsNullOrWhiteSpace(correction.CustomerName) ? invoice.Customer.Name : correction.CustomerName!;
        var newEmail = string.IsNullOrWhiteSpace(correction.CustomerEmail) ? invoice.Customer.Email : correction.CustomerEmail!;

        // The provider's update replaces the whole body, so the amount block is re-sent unchanged — it is
        // taken from the stored bill (which was sourced from the order), never restated by the caller.
        var request = new CorrectInvoiceRequest(
            Description: invoice.Description,
            Amount: invoice.Amount,
            Currency: invoice.Currency,
            DueDate: newDueDate,
            CustomerName: newName,
            CustomerEmail: newEmail,
            MerchantCustomerId: invoice.MerchantCustomerId);

        var result = await _provider.CorrectAsync(invoice.ProviderInvoiceId, request, cancellationToken);

        invoice.ApplyCorrection(correction.DueDate, correction.CustomerName, correction.CustomerEmail);
        invoice.SyncProviderStatus(result.Status);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        return invoice;
    }

    public async Task<Invoice> IssueInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.GetByIdAsync(invoiceId, cancellationToken)
            ?? throw new InvoiceNotFoundException(invoiceId);

        if (invoice.IsWithdrawn)
        {
            throw new InvoiceTransitionException(invoice.Id, invoice.Status, "issue");
        }

        // Already put to the shopper — issuing again is a no-op success.
        if (invoice.IsIssued)
        {
            return invoice;
        }

        var result = await _provider.IssueAsync(invoice.ProviderInvoiceId, cancellationToken);

        invoice.MarkIssued(result.Status);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        return invoice;
    }

    public async Task<Invoice> WithdrawInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.GetByIdAsync(invoiceId, cancellationToken)
            ?? throw new InvoiceNotFoundException(invoiceId);

        // Already withdrawn — withdrawing again is a no-op success.
        if (invoice.IsWithdrawn)
        {
            return invoice;
        }

        var result = await _provider.WithdrawAsync(invoice.ProviderInvoiceId, cancellationToken);

        invoice.MarkWithdrawn(result.Status);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        return invoice;
    }

    public async Task<IReadOnlyList<Invoice>> GetInvoicesForShopperAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _invoiceRepository.ListAsync(new InvoicesByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // The provider's list projection carries no per-invoice creation date and no date-range filter, so the
        // date range is authoritative for eShop's own side (bills eShop believes it raised in the range, by the
        // date eShop recorded), and the provider's whole-account record is cross-referenced by id. Provider bills
        // eShop has no record of are still surfaced (as our-tagged orphans, or as other activity on the account),
        // so a discrepancy in either direction is visible.
        var providerInvoices = await _provider.ListAllInvoicesAsync(cancellationToken);
        var eShopInvoices = await _invoiceRepository.ListAsync(cancellationToken);

        var providerById = providerInvoices
            .Where(p => !string.IsNullOrEmpty(p.ProviderInvoiceId))
            .GroupBy(p => p.ProviderInvoiceId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matchedProviderIds = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();

        // eShop's side, scoped to the requested range by the date eShop recorded.
        foreach (var invoice in eShopInvoices)
        {
            if (invoice.CreatedDate < from || invoice.CreatedDate > to)
            {
                continue;
            }

            providerById.TryGetValue(invoice.ProviderInvoiceId, out var provider);
            if (provider is not null)
            {
                matchedProviderIds.Add(invoice.ProviderInvoiceId);
            }

            entries.Add(new ReconciliationEntry(
                Status: provider is not null ? ReconciliationStatus.Reconciled : ReconciliationStatus.MissingFromProvider,
                BelongsToEShop: true,
                PresentAtProvider: provider is not null,
                PresentInEShop: true,
                InvoiceId: invoice.Id,
                ProviderInvoiceId: invoice.ProviderInvoiceId,
                MerchantCustomerId: invoice.MerchantCustomerId,
                ProviderStatus: provider?.Status ?? invoice.ProviderStatus,
                EShopStatus: invoice.Status,
                Amount: invoice.Amount,
                Currency: invoice.Currency,
                CreatedDate: invoice.CreatedDate));
        }

        // The provider's side: every account bill eShop did not just match above. An eShop-tagged bill eShop has
        // no record of is a genuine reverse discrepancy; anything else is other activity on the shared account,
        // marked as not eShop's so the report never presents the account as though it were all eShop's.
        foreach (var provider in providerInvoices)
        {
            if (string.IsNullOrEmpty(provider.ProviderInvoiceId) || matchedProviderIds.Contains(provider.ProviderInvoiceId))
            {
                continue;
            }

            var isEShopTagged = IsEShopMerchantCustomerId(provider.MerchantCustomerId);

            entries.Add(new ReconciliationEntry(
                Status: isEShopTagged ? ReconciliationStatus.MissingFromEShop : ReconciliationStatus.ForeignProviderInvoice,
                BelongsToEShop: isEShopTagged,
                PresentAtProvider: true,
                PresentInEShop: false,
                InvoiceId: null,
                ProviderInvoiceId: provider.ProviderInvoiceId,
                MerchantCustomerId: provider.MerchantCustomerId,
                ProviderStatus: provider.Status,
                EShopStatus: null,
                Amount: provider.Amount,
                Currency: provider.Currency,
                CreatedDate: provider.CreatedDate));
        }

        var summary = new ReconciliationSummary(
            ProviderInvoiceCount: providerInvoices.Count,
            EShopInvoiceCount: entries.Count(e => e.BelongsToEShop),
            ReconciledCount: entries.Count(e => e.Status == ReconciliationStatus.Reconciled),
            MissingFromEShopCount: entries.Count(e => e.Status == ReconciliationStatus.MissingFromEShop),
            MissingFromProviderCount: entries.Count(e => e.Status == ReconciliationStatus.MissingFromProvider),
            ForeignProviderInvoiceCount: entries.Count(e => e.Status == ReconciliationStatus.ForeignProviderInvoice));

        const string note =
            "The date range scopes eShop's own bills (by the date eShop recorded). The provider's list carries " +
            "no per-invoice creation date, so provider-only rows (MissingFromEShop / ForeignProviderInvoice) " +
            "reflect the whole account and are not date-bounded.";

        return new ReconciliationReport(from, to, summary, entries, note);
    }

    private async Task<Invoice> LoadOwnedInvoiceAsync(int invoiceId, string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var invoice = await _invoiceRepository.GetByIdAsync(invoiceId, cancellationToken);

        // Another shopper's bill is reported as not found so its existence is never revealed.
        if (invoice is null || !string.Equals(invoice.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new InvoiceNotFoundException(invoiceId);
        }

        return invoice;
    }

    private static string BuildMerchantCustomerId(string buyerId) => $"{EShopMerchantCustomerPrefix}:{buyerId}";

    private static bool IsEShopMerchantCustomerId(string? merchantCustomerId) =>
        merchantCustomerId is not null &&
        merchantCustomerId.StartsWith(EShopMerchantCustomerPrefix + ":", StringComparison.Ordinal);
}
