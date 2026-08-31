using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CyberSourceMergedSpec;
using CyberSourceMergedSpec.Core.ErrorResponse;
using CyberSourceMergedSpec.Core.Exceptions;
using CyberSourceMergedSpec.Errors;
using CyberSourceMergedSpec.Models;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;
using InvoiceEntity = Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate.Invoice;
using InvoiceStatus = Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate.InvoiceStatus;
using OrderEntity = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order;
// Disambiguate our domain result type from the SDK's CyberSourceMergedSpec.Models.InvoiceDetails.
using InvoiceDetails = Microsoft.eShopWeb.ApplicationCore.Invoicing.InvoiceDetails;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Customer invoicing backed by the Visa/CyberSource SDK. Owns the provider round-trips, the mapping
/// between eShop orders and provider invoices, and the local record that lets later requests act on a
/// bill. Every provider call is funnelled through this class's error boundary, which converts SDK,
/// transport and parse failures into <see cref="InvoiceProviderException"/> carrying the provider's
/// HTTP status where there was one.
/// </summary>
public sealed class VisaInvoiceService : IInvoiceService
{
    private const int PageSize = 100;
    private const int MaxPages = 200; // hard backstop against an uncooperative pager
    private const int MaxReconcileLookups = 500; // cap per-invoice date lookups in a single report
    private const int ReconcileConcurrency = 6; // bounded parallelism for those lookups

    private readonly CyberSourceMergedSpecClient _client;
    private readonly IRepository<InvoiceEntity> _invoiceRepository;
    private readonly IReadRepository<OrderEntity> _orderRepository;
    private readonly string _currency;

    public VisaInvoiceService(
        CyberSourceMergedSpecClient client,
        IRepository<InvoiceEntity> invoiceRepository,
        IReadRepository<OrderEntity> orderRepository,
        IOptions<VisaSettings> settings)
    {
        _client = client;
        _invoiceRepository = invoiceRepository;
        _orderRepository = orderRepository;
        _currency = string.IsNullOrWhiteSpace(settings.Value.Currency) ? "USD" : settings.Value.Currency;
    }

    public async Task<ServiceResult<InvoiceDetails>> RaiseInvoiceAsync(int orderId, string buyerId,
        DateTimeOffset dueDate, string? customerName, string? customerEmail, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);

