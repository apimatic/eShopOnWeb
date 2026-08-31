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

public class InvoicingService : IInvoicingService
{
    // This shared sandbox account bills in USD; every bill raised uses it.
    private const string BillingCurrency = "USD";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Invoice> _invoiceRepository;
    private readonly IInvoiceProviderGateway _gateway;
    private readonly IInvoicingInstance _instance;

    public InvoicingService(IRepository<Order> orderRepository, IRepository<Invoice> invoiceRepository,
        IInvoiceProviderGateway gateway, IInvoicingInstance instance)
    {
        _orderRepository = orderRepository;
        _invoiceRepository = invoiceRepository;
        _gateway = gateway;
        _instance = instance;
    }

    // The marker stamped into the provider's merchant-customer-id so this deployment's bills are
    // recognisable at list time and distinguishable from sibling/foreign bills on the shared account.
    private string MerchantMarkerPrefix => $"eshop-{_instance.Tag}-";

    public async Task<string> RaiseInvoiceForOrderAsync(int orderId, DateOnly dueDate, string buyerId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);

        // Not found and not-yours are deliberately indistinguishable: one shopper must never learn of another's order.
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new OrderNotFoundException(orderId);

        var existing = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByOrderIdSpec(orderId), cancellationToken);
        if (existing is not null)
            throw new InvoiceStateException($"Order {orderId} has already been billed (invoice {existing.ProviderInvoiceId}).");

        var amount = order.Total();
        var lines = BuildLines(order);
        var invoiceNumber = BuildInvoiceNumber(orderId);
        var merchantReference = BuildMerchantReference(orderId);
        var (customerName, customerEmail) = DeriveCustomer(buyerId);
        var dueDateOffset = ToDueDate(dueDate);

        var request = new NewInvoiceRequest(
            Description: DescriptionFor(orderId),
            DueDate: dueDateOffset,
            TotalAmount: FormatAmount(amount),
            Currency: BillingCurrency,
            CustomerName: customerName,
            CustomerEmail: customerEmail,
            MerchantCustomerId: merchantReference,
            InvoiceNumber: invoiceNumber,
            Lines: lines);

        var receipt = await _gateway.RaiseAsync(request, cancellationToken);

        var invoice = new Invoice(orderId, buyerId, receipt.ProviderInvoiceId, invoiceNumber,
            merchantReference, amount, BillingCurrency, dueDateOffset, customerName, customerEmail);
        invoice.RecordProviderStatus(receipt.Status);
        await _invoiceRepository.AddAsync(invoice, cancellationToken);

        return receipt.ProviderInvoiceId;
    }

    public async Task<InvoiceDetails> GetInvoiceAsync(string invoiceId, string requesterId, bool isOperator, CancellationToken cancellationToken)
    {
        var invoice = await LoadOwnedInvoiceAsync(invoiceId, requesterId, isOperator, cancellationToken);

        // The provider owns the truth about status, history and the pay link — ask it.
        var state = await _gateway.GetAsync(invoice.ProviderInvoiceId, cancellationToken);

        invoice.RecordProviderStatus(state.Status);
        if (invoice.IsIssued)
            invoice.SetPaymentLink(state.PaymentLink);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        return ToDetails(invoice, state);
    }

    public async Task CorrectInvoiceAsync(string invoiceId, DateOnly? dueDate, CustomerDetails? customer,
        string requesterId, bool isOperator, CancellationToken cancellationToken)
    {
        var invoice = await LoadOwnedInvoiceAsync(invoiceId, requesterId, isOperator, cancellationToken);

        // Enforce the state rule locally so a refusal is deterministic and does not depend on the
        // provider's (undocumented) refusal status.
        if (!invoice.IsDraft)
            throw new InvoiceStateException(
                $"Invoice {invoice.ProviderInvoiceId} is {invoice.Status.ToString().ToLowerInvariant()} and can no longer be corrected.");

        var newDueDate = dueDate.HasValue ? ToDueDate(dueDate.Value) : invoice.DueDate;
        var newName = customer is not null ? customer.Name : invoice.CustomerName;
        var newEmail = customer is not null ? customer.Email : invoice.CustomerEmail;

        // The provider update is a full replace, so re-send the order's amount and lines unchanged —
        // the amount is not correctable here, it comes from the order.
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(invoice.OrderId), cancellationToken);
        var lines = order is null ? new List<InvoiceLine>() : BuildLines(order);

        var correction = new InvoiceCorrection(
            ProviderInvoiceId: invoice.ProviderInvoiceId,
            Description: DescriptionFor(invoice.OrderId),
            DueDate: newDueDate,
            TotalAmount: FormatAmount(invoice.Amount),
            Currency: invoice.Currency,
            CustomerName: newName,
            CustomerEmail: newEmail,
            MerchantCustomerId: invoice.MerchantReference,
            Lines: lines);

        await _gateway.CorrectAsync(correction, cancellationToken);

        invoice.Correct(newDueDate, newName, newEmail);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
    }

    public async Task IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await LoadTrackedInvoiceAsync(invoiceId, cancellationToken);

        if (invoice.IsWithdrawn)
            throw new InvoiceStateException($"Invoice {invoice.ProviderInvoiceId} has been withdrawn and can no longer be issued.");
        if (invoice.IsIssued)
            throw new InvoiceStateException($"Invoice {invoice.ProviderInvoiceId} has already been issued.");

        var state = await _gateway.IssueAsync(invoice.ProviderInvoiceId, cancellationToken);

        // The pay link may not be on the deliver response; fetch it if it is not there yet.
        var paymentLink = state.PaymentLink;
        var providerStatus = state.Status;
        if (string.IsNullOrEmpty(paymentLink))
        {
            var refreshed = await _gateway.GetAsync(invoice.ProviderInvoiceId, cancellationToken);
            paymentLink = refreshed.PaymentLink;
            providerStatus = refreshed.Status ?? providerStatus;
        }

        invoice.MarkIssued(paymentLink);
        invoice.RecordProviderStatus(providerStatus);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
    }

    public async Task WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await LoadTrackedInvoiceAsync(invoiceId, cancellationToken);

        if (invoice.IsWithdrawn)
            throw new InvoiceStateException($"Invoice {invoice.ProviderInvoiceId} has already been withdrawn.");

        await _gateway.WithdrawAsync(invoice.ProviderInvoiceId, cancellationToken);

        invoice.MarkWithdrawn();
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
    }

    public async Task<IReadOnlyList<InvoiceSummary>> GetInvoicesForShopperAsync(string buyerId, CancellationToken cancellationToken)
    {
        var invoices = await _invoiceRepository.ListAsync(new InvoicesByBuyerSpec(buyerId), cancellationToken);
        return invoices.Select(ToSummary).ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // The provider's list projection carries no creation date and offers no date filter, so eShop's
        // own records (which have an accurate created date) are the date-authoritative side. The provider
        // account is listed in full and lined up against them by id.
        var providerRecords = await _gateway.ListAllAsync(cancellationToken);
        var allEShop = await _invoiceRepository.ListAsync(cancellationToken);

        var eShopById = allEShop
            .Where(i => !string.IsNullOrEmpty(i.ProviderInvoiceId))
            .ToDictionary(i => i.ProviderInvoiceId, StringComparer.Ordinal);
        var eShopInRange = allEShop.Where(i => i.CreatedDate >= from && i.CreatedDate <= to).ToList();
        var inRangeIds = new HashSet<string>(eShopInRange.Select(i => i.ProviderInvoiceId), StringComparer.Ordinal);

        var providerById = providerRecords
            .Where(r => !string.IsNullOrEmpty(r.ProviderInvoiceId))
            .GroupBy(r => r.ProviderInvoiceId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var entries = new List<ReconciliationEntry>();
        var matched = 0;

        // 1) eShop's own bills raised in the range, each lined up against the provider. Where a bill is
        //    present at the provider, its live provider status is shown (the authoritative provider view).
        foreach (var invoice in eShopInRange)
        {
            var present = providerById.TryGetValue(invoice.ProviderInvoiceId, out var providerRecord);
            if (present) matched++;

            entries.Add(new ReconciliationEntry(
                InvoiceId: invoice.ProviderInvoiceId,
                Origin: InvoiceOrigin.EShop,
                PresentAtProvider: present,
                PresentInEShop: true,
                Status: present ? providerRecord!.Status : invoice.ProviderStatus ?? invoice.Status.ToString(),
                Amount: FormatAmount(invoice.Amount),
                Currency: invoice.Currency,
                CreatedDate: invoice.CreatedDate,
                Discrepancy: present ? ReconciliationDiscrepancy.None : ReconciliationDiscrepancy.MissingFromProvider));
        }

        // 2) The provider's record, minus the eShop-in-range bills already listed above — so a bill the
        //    provider knows about that eShop doesn't (or a foreign bill) is made plain.
        foreach (var record in providerRecords)
        {
            var providerId = record.ProviderInvoiceId ?? string.Empty;
            if (providerId.Length > 0 && inRangeIds.Contains(providerId))
                continue;

            var knownToEShop = providerId.Length > 0 && eShopById.ContainsKey(providerId);
            if (knownToEShop)
                continue; // an eShop bill outside the requested range — not a discrepancy

            var carriesMyMarker = (record.MerchantCustomerId ?? string.Empty)
                .StartsWith(MerchantMarkerPrefix, StringComparison.OrdinalIgnoreCase);

            if (carriesMyMarker)
            {
                // This deployment's marker but not in this deployment's records — the provider knows of an
                // eShop bill eShop doesn't.
                entries.Add(new ReconciliationEntry(record.ProviderInvoiceId, InvoiceOrigin.EShop,
                    PresentAtProvider: true, PresentInEShop: false, record.Status, record.TotalAmount,
                    record.Currency, CreatedDate: null, ReconciliationDiscrepancy.MissingFromEShop));
            }
            else
            {
                // Not this application's bill — foreign to eShop (or raised by another deployment).
                entries.Add(new ReconciliationEntry(record.ProviderInvoiceId, InvoiceOrigin.External,
                    PresentAtProvider: true, PresentInEShop: false, record.Status, record.TotalAmount,
                    record.Currency, CreatedDate: null, ReconciliationDiscrepancy.None));
            }
        }

        const string note = "The provider's list projection carries no creation date and no date filter, "
            + "so eShop-originated entries are bounded by eShop's own created date while the provider's whole "
            + "account is listed and labelled by origin (EShop vs External).";

        return new ReconciliationReport(from, to, providerRecords.Count, eShopInRange.Count, matched, note, entries);
    }

    private async Task<Invoice> LoadOwnedInvoiceAsync(string invoiceId, string requesterId, bool isOperator, CancellationToken cancellationToken)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpec(invoiceId), cancellationToken);
        if (invoice is null || (!isOperator && !string.Equals(invoice.BuyerId, requesterId, StringComparison.Ordinal)))
            throw new InvoiceNotFoundException(invoiceId);
        return invoice;
    }

    private async Task<Invoice> LoadTrackedInvoiceAsync(string invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpec(invoiceId), cancellationToken);
        if (invoice is null)
            throw new InvoiceNotFoundException(invoiceId);
        return invoice;
    }

    private static List<InvoiceLine> BuildLines(Order order) =>
        order.OrderItems
            .Select(oi => new InvoiceLine(
                oi.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture),
                oi.ItemOrdered.ProductName,
                FormatAmount(oi.UnitPrice),
                oi.Units))
            .ToList();

    private static string DescriptionFor(int orderId) => $"eShopOnWeb order #{orderId}";

    private string BuildMerchantReference(int orderId) => $"{MerchantMarkerPrefix}{orderId}";

    private static string BuildInvoiceNumber(int orderId)
    {
        var token = Guid.NewGuid().ToString("N").Substring(0, 6);
        return $"ESHOP-{orderId}-{token}";
    }

    private static (string Name, string Email) DeriveCustomer(string buyerId)
    {
        // The shopper's username is their email address in this app, which is a valid test fixture.
        if (buyerId.Contains('@'))
            return (buyerId[..buyerId.IndexOf('@')], buyerId);
        return (buyerId, $"{buyerId}@example.com");
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static DateTimeOffset ToDueDate(DateOnly dueDate) =>
        new DateTimeOffset(dueDate.Year, dueDate.Month, dueDate.Day, 0, 0, 0, TimeSpan.Zero);

    private static InvoiceDetails ToDetails(Invoice invoice, InvoiceState state)
    {
        // A pay link is only ever surfaced while the bill is issued (never before, never after withdrawal).
        var paymentLink = invoice.IsIssued ? (state.PaymentLink ?? invoice.PaymentLink) : null;

        return new InvoiceDetails(
            InvoiceId: invoice.ProviderInvoiceId,
            OrderId: invoice.OrderId,
            Status: invoice.Status.ToString(),
            ProviderStatus: state.Status ?? invoice.ProviderStatus,
            Currency: invoice.Currency,
            Amount: FormatAmount(invoice.Amount),
            DueDate: DateOnly.FromDateTime(invoice.DueDate.UtcDateTime),
            CustomerName: invoice.CustomerName,
            CustomerEmail: invoice.CustomerEmail,
            Issued: invoice.IsIssued,
            PaymentLink: paymentLink,
            History: state.History);
    }

    private static InvoiceSummary ToSummary(Invoice invoice) =>
        new InvoiceSummary(
            InvoiceId: invoice.ProviderInvoiceId,
            OrderId: invoice.OrderId,
            Status: invoice.Status.ToString(),
            Amount: FormatAmount(invoice.Amount),
            Currency: invoice.Currency,
            DueDate: DateOnly.FromDateTime(invoice.DueDate.UtcDateTime),
            Issued: invoice.IsIssued);
}
