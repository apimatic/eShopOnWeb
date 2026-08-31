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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// The Visa/CyberSource implementation of <see cref="IInvoicingProvider"/>. It is the ONLY place that
/// speaks the provider SDK; it maps eShop's provider-neutral commands onto SDK requests and SDK responses
/// (and SDK failures) back onto eShop types. Every call is bounded by a single whole-operation deadline and
/// every failure is translated into a single caller-safe <see cref="InvoicingProviderException"/>.
/// </summary>
public class VisaInvoicingProvider : IInvoicingProvider
{
    private const int PageSize = 100;
    private const int MaxPages = 100; // backstop so a mis-behaving list can never page forever

    private static readonly IReadOnlyList<ProviderInvoiceHistoryEntry> EmptyHistory = Array.Empty<ProviderInvoiceHistoryEntry>();

    private readonly CyberSourceMergedSpecClient _client;
    private readonly ILogger<VisaInvoicingProvider> _logger;
    private readonly TimeSpan _budget;

    public VisaInvoicingProvider(CyberSourceMergedSpecClient client,
        IOptions<VisaSettings> settings,
        ILogger<VisaInvoicingProvider> logger)
    {
        _client = client;
        _logger = logger;
        var seconds = settings.Value.RequestTimeoutSeconds > 0 ? settings.Value.RequestTimeoutSeconds : 30;
        _budget = TimeSpan.FromSeconds(seconds);
    }

    public Task<ProviderInvoiceResult> RaiseAsync(RaiseInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var body = new CreateInvoiceRequest
        {
            CustomerInformation = new CustomerInformation
            {
                Name = request.CustomerName,
                Email = request.CustomerEmail,
                MerchantCustomerId = request.MerchantCustomerId
            },
            InvoiceInformation = new InvoiceInformation
            {
                Description = request.Description,
                DueDate = request.DueDate,
                SendImmediately = false // keep it a draft — not yet put to the shopper
            },
            OrderInformation = new OrderInformation60
            {
                AmountDetails = new AmountDetails60
                {
                    TotalAmount = FormatAmount(request.Amount),
                    Currency = request.Currency
                },
                LineItems = request.LineItems.Select(MapLineItem).ToList()
            }
        };

        return InvokeWriteAsync<InvoicingV2InvoicesPost201Response, CreateInvoiceError>(
            "raise the bill",
            ct => _client.Invoices.CreateInvoice(body, ct: ct),
            r => new ProviderInvoiceResult(RequireId(r.Id), r.Status, r.InvoiceInformation?.PaymentLink, EmptyHistory),
            TranslateCreate,
            cancellationToken);
    }

