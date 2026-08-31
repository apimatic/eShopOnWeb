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
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Talks to Visa's invoicing platform (CyberSource) through the generated SDK's single <c>Invoices</c>
/// controller. Every SDK call is routed through the base URL bound from configuration (set once on the
/// injected, long-lived client). Every failure — a provider rejection, an unreachable provider, or an
/// unprocessable response — is translated into an <see cref="InvoicingProviderException"/> carrying a
/// caller-safe message and, where known, the provider's HTTP status. No provider message, body, or secret
/// is ever surfaced verbatim.
/// </summary>
public class CyberSourceInvoicingProvider : IInvoicingProvider
{
    private const int PageSize = 100;
    private const int MaxPages = 200;
    private const int MaxDetailFetches = 1000;

    private readonly CyberSourceMergedSpecClient _client;
    private readonly ILogger<CyberSourceInvoicingProvider> _logger;

    public CyberSourceInvoicingProvider(CyberSourceMergedSpecClient client, ILogger<CyberSourceInvoicingProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<ProviderInvoice> RaiseAsync(RaiseInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        var request = new CreateInvoiceRequest
        {
            CustomerInformation = ToCustomer(command.Customer),
            InvoiceInformation = new InvoiceInformation
            {
                Description = command.Description,
                DueDate = command.DueDate,
                SendImmediately = false, // draft — not yet put to the shopper
            },
            OrderInformation = ToOrder(command.TotalAmount, command.Currency, command.Lines),
        };

        try
        {
            var resp = await _client.Invoices.CreateInvoice(request, ct: cancellationToken);
            return Map(resp.Id, resp.Status, resp.InvoiceInformation, resp.OrderInformation, resp.CustomerInformation, null);
        }
        catch (SdkException<CreateInvoiceError> ex) { throw FromCreate(ex); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Transport(ex); }
    }

    public async Task<ProviderInvoice> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var resp = await _client.Invoices.GetInvoice(providerInvoiceId, ct: cancellationToken);
            return Map(resp.Id, resp.Status, resp.InvoiceInformation, resp.OrderInformation, resp.CustomerInformation, resp.InvoiceHistory);
        }
        catch (SdkException<GetInvoiceError> ex) { throw FromGet(ex); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Transport(ex); }
    }

    public async Task<ProviderInvoice> UpdateAsync(string providerInvoiceId, UpdateInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        var request = new UpdateInvoiceRequest
        {
            CustomerInformation = ToCustomer(command.Customer),
            // Update is a whole-document PUT: the invoice + order blocks must be re-sent in full.
            InvoiceInformation = new InvoiceInformation4
            {
                Description = command.Description,
                DueDate = command.DueDate,
            },
            OrderInformation = ToOrder(command.TotalAmount, command.Currency, command.Lines),
        };

        try
        {
            var resp = await _client.Invoices.UpdateInvoice(providerInvoiceId, request, ct: cancellationToken);
            return Map(resp.Id, resp.Status, resp.InvoiceInformation, resp.OrderInformation, resp.CustomerInformation, null);
        }
        catch (SdkException<UpdateInvoiceError> ex) { throw FromUpdate(ex); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Transport(ex); }
    }

    public async Task<ProviderInvoice> IssueAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var resp = await _client.Invoices.PerformSendAction(providerInvoiceId, ct: cancellationToken);
            return Map(resp.Id, resp.Status, resp.InvoiceInformation, resp.OrderInformation, resp.CustomerInformation, null);
        }
        catch (SdkException<PerformSendActionError> ex) { throw FromSend(ex); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Transport(ex); }
    }

    public async Task<ProviderInvoice> WithdrawAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var resp = await _client.Invoices.PerformCancelAction(providerInvoiceId, ct: cancellationToken);
            return Map(resp.Id, resp.Status, resp.InvoiceInformation, resp.OrderInformation, resp.CustomerInformation, null);
        }
        catch (SdkException<PerformCancelActionError> ex) { throw FromCancel(ex); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Transport(ex); }
    }

    public async Task<IReadOnlyList<ProviderInvoiceSummary>> ListRaisedBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // The SDK's list has no server-side date range, so we page the whole account and filter on the
        // raised date client-side. The list entry's createdDate is frequently empty; when it is, we derive
        // the raised date from the invoice's own history (its earliest event) via a bounded detail fetch.
        var entries = new List<Invoice1>();
        var offset = 0;
        var pages = 0;

        while (pages < MaxPages)
        {
            InvoicingV2InvoicesAllGet200Response resp;
            try
            {
                resp = await _client.Invoices.GetAllInvoices(offset: offset, limit: PageSize, status: null, ct: cancellationToken);
            }
            catch (SdkException<GetAllInvoicesError> ex) { throw FromList(ex); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (JsonException ex) { throw Unprocessable(ex); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Transport(ex); }

            var page = resp.Invoices ?? new List<Invoice1>();
            entries.AddRange(page.Where(inv => !string.IsNullOrEmpty(inv.Id)));

            pages++;
            var total = resp.TotalInvoices ?? 0;
            offset += PageSize;
            if (page.Count == 0 || offset >= total)
                break;
        }

        if (pages >= MaxPages)
            _logger.LogWarning("Reconciliation reached the page cap of {MaxPages}; the report may be truncated.", MaxPages);

        var results = new List<ProviderInvoiceSummary>();
        var detailFetches = 0;

        foreach (var inv in entries)
        {
            var created = ParseDate(inv.CreatedDate);
            if (created is null && detailFetches < MaxDetailFetches)
            {
                detailFetches++;
                created = await ResolveRaisedDateFromHistoryAsync(inv.Id!, cancellationToken);
            }

            if (created is null || created < from || created > to)
                continue;

            results.Add(new ProviderInvoiceSummary(
                Id: inv.Id!,
                Status: inv.Status,
                CreatedDate: created,
                CustomerName: inv.CustomerInformation?.Name,
                MerchantCustomerId: inv.CustomerInformation?.MerchantCustomerId,
                TotalAmount: inv.OrderInformation?.AmountDetails?.TotalAmount,
                Currency: inv.OrderInformation?.AmountDetails?.Currency,
                DueDate: inv.InvoiceInformation?.DueDate));
        }

        if (detailFetches >= MaxDetailFetches)
            _logger.LogWarning("Reconciliation reached the detail-fetch cap of {Cap}; some bills may be omitted.", MaxDetailFetches);

        return results;
    }

    /// <summary>Derive a bill's raised date from the earliest event in its provider history. Returns null
    /// when the bill cannot be read or has no dated history, in which case it is left out of the range.</summary>
    private async Task<DateTimeOffset?> ResolveRaisedDateFromHistoryAsync(string providerInvoiceId, CancellationToken cancellationToken)
    {
        try
        {
            var detail = await GetAsync(providerInvoiceId, cancellationToken);
            var dates = detail.History.Where(h => h.Date.HasValue).Select(h => h.Date!.Value).ToList();
            return dates.Count > 0 ? dates.Min() : null;
        }
        catch (InvoicingProviderException)
        {
            return null; // a bill we cannot read is simply not placed in the range
        }
    }

    // ----- mapping helpers -----

    private static CustomerInformation ToCustomer(InvoiceCustomer customer) => new()
    {
        Name = customer.Name,
        Email = customer.Email,
        MerchantCustomerId = customer.MerchantCustomerId,
    };

    private static OrderInformation60 ToOrder(decimal totalAmount, string currency, IReadOnlyList<InvoiceLineItem> lines) => new()
    {
        AmountDetails = new AmountDetails60
        {
            TotalAmount = Money(totalAmount),
            Currency = currency,
        },
        LineItems = lines.Select(l => new LineItem17
        {
            ProductSku = l.Sku,
            ProductName = l.ProductName,
            UnitPrice = Money(l.UnitPrice),
            Quantity = l.Quantity,
        }).ToList(),
    };

    private static ProviderInvoice Map(string? id, string? status, InvoiceInformation1? invoice,
        OrderInformation61? order, CustomerInformation? customer, IReadOnlyList<InvoiceHistory>? history)
    {
        var events = history?
            .Select(h => new ProviderInvoiceEvent(h.Event, h.Date))
            .ToList() ?? new List<ProviderInvoiceEvent>();

        return new ProviderInvoice(
            Id: id ?? string.Empty,
            Status: status,
            PaymentLink: invoice?.PaymentLink,
            TotalAmount: order?.AmountDetails?.TotalAmount,
            Currency: order?.AmountDetails?.Currency,
            DueDate: invoice?.DueDate,
            CustomerName: customer?.Name,
            CustomerEmail: customer?.Email,
            MerchantCustomerId: customer?.MerchantCustomerId,
            History: events);
    }

    private static string Money(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    // ----- error translation -----

    private InvoicingProviderException Transport(Exception ex)
    {
        _logger.LogWarning(ex, "The invoicing provider was unreachable.");
        return new InvoicingProviderException("The invoicing provider is currently unreachable. Please try again.", null, ex);
    }

    private InvoicingProviderException Unprocessable(JsonException ex)
    {
        _logger.LogWarning(ex, "The invoicing provider returned a response that could not be processed.");
        return new InvoicingProviderException("The invoicing provider returned a response that could not be processed.", 502, ex);
    }

    private InvoicingProviderException Fail(int? status, string? providerMessage)
    {
        var message = string.IsNullOrWhiteSpace(providerMessage)
            ? "The invoicing provider rejected the request."
            : providerMessage!;
        _logger.LogWarning("Invoicing provider returned status {Status}.", status);
        return new InvoicingProviderException(message, status);
    }

    private InvoicingProviderException FromCreate(SdkException<CreateInvoiceError> ex)
    {
        var e = ex.Error;
        if (e.TryGetInvoicingV2InvoicesPost400Response1(out var b)) return Fail(400, b?.Reason ?? b?.Message);
        if (e.TryGetInvoicingV2InvoicesPost404Response1(out var n)) return Fail(404, n?.Reason ?? n?.Message);
        if (e.TryGetInvoicingV2InvoicesPost502Response1(out var g)) return Fail(502, g?.Reason ?? g?.Message);
        if (e.TryGetRawError(out var raw)) return Fail((int)raw.StatusCode, null);
        return Fail(null, null);
    }

    private InvoicingProviderException FromGet(SdkException<GetInvoiceError> ex)
    {
        var e = ex.Error;
        if (e.TryGetInvoicingV2InvoicesGet400Response1(out var b)) return Fail(400, b?.Reason ?? b?.Message);
        if (e.TryGetInvoicingV2InvoicesGet404Response1(out var n)) return Fail(404, n?.Reason ?? n?.Message);
        if (e.TryGetInvoicingV2InvoicesGet502Response1(out var g)) return Fail(502, g?.Reason ?? g?.Message);
        if (e.TryGetRawError(out var raw)) return Fail((int)raw.StatusCode, null);
        return Fail(null, null);
    }

    private InvoicingProviderException FromUpdate(SdkException<UpdateInvoiceError> ex)
    {
        var e = ex.Error;
        if (e.TryGetInvoicingV2InvoicesPut400Response1(out var b)) return Fail(400, b?.Reason ?? b?.Message);
        if (e.TryGetInvoicingV2InvoicesPut404Response1(out var n)) return Fail(404, n?.Reason ?? n?.Message);
        if (e.TryGetInvoicingV2InvoicesPut502Response1(out var g)) return Fail(502, g?.Reason ?? g?.Message);
        if (e.TryGetRawError(out var raw)) return Fail((int)raw.StatusCode, null);
        return Fail(null, null);
    }

    private InvoicingProviderException FromSend(SdkException<PerformSendActionError> ex)
    {
        var e = ex.Error;
        if (e.TryGetInvoicingV2InvoicesSend400Response1(out var b)) return Fail(400, b?.Reason ?? b?.Message);
        if (e.TryGetInvoicingV2InvoicesSend404Response1(out var n)) return Fail(404, n?.Reason ?? n?.Message);
        if (e.TryGetInvoicingV2InvoicesSend502Response1(out var g)) return Fail(502, g?.Reason ?? g?.Message);
        if (e.TryGetRawError(out var raw)) return Fail((int)raw.StatusCode, null);
        return Fail(null, null);
    }

    private InvoicingProviderException FromCancel(SdkException<PerformCancelActionError> ex)
    {
        var e = ex.Error;
        if (e.TryGetInvoicingV2InvoicesCancel400Response1(out var b)) return Fail(400, b?.Reason ?? b?.Message);
        if (e.TryGetInvoicingV2InvoicesCancel404Response1(out var n)) return Fail(404, n?.Reason ?? n?.Message);
        if (e.TryGetInvoicingV2InvoicesCancel502Response1(out var g)) return Fail(502, g?.Reason ?? g?.Message);
        if (e.TryGetRawError(out var raw)) return Fail((int)raw.StatusCode, null);
        return Fail(null, null);
    }

    private InvoicingProviderException FromList(SdkException<GetAllInvoicesError> ex)
    {
        var e = ex.Error;
        if (e.TryGetInvoicingV2InvoicesAllGet400Response1(out var b)) return Fail(400, b?.Reason ?? b?.Message);
        if (e.TryGetInvoicingV2InvoicesAllGet404Response1(out var n)) return Fail(404, n?.Reason ?? n?.Message);
        if (e.TryGetInvoicingV2InvoicesAllGet502Response1(out var g)) return Fail(502, g?.Reason ?? g?.Message);
        if (e.TryGetRawError(out var raw)) return Fail((int)raw.StatusCode, null);
        return Fail(null, null);
    }
}