        // A shopper must never invoice another's order: absence and non-ownership look identical from outside.
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return ServiceResult<InvoiceDetails>.NotFound($"Order {orderId} was not found.");
        }

        var amount = order.Total();
        var name = string.IsNullOrWhiteSpace(customerName) ? buyerId : customerName;
        var email = string.IsNullOrWhiteSpace(customerEmail) ? buyerId : customerEmail;

        var lineItems = order.OrderItems.Select(oi => new LineItem17
        {
            ProductSku = oi.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture),
            ProductName = oi.ItemOrdered.ProductName,
            Quantity = oi.Units,
            UnitPrice = Money(oi.UnitPrice),
            TotalAmount = Money(oi.UnitPrice * oi.Units)
        }).ToList();

        var request = new CreateInvoiceRequest
        {
            InvoiceInformation = new InvoiceInformation
            {
                Description = $"eShopOnWeb order {orderId}",
                DueDate = dueDate,
                SendImmediately = false // raise as a draft — not yet put to the shopper
            },
            OrderInformation = new OrderInformation60
            {
                AmountDetails = new AmountDetails60
                {
                    TotalAmount = Money(amount),
                    Currency = _currency
                },
                LineItems = lineItems
            },
            CustomerInformation = new CustomerInformation
            {
                Name = name,
                Email = email,
                MerchantCustomerId = buyerId
            }
        };

        InvoicingV2InvoicesPost201Response response;
        try
        {
            response = await _client.Invoices.CreateInvoice(request, ct: cancellationToken);
        }
        catch (SdkException<CreateInvoiceError> ex)
        {
            throw ProviderError("The bill could not be raised with the provider.", StatusFrom(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }

        var providerInvoiceId = response.Id;
        if (string.IsNullOrEmpty(providerInvoiceId))
        {
            throw ProviderError("The provider did not return an invoice identifier.", null, null);
        }

        var invoice = new InvoiceEntity(orderId, buyerId, providerInvoiceId, dueDate, name, email, _currency, amount);
        await _invoiceRepository.AddAsync(invoice, cancellationToken);

        var details = MapDetails(invoice, response.Status, paymentLink: null, history: null);
        return ServiceResult<InvoiceDetails>.Ok(details);
    }

    public async Task<ServiceResult<InvoiceDetails>> GetInvoiceAsync(string invoiceId, string buyerId,
        CancellationToken cancellationToken)
    {
        var invoice = await FindOwnedAsync(invoiceId, buyerId, cancellationToken);
        if (invoice is null)
        {
            return ServiceResult<InvoiceDetails>.NotFound();
        }

        InvoicingV2InvoicesGet200Response response;
        try
        {
            response = await _client.Invoices.GetInvoice(invoiceId, ct: cancellationToken);
        }
        catch (SdkException<GetInvoiceError> ex)
        {
            throw ProviderError("The bill could not be read from the provider.", StatusFrom(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }

        var history = response.InvoiceHistory?
            .Select(h => new InvoiceHistoryEntry(h.Event, h.Date))
            .ToList();

        // A withdrawn bill is not payable — never hand out a pay link even if the provider still echoes one.
        var paymentLink = invoice.Status == InvoiceStatus.Withdrawn ? null : response.InvoiceInformation?.PaymentLink;

        var details = MapDetails(invoice, response.Status, paymentLink, history);
        return ServiceResult<InvoiceDetails>.Ok(details);
    }

    public async Task<ServiceResult<InvoiceDetails>> CorrectInvoiceAsync(string invoiceId, string buyerId,
        DateTimeOffset? dueDate, string? customerName, string? customerEmail, CancellationToken cancellationToken)
    {
        var invoice = await FindOwnedAsync(invoiceId, buyerId, cancellationToken);
        if (invoice is null)
        {
            return ServiceResult<InvoiceDetails>.NotFound();
        }

        // Once put to the shopper or withdrawn, a bill can no longer be corrected — say so, don't no-op.
        if (!invoice.CanBeCorrected)
        {
            return ServiceResult<InvoiceDetails>.Conflict(
                $"This bill can no longer be corrected because it is {invoice.Status}.");
        }

        var newDueDate = dueDate ?? invoice.DueDate;
        var newName = customerName ?? invoice.CustomerName ?? buyerId;
        var newEmail = customerEmail ?? invoice.CustomerEmail ?? buyerId;

        // The amount is not correctable: resend the order amount unchanged (the SDK requires it on update).
        var request = new UpdateInvoiceRequest
        {
            InvoiceInformation = new InvoiceInformation4
            {
                Description = $"eShopOnWeb order {invoice.OrderId}",
                DueDate = newDueDate
            },
            OrderInformation = new OrderInformation60
            {
                AmountDetails = new AmountDetails60
                {
                    TotalAmount = Money(invoice.Amount),
                    Currency = invoice.Currency
                }
            },
            CustomerInformation = new CustomerInformation
            {
                Name = newName,
                Email = newEmail,
                MerchantCustomerId = buyerId
            }
        };

        InvoicingV2InvoicesPut200Response response;
        try
        {
            response = await _client.Invoices.UpdateInvoice(invoiceId, request, ct: cancellationToken);
        }
        catch (SdkException<UpdateInvoiceError> ex)
        {
            throw ProviderError("The bill could not be corrected with the provider.", StatusFrom(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }

        invoice.ApplyCorrection(newDueDate, newName, newEmail);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        var details = MapDetails(invoice, response.Status, paymentLink: null, history: null);
        return ServiceResult<InvoiceDetails>.Ok(details);
    }

    public async Task<ServiceResult<InvoiceDetails>> IssueInvoiceAsync(string invoiceId,
        CancellationToken cancellationToken)
    {
        // Operator action — acts on any shopper's bill, so no ownership check.
        var invoice = await FindAsync(invoiceId, cancellationToken);
        if (invoice is null)
        {
            return ServiceResult<InvoiceDetails>.NotFound();
        }

        InvoicingV2InvoicesSend200Response response;
        try
        {
            response = await _client.Invoices.PerformSendAction(invoiceId, ct: cancellationToken);
        }
        catch (SdkException<PerformSendActionError> ex)
        {
            throw ProviderError("The bill could not be put to the shopper.", StatusFrom(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }

        invoice.MarkIssued();
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        // The pay link is only populated once the bill is live; re-read it so the caller can pay it.
        var paymentLink = response.InvoiceInformation?.PaymentLink;
        if (string.IsNullOrEmpty(paymentLink))
        {
            paymentLink = await TryReadPaymentLinkAsync(invoiceId, cancellationToken);
        }

        var details = MapDetails(invoice, response.Status, paymentLink, history: null);
        return ServiceResult<InvoiceDetails>.Ok(details);
    }

    public async Task<ServiceResult<InvoiceDetails>> WithdrawInvoiceAsync(string invoiceId,
        CancellationToken cancellationToken)
    {
        // Operator action — acts on any shopper's bill, so no ownership check.
        var invoice = await FindAsync(invoiceId, cancellationToken);
        if (invoice is null)
        {
            return ServiceResult<InvoiceDetails>.NotFound();
        }

        InvoicingV2InvoicesCancel200Response response;
        try
        {
            response = await _client.Invoices.PerformCancelAction(invoiceId, ct: cancellationToken);
        }
        catch (SdkException<PerformCancelActionError> ex)
        {
            throw ProviderError("The bill could not be withdrawn.", StatusFrom(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }

        invoice.MarkWithdrawn();
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        // Withdrawn: not payable, so no pay link is handed out.
        var details = MapDetails(invoice, response.Status, paymentLink: null, history: null);
        return ServiceResult<InvoiceDetails>.Ok(details);
    }

    public async Task<IReadOnlyList<InvoiceSummary>> GetInvoicesForBuyerAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var invoices = await _invoiceRepository.ListAsync(new CustomerInvoicesSpecification(buyerId), cancellationToken);
        return invoices.Select(i => new InvoiceSummary
        {
            InvoiceId = i.ProviderInvoiceId,
            OrderId = i.OrderId,
            LocalStatus = i.Status.ToString(),
            DueDate = i.DueDate,
            Amount = i.Amount,
            Currency = i.Currency
        }).ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        // eShop's own record of bills raised in the range — its local created date is authoritative.
        var eShopInvoices = await _invoiceRepository.ListAsync(
            new InvoicesCreatedBetweenSpecification(from, to), cancellationToken);
        var eShopByProviderId = eShopInvoices
            .GroupBy(i => i.ProviderInvoiceId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // The provider's full current record. Its list carries no per-invoice creation date and it
        // offers no date filter, so a bill's raise date is obtained per invoice from its history.
        var providerList = await PageAllProviderInvoicesAsync(cancellationToken);
        var providerIdsAll = new HashSet<string>(
            providerList.Where(p => p.Id is not null).Select(p => p.Id!), StringComparer.Ordinal);

        var entries = new List<ReconciliationEntry>();
        var matched = 0;
        var providerOnly = 0;

        // Provider bills we recognise as ours (in eShop's in-range record) are in the range by
        // definition; the rest need a raise date before we can place them in the range.
        var notOurs = new List<ProviderListItem>();
        foreach (var p in providerList)
        {
            if (p.Id is null)
            {
                continue;
            }

            if (eShopByProviderId.TryGetValue(p.Id, out var local))
            {
                matched++;
                entries.Add(new ReconciliationEntry
                {
                    InvoiceId = p.Id,
                    Presence = ReconciliationPresence.Matched,
                    ProviderStatus = p.Status,
                    ProviderCreatedDate = local.CreatedDate,
                    OrderId = local.OrderId,
                    LocalStatus = local.Status.ToString(),
                    Amount = local.Amount,
                    Currency = local.Currency
                });
            }
            else
            {
                notOurs.Add(p);
            }
        }

        // Establish the raise date of each not-ours provider bill (from its history) and keep those
        // that fall in the range — the account's other activity, made plain as not eShop's.
        var createdDates = await ResolveProviderCreatedDatesAsync(notOurs, cancellationToken);
        foreach (var p in notOurs)
        {
            if (!createdDates.TryGetValue(p.Id!, out var created) || created is null
                || created < from || created > to)
            {
                continue;
            }

            providerOnly++;
            entries.Add(new ReconciliationEntry
            {
                InvoiceId = p.Id!,
                Presence = ReconciliationPresence.ProviderOnly,
                ProviderStatus = p.Status,
                ProviderCreatedDate = created
            });
        }

        // eShop bills the provider does not have at all — a discrepancy the operator should see.
        var eShopOnly = 0;
        foreach (var local in eShopInvoices)
        {
            if (!providerIdsAll.Contains(local.ProviderInvoiceId))
            {
                eShopOnly++;
                entries.Add(new ReconciliationEntry
                {
                    InvoiceId = local.ProviderInvoiceId,
                    Presence = ReconciliationPresence.EShopOnly,
                    OrderId = local.OrderId,
                    LocalStatus = local.Status.ToString(),
                    Amount = local.Amount,
                    Currency = local.Currency
                });
            }
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            ProviderInvoiceCount = matched + providerOnly,
            EShopInvoiceCount = eShopInvoices.Count,
            MatchedCount = matched,
            ProviderOnlyCount = providerOnly,
            EShopOnlyCount = eShopOnly,
            Entries = entries
        };
    }

    // ---- provider paging & date resolution -------------------------------------------------------

    private sealed record ProviderListItem(string? Id, string? Status);

    /// <summary>Pages the provider's whole invoice list (id + status). The list carries no creation date.</summary>
    private async Task<List<ProviderListItem>> PageAllProviderInvoicesAsync(CancellationToken cancellationToken)
    {
        var result = new List<ProviderListItem>();
        var offset = 0;

        for (var page = 0; page < MaxPages; page++)
        {
            InvoicingV2InvoicesAllGet200Response response;
            try
            {
                response = await _client.Invoices.GetAllInvoices(
                    offset: offset, limit: PageSize, status: null, ct: cancellationToken);
            }
            catch (SdkException<GetAllInvoicesError> ex)
            {
                throw ProviderError("The provider's invoice list could not be read.", StatusFrom(ex.Error), ex);
            }
            catch (JsonException ex)
            {
                throw UnprocessableResponse(ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw Unreachable(ex);
            }

            var pageItems = response.Invoices;
            if (pageItems is null || pageItems.Count == 0)
            {
                break;
            }

            foreach (var item in pageItems)
            {
                result.Add(new ProviderListItem(item.Id, item.Status));
            }

            offset += pageItems.Count;

            var total = response.TotalInvoices ?? 0;
            if (offset >= total || pageItems.Count < PageSize)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves each not-ours provider invoice's raise date from its history, with bounded concurrency
    /// and a hard lookup cap. Best-effort per invoice: one unreadable detail does not fail the report.
    /// </summary>
    private async Task<Dictionary<string, DateTimeOffset?>> ResolveProviderCreatedDatesAsync(
        List<ProviderListItem> items, CancellationToken cancellationToken)
    {
        var targets = items.Where(i => i.Id is not null).Take(MaxReconcileLookups).ToList();
        var result = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);

        using var gate = new SemaphoreSlim(ReconcileConcurrency);
        var tasks = targets.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return (Id: item.Id!, Created: await GetProviderCreatedDateAsync(item.Id!, cancellationToken));
            }
            finally
            {
                gate.Release();
            }
        });

        foreach (var pair in await Task.WhenAll(tasks))
        {
            result[pair.Id] = pair.Created;
        }

        return result;
    }

    /// <summary>The bill's raise date is the earliest event in its provider history. Best-effort.</summary>
    private async Task<DateTimeOffset?> GetProviderCreatedDateAsync(string invoiceId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Invoices.GetInvoice(invoiceId, ct: cancellationToken);
            var dates = response.InvoiceHistory?
                .Where(h => h.Date is not null)
                .Select(h => h.Date!.Value)
                .ToList();
            return dates is { Count: > 0 } ? dates.Min() : null;
        }
        catch (Exception ex) when (ex is SdkException<GetInvoiceError> or JsonException
            or HttpRequestException or TaskCanceledException)
        {
            return null; // an unreadable single detail must not fail the whole report
        }
    }

    private async Task<string?> TryReadPaymentLinkAsync(string invoiceId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Invoices.GetInvoice(invoiceId, ct: cancellationToken);
            return response.InvoiceInformation?.PaymentLink;
        }
        catch (SdkException<GetInvoiceError>)
        {
            return null; // the send itself succeeded; a follow-up read failure must not fail the issue
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    // ---- local lookups ---------------------------------------------------------------------------

    private Task<InvoiceEntity?> FindAsync(string invoiceId, CancellationToken cancellationToken) =>
        _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), cancellationToken);

    private async Task<InvoiceEntity?> FindOwnedAsync(string invoiceId, string buyerId, CancellationToken cancellationToken)
    {
        var invoice = await FindAsync(invoiceId, cancellationToken);
        if (invoice is null || !string.Equals(invoice.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return null; // absence and non-ownership are indistinguishable to the caller
        }

        return invoice;
    }

    // ---- mapping & helpers -----------------------------------------------------------------------

    private static InvoiceDetails MapDetails(InvoiceEntity invoice, string? providerStatus,
        string? paymentLink, IReadOnlyList<InvoiceHistoryEntry>? history) => new()
        {
            InvoiceId = invoice.ProviderInvoiceId,
            OrderId = invoice.OrderId,
            LocalStatus = invoice.Status.ToString(),
            ProviderStatus = providerStatus,
            DueDate = invoice.DueDate,
            CustomerName = invoice.CustomerName,
            CustomerEmail = invoice.CustomerEmail,
            Currency = invoice.Currency,
            Amount = invoice.Amount,
            PaymentLink = paymentLink,
            History = history ?? Array.Empty<InvoiceHistoryEntry>()
        };

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static InvoiceProviderException ProviderError(string message, int? status, Exception? inner) =>
        new(message, status, inner);

    private static InvoiceProviderException UnprocessableResponse(JsonException ex) =>
        new("The provider returned a response that could not be processed.", null, ex);

    private static InvoiceProviderException Unreachable(Exception ex) =>
        new("The billing provider is currently unavailable.", null, ex);

    // ---- per-operation status extraction (accessor names embed the operation path segment) -------

    private static int? StatusFrom(CreateInvoiceError e)
    {
        if (e.TryGetInvoicingV2InvoicesPost400Response1(out _)) return 400;
        if (e.TryGetInvoicingV2InvoicesPost404Response1(out _)) return 404;
        if (e.TryGetInvoicingV2InvoicesPost502Response1(out _)) return 502;
        return e.TryGetRawError(out var raw) ? (int)raw.StatusCode : null;
    }

    private static int? StatusFrom(GetInvoiceError e)
    {
        if (e.TryGetInvoicingV2InvoicesGet400Response1(out _)) return 400;
        if (e.TryGetInvoicingV2InvoicesGet404Response1(out _)) return 404;
        if (e.TryGetInvoicingV2InvoicesGet502Response1(out _)) return 502;
        return e.TryGetRawError(out var raw) ? (int)raw.StatusCode : null;
    }

    private static int? StatusFrom(UpdateInvoiceError e)
    {
        if (e.TryGetInvoicingV2InvoicesPut400Response1(out _)) return 400;
        if (e.TryGetInvoicingV2InvoicesPut404Response1(out _)) return 404;
        if (e.TryGetInvoicingV2InvoicesPut502Response1(out _)) return 502;
        return e.TryGetRawError(out var raw) ? (int)raw.StatusCode : null;
    }

    private static int? StatusFrom(PerformSendActionError e)
    {
        if (e.TryGetInvoicingV2InvoicesSend400Response1(out _)) return 400;
        if (e.TryGetInvoicingV2InvoicesSend404Response1(out _)) return 404;
        if (e.TryGetInvoicingV2InvoicesSend502Response1(out _)) return 502;
        return e.TryGetRawError(out var raw) ? (int)raw.StatusCode : null;
    }

    private static int? StatusFrom(PerformCancelActionError e)
    {
        if (e.TryGetInvoicingV2InvoicesCancel400Response1(out _)) return 400;
        if (e.TryGetInvoicingV2InvoicesCancel404Response1(out _)) return 404;
        if (e.TryGetInvoicingV2InvoicesCancel502Response1(out _)) return 502;
        return e.TryGetRawError(out var raw) ? (int)raw.StatusCode : null;
    }

    private static int? StatusFrom(GetAllInvoicesError e)
    {
        if (e.TryGetInvoicingV2InvoicesAllGet400Response1(out _)) return 400;
        if (e.TryGetInvoicingV2InvoicesAllGet404Response1(out _)) return 404;
        if (e.TryGetInvoicingV2InvoicesAllGet502Response1(out _)) return 502;
        return e.TryGetRawError(out var raw) ? (int)raw.StatusCode : null;
    }
}