    public Task<ProviderInvoiceResult> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        return InvokeReadAsync<InvoicingV2InvoicesGet200Response, GetInvoiceError>(
            "read the bill",
            ct => _client.Invoices.GetInvoice(providerInvoiceId, ct: ct),
            r => new ProviderInvoiceResult(
                r.Id ?? providerInvoiceId,
                r.Status,
                r.InvoiceInformation?.PaymentLink,
                MapHistory(r.InvoiceHistory)),
            TranslateGet,
            cancellationToken);
    }

    public Task<ProviderInvoiceResult> CorrectAsync(string providerInvoiceId, CorrectInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var body = new UpdateInvoiceRequest
        {
            CustomerInformation = new CustomerInformation
            {
                Name = request.CustomerName,
                Email = request.CustomerEmail,
                MerchantCustomerId = request.MerchantCustomerId
            },
            InvoiceInformation = new InvoiceInformation4
            {
                Description = request.Description,
                DueDate = request.DueDate
            },
            // The provider's update replaces the whole body, so the amount block is required even though the
            // amount is unchanged — it is re-supplied from the stored bill, never restated by the caller.
            OrderInformation = new OrderInformation60
            {
                AmountDetails = new AmountDetails60
                {
                    TotalAmount = FormatAmount(request.Amount),
                    Currency = request.Currency
                }
            }
        };

        // Update is a PUT (idempotent) so it is a read-style invoke (no single-send guard needed).
        return InvokeReadAsync<InvoicingV2InvoicesPut200Response, UpdateInvoiceError>(
            "correct the bill",
            ct => _client.Invoices.UpdateInvoice(providerInvoiceId, body, ct: ct),
            r => new ProviderInvoiceResult(r.Id ?? providerInvoiceId, r.Status, r.InvoiceInformation?.PaymentLink, EmptyHistory),
            TranslateUpdate,
            cancellationToken);
    }

    public Task<ProviderInvoiceResult> IssueAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        return InvokeWriteAsync<InvoicingV2InvoicesSend200Response, PerformSendActionError>(
            "put the bill to the shopper",
            ct => _client.Invoices.PerformSendAction(providerInvoiceId, ct: ct),
            r => new ProviderInvoiceResult(r.Id ?? providerInvoiceId, r.Status, r.InvoiceInformation?.PaymentLink, EmptyHistory),
            TranslateSend,
            cancellationToken);
    }

    public Task<ProviderInvoiceResult> WithdrawAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        return InvokeWriteAsync<InvoicingV2InvoicesCancel200Response, PerformCancelActionError>(
            "withdraw the bill",
            ct => _client.Invoices.PerformCancelAction(providerInvoiceId, ct: ct),
            r => new ProviderInvoiceResult(r.Id ?? providerInvoiceId, r.Status, r.InvoiceInformation?.PaymentLink, EmptyHistory),
            TranslateCancel,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderInvoiceSummary>> ListAllInvoicesAsync(CancellationToken cancellationToken = default)
    {
        // One budget for the whole paged operation, linked to the caller's token.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_budget);
        var token = cts.Token;

        var results = new List<ProviderInvoiceSummary>();

        try
        {
            var offset = 0;
            for (var page = 0; page < MaxPages; page++)
            {
                // status filter has no default in the SDK — pass null explicitly for "no filter".
                var response = await _client.Invoices.GetAllInvoices(offset, PageSize, null, ct: token);

                var rows = response.Invoices;
                if (rows is null || rows.Count == 0)
                {
                    break;
                }

                foreach (var row in rows)
                {
                    results.Add(MapSummary(row));
                }

                offset += rows.Count;

                var total = response.TotalInvoices;
                if (rows.Count < PageSize || (total.HasValue && offset >= total.Value))
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            throw Translate<GetAllInvoicesError>(ex, "reconcile bills", TranslateAllGet);
        }

        return results;
    }

    // ---- invocation + error boundary -------------------------------------------------------------

    private async Task<ProviderInvoiceResult> InvokeReadAsync<TResponse, TError>(
        string action,
        Func<CancellationToken, Task<TResponse>> call,
        Func<TResponse, ProviderInvoiceResult> map,
        Func<TError, (int? Status, string Message)> translateTyped,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_budget);
        try
        {
            var response = await call(cts.Token);
            return map(response);
        }
        catch (SdkException<TError> ex)
        {
            var (status, message) = translateTyped(ex.Error);
            throw new InvoicingProviderException(message, status, ex);
        }
        catch (Exception ex)
        {
            throw Translate(ex, action, translateTyped);
        }
    }

    private async Task<ProviderInvoiceResult> InvokeWriteAsync<TResponse, TError>(
        string action,
        Func<CancellationToken, Task<TResponse>> call,
        Func<TResponse, ProviderInvoiceResult> map,
        Func<TError, (int? Status, string Message)> translateTyped,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_budget);
        try
        {
            // Permit only one network send within this scope, so a retried transport failure cannot
            // duplicate a non-idempotent write.
            using (SingleSendGuard.BeginScope())
            {
                var response = await call(cts.Token);
                return map(response);
            }
        }
        catch (SdkException<TError> ex)
        {
            var (status, message) = translateTyped(ex.Error);
            throw new InvoicingProviderException(message, status, ex);
        }
        catch (Exception ex)
        {
            throw Translate(ex, action, translateTyped, outcomeUnknownOnTransport: true);
        }
    }

    /// <summary>
    /// Translates the non-typed failure kinds shared by every operation into a caller-safe provider error.
    /// A drifted 2xx body and a duplicate-send refusal are "unknown outcome"; a transport failure on a write
    /// is also unknown (the request may have reached the provider).
    /// </summary>
    private InvoicingProviderException Translate<TError>(Exception ex, string action, Func<TError, (int? Status, string Message)> translateTyped, bool outcomeUnknownOnTransport = false)
    {
        switch (ex)
        {
            case SdkException<TError> typed:
                var (status, message) = translateTyped(typed.Error);
                return new InvoicingProviderException(message, status, typed);

            case DuplicateSendBlockedException blocked:
                _logger.LogWarning(blocked, "A provider write for '{Action}' was interrupted and not re-sent.", action);
                return new InvoicingProviderException(
                    $"The provider request to {action} was interrupted after being sent; it was not retried to avoid a duplicate. Reconcile before retrying.",
                    providerStatusCode: null, innerException: blocked, outcomeUnknown: true);

            case JsonException json:
                // A non-2xx body that did not match the typed error shape destroys the SdkException and its
                // status; a drifted 2xx body throws here too. Either way the detail is lost — surface it as a
                // provider-side problem without leaking JSON internals.
                _logger.LogError(json, "Could not process the provider response for '{Action}'.", action);
                return new InvoicingProviderException(
                    $"The provider returned a response for '{action}' that could not be processed.",
                    providerStatusCode: null, innerException: json);

            case OperationCanceledException canceled:
                _logger.LogWarning(canceled, "The provider request to {Action} timed out or was canceled.", action);
                return new InvoicingProviderException(
                    $"The provider request to {action} timed out.",
                    providerStatusCode: null, innerException: canceled, outcomeUnknown: outcomeUnknownOnTransport);

            case HttpRequestException http:
                _logger.LogWarning(http, "The provider was unreachable while trying to {Action}.", action);
                return new InvoicingProviderException(
                    $"The invoicing provider was unreachable while trying to {action}.",
                    providerStatusCode: null, innerException: http, outcomeUnknown: outcomeUnknownOnTransport);

            case InvoicingProviderException already:
                return already;

            default:
                _logger.LogError(ex, "Unexpected failure while trying to {Action}.", action);
                return new InvoicingProviderException(
                    $"An unexpected error occurred while trying to {action}.",
                    providerStatusCode: null, innerException: ex);
        }
    }

    // ---- typed-error extraction (one per operation) ----------------------------------------------

    private static (int?, string) TranslateCreate(CreateInvoiceError e)
    {
        if (e.TryGetInvoicingV2InvoicesPost400Response1(out var p)) return (400, Describe(p?.Reason, p?.Message));
        if (e.TryGetInvoicingV2InvoicesPost404Response1(out var q)) return (404, Describe(q?.Reason, q?.Message));
        if (e.TryGetInvoicingV2InvoicesPost502Response1(out var r)) return (502, Describe(r?.Reason, r?.Message));
        return FromRaw(e);
    }

    private static (int?, string) TranslateGet(GetInvoiceError e)
    {
        if (e.TryGetInvoicingV2InvoicesGet400Response1(out var p)) return (400, Describe(p?.Reason, p?.Message));
        if (e.TryGetInvoicingV2InvoicesGet404Response1(out var q)) return (404, Describe(q?.Reason, q?.Message));
        if (e.TryGetInvoicingV2InvoicesGet502Response1(out var r)) return (502, Describe(r?.Reason, r?.Message));
        return FromRaw(e);
    }

    private static (int?, string) TranslateUpdate(UpdateInvoiceError e)
    {
        if (e.TryGetInvoicingV2InvoicesPut400Response1(out var p)) return (400, Describe(p?.Reason, p?.Message));
        if (e.TryGetInvoicingV2InvoicesPut404Response1(out var q)) return (404, Describe(q?.Reason, q?.Message));
        if (e.TryGetInvoicingV2InvoicesPut502Response1(out var r)) return (502, Describe(r?.Reason, r?.Message));
        return FromRaw(e);
    }

    private static (int?, string) TranslateSend(PerformSendActionError e)
    {
        if (e.TryGetInvoicingV2InvoicesSend400Response1(out var p)) return (400, Describe(p?.Reason, p?.Message));
        if (e.TryGetInvoicingV2InvoicesSend404Response1(out var q)) return (404, Describe(q?.Reason, q?.Message));
        if (e.TryGetInvoicingV2InvoicesSend502Response1(out var r)) return (502, Describe(r?.Reason, r?.Message));
        return FromRaw(e);
    }

    private static (int?, string) TranslateCancel(PerformCancelActionError e)
    {
        if (e.TryGetInvoicingV2InvoicesCancel400Response1(out var p)) return (400, Describe(p?.Reason, p?.Message));
        if (e.TryGetInvoicingV2InvoicesCancel404Response1(out var q)) return (404, Describe(q?.Reason, q?.Message));
        if (e.TryGetInvoicingV2InvoicesCancel502Response1(out var r)) return (502, Describe(r?.Reason, r?.Message));
        return FromRaw(e);
    }

    private static (int?, string) TranslateAllGet(GetAllInvoicesError e)
    {
        if (e.TryGetInvoicingV2InvoicesAllGet400Response1(out var p)) return (400, Describe(p?.Reason, p?.Message));
        if (e.TryGetInvoicingV2InvoicesAllGet404Response1(out var q)) return (404, Describe(q?.Reason, q?.Message));
        if (e.TryGetInvoicingV2InvoicesAllGet502Response1(out var r)) return (502, Describe(r?.Reason, r?.Message));
        return FromRaw(e);
    }

    private static (int?, string) FromRaw(ApiError e)
    {
        if (e.TryGetRawError(out RawError raw))
        {
            return ((int)raw.StatusCode, $"The invoicing provider rejected the request (HTTP {(int)raw.StatusCode}).");
        }

        return (null, "The invoicing provider rejected the request.");
    }

    private static string Describe(string? reason, string? message)
    {
        var text = !string.IsNullOrWhiteSpace(message) ? message : reason;
        return string.IsNullOrWhiteSpace(text)
            ? "The invoicing provider rejected the request."
            : $"The invoicing provider rejected the request: {text}";
    }

    // ---- mapping helpers -------------------------------------------------------------------------

    private static LineItem17 MapLineItem(InvoiceLineItem item) => new()
    {
        ProductName = item.ProductName,
        ProductSku = item.Sku,
        Quantity = item.Quantity,
        UnitPrice = FormatAmount(item.UnitPrice),
        TotalAmount = FormatAmount(item.TotalAmount)
    };

    private static IReadOnlyList<ProviderInvoiceHistoryEntry> MapHistory(IReadOnlyList<InvoiceHistory>? history)
    {
        if (history is null || history.Count == 0)
        {
            return EmptyHistory;
        }

        return history.Select(h => new ProviderInvoiceHistoryEntry(h.Event, h.Date)).ToList();
    }

    private static ProviderInvoiceSummary MapSummary(Invoice1 row)
    {
        var amountText = row.OrderInformation?.AmountDetails?.TotalAmount;
        decimal? amount = TryParseAmount(amountText);
        var createdRaw = row.CreatedDate;

        return new ProviderInvoiceSummary(
            ProviderInvoiceId: row.Id ?? string.Empty,
            Status: row.Status,
            CreatedDateRaw: createdRaw,
            CreatedDate: TryParseDate(createdRaw),
            MerchantCustomerId: row.CustomerInformation?.MerchantCustomerId,
            Amount: amount,
            Currency: row.OrderInformation?.AmountDetails?.Currency);
    }

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal? TryParseAmount(string? text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static DateTimeOffset? TryParseDate(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) ? value : null;

    private static string RequireId(string? id) =>
        string.IsNullOrEmpty(id)
            ? throw new InvoicingProviderException("The provider did not return an identifier for the raised bill.")
            : id;
}
