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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Talks to Visa via its CyberSource platform using the CyberSourceMergedSpec SDK. This is the only place
/// that references SDK types: every method maps the SDK envelopes onto the application's own
/// <see cref="ProviderInvoice"/> / <see cref="ProviderInvoiceSummary"/> types, and every failure is
/// translated into <see cref="InvoicingProviderException"/> with the provider's HTTP status carried on it.
/// No secret and no SDK type name ever crosses this boundary.
/// </summary>
public class CyberSourceInvoicingService : IInvoicingService
{
    // The provider list operation has no server-side date filter, and — despite the SDK modelling it —
    // returns no per-invoice created date on the wire. So reconciliation pages the account, reads each
    // bill's creation date from its history (earliest event) via GetInvoice, and filters client-side.
    // The caps bound both the paging and the per-invoice enrichment so nothing can spin unbounded.
    private const int PageSize = 100;
    private const int MaxPages = 50;
    private const int MaxEnrichmentCalls = 500;

    private readonly CyberSourceMergedSpecClient _client;
    private readonly IAppLogger<CyberSourceInvoicingService> _logger;
    private readonly TimeSpan _callBudget;

    public CyberSourceInvoicingService(
        CyberSourceMergedSpecClient client,
        IOptions<VisaSettings> settings,
        IAppLogger<CyberSourceInvoicingService> logger)
    {
        _client = client;
        _logger = logger;
        _callBudget = TimeSpan.FromSeconds(settings.Value.RequestTimeoutSeconds);
    }

    public Task<ProviderInvoice> RaiseInvoiceAsync(RaiseInvoiceCommand command, CancellationToken ct = default)
    {
        var request = new CreateInvoiceRequest
        {
            CustomerInformation = new CustomerInformation
            {
                Name = command.CustomerName,
                Email = command.CustomerEmail
            },
            InvoiceInformation = new InvoiceInformation
            {
                InvoiceNumber = command.InvoiceNumber,
                Description = command.Description,
                DueDate = command.DueDate
                // SendImmediately left at its default (false) => the bill starts as a DRAFT,
                // not yet put to the shopper.
            },
            OrderInformation = BuildOrderInformation(command.TotalAmount, command.Currency, command.LineItems)
        };

        return ExecuteAsync("raise the bill",
            async token =>
            {
                var response = await _client.Invoices.CreateInvoice(request, ct: token);
                return Map(response.Id, response.Status, response.InvoiceInformation?.PaymentLink);
            },
            ex => ex is SdkException<CreateInvoiceError> sdk ? TranslateCreate(sdk) : null,
            ct);
    }

    public Task<ProviderInvoice> GetInvoiceAsync(string providerInvoiceId, CancellationToken ct = default)
    {
        return ExecuteAsync("read the bill",
            async token =>
            {
                var response = await _client.Invoices.GetInvoice(providerInvoiceId, ct: token);
                return Map(response.Id, response.Status, response.InvoiceInformation?.PaymentLink, response.InvoiceHistory);
            },
            ex => ex is SdkException<GetInvoiceError> sdk ? TranslateGet(sdk) : null,
            ct);
    }

    public Task<ProviderInvoice> AmendInvoiceAsync(string providerInvoiceId, AmendInvoiceCommand command, CancellationToken ct = default)
    {
        // The provider update is a full replace: both invoiceInformation and orderInformation must be
        // re-sent. The amount is not being corrected — it is re-sent unchanged from the order.
        var request = new UpdateInvoiceRequest
        {
            CustomerInformation = new CustomerInformation
            {
                Name = command.CustomerName,
                Email = command.CustomerEmail
            },
            InvoiceInformation = new InvoiceInformation4
            {
                Description = command.Description,
                DueDate = command.DueDate
            },
            OrderInformation = BuildOrderInformation(command.TotalAmount, command.Currency, lineItems: null)
        };

        return ExecuteAsync("correct the bill",
            async token =>
            {
                var response = await _client.Invoices.UpdateInvoice(providerInvoiceId, request, ct: token);
                return Map(response.Id, response.Status, response.InvoiceInformation?.PaymentLink);
            },
            ex => ex is SdkException<UpdateInvoiceError> sdk ? TranslateUpdate(sdk) : null,
            ct);
    }

    public Task<ProviderInvoice> IssueInvoiceAsync(string providerInvoiceId, CancellationToken ct = default)
    {
        return ExecuteAsync("issue the bill",
            async token =>
            {
                var response = await _client.Invoices.PerformSendAction(providerInvoiceId, ct: token);
                return Map(response.Id, response.Status, response.InvoiceInformation?.PaymentLink);
            },
            ex => ex is SdkException<PerformSendActionError> sdk ? TranslateSend(sdk) : null,
            ct);
    }

    public Task<ProviderInvoice> WithdrawInvoiceAsync(string providerInvoiceId, CancellationToken ct = default)
    {
        return ExecuteAsync("withdraw the bill",
            async token =>
            {
                var response = await _client.Invoices.PerformCancelAction(providerInvoiceId, ct: token);
                return Map(response.Id, response.Status, paymentLink: null);
            },
            ex => ex is SdkException<PerformCancelActionError> sdk ? TranslateCancel(sdk) : null,
            ct);
    }

    public async Task<IReadOnlyList<ProviderInvoiceSummary>> ListInvoicesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<ProviderInvoiceSummary>();
        var offset = 0;
        var page = 0;
        var enrichmentCalls = 0;
        var enrichmentCapped = false;

        for (; page < MaxPages; page++)
        {
            var response = await ExecuteAsync("list bills for reconciliation",
                token => _client.Invoices.GetAllInvoices(offset, PageSize, status: null, ct: token),
                ex => ex is SdkException<GetAllInvoicesError> sdk ? TranslateList(sdk) : null,
                ct);

            var invoices = response.Invoices ?? new List<Invoice1>();

            foreach (var invoice in invoices)
            {
                if (string.IsNullOrEmpty(invoice.Id))
                    continue;

                if (enrichmentCalls >= MaxEnrichmentCalls)
                {
                    enrichmentCapped = true;
                    break;
                }

                enrichmentCalls++;
                var created = await GetCreatedDateAsync(invoice.Id!, ct);
                if (created is null || created < from || created > to)
                    continue;

                results.Add(new ProviderInvoiceSummary
                {
                    ProviderInvoiceId = invoice.Id!,
                    Status = invoice.Status,
                    CreatedDate = created
                });
            }

            if (enrichmentCapped || invoices.Count < PageSize)
                break; // enrichment cap hit, or last page reached

            offset += PageSize;
        }

        if (page >= MaxPages || enrichmentCapped)
        {
            _logger.LogWarning(
                "Reconciliation stopped at its safety cap while listing provider invoices; the report may be incomplete.");
        }

        return results;
    }

    /// <summary>
    /// The provider does not return a created date on the list, so read the bill's history and take its
    /// earliest event date as the creation time. Returns null when the bill carries no dated history.
    /// </summary>
    private async Task<DateTimeOffset?> GetCreatedDateAsync(string providerInvoiceId, CancellationToken ct)
    {
        var response = await ExecuteAsync("read a bill for reconciliation",
            token => _client.Invoices.GetInvoice(providerInvoiceId, ct: token),
            ex => ex is SdkException<GetInvoiceError> sdk ? TranslateGet(sdk) : null,
            ct);

        var dates = (response.InvoiceHistory ?? new List<InvoiceHistory>())
            .Where(h => h.Date.HasValue)
            .Select(h => h.Date!.Value)
            .ToList();

        return dates.Count > 0 ? dates.Min() : null;
    }

    // -- helpers -------------------------------------------------------------------------------------

    private static OrderInformation60 BuildOrderInformation(decimal totalAmount, string currency,
        IReadOnlyList<InvoiceLineItemDetail>? lineItems)
    {
        return new OrderInformation60
        {
            AmountDetails = new AmountDetails60
            {
                TotalAmount = FormatAmount(totalAmount),
                Currency = currency
            },
            LineItems = lineItems is { Count: > 0 }
                ? lineItems.Select(item => new LineItem17
                {
                    ProductSku = item.Sku,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    UnitPrice = FormatAmount(item.UnitPrice)
                }).ToList()
                : null
        };
    }

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static ProviderInvoice Map(string? id, string? status, string? paymentLink,
        IReadOnlyList<InvoiceHistory>? history = null)
    {
        return new ProviderInvoice
        {
            ProviderInvoiceId = id ?? string.Empty,
            Status = status,
            PaymentLink = paymentLink,
            History = history is null
                ? new List<InvoiceHistoryEntry>()
                : history.Select(entry => new InvoiceHistoryEntry
                {
                    Event = entry.Event,
                    Date = entry.Date,
                    TransactionId = entry.TransactionDetails?.TransactionId,
                    TransactionAmount = entry.TransactionDetails?.Amount
                }).ToList()
        };
    }

    /// <summary>
    /// One deadline per call, connection failures and unreadable bodies converted to
    /// <see cref="InvoicingProviderException"/>, and the operation's typed error translated by the supplied
    /// delegate. A cancellation that came from the caller's own token is left to propagate.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(string action, Func<CancellationToken, Task<T>> call,
        Func<Exception, InvoicingProviderException?> translateProviderError, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_callBudget);

        try
        {
            return await call(cts.Token);
        }
        catch (Exception ex) when (translateProviderError(ex) is { } translated)
        {
            throw translated;
        }
        catch (JsonException ex)
        {
            // A 2xx body that no longer matches the model, or an error body that does not match its
            // generated error shape. Either way the detail is unusable — surface it as a provider fault.
            throw new InvoicingProviderException(
                $"The invoicing provider returned a response that could not be processed while trying to {action}.",
                statusCode: null, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the caller aborted — not a provider fault
        }
        catch (OperationCanceledException ex)
        {
            throw new InvoicingProviderException(
                $"The invoicing provider did not respond in time while trying to {action}.",
                statusCode: null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvoicingProviderException(
                $"The invoicing provider was unreachable while trying to {action}.",
                statusCode: null, ex);
        }
    }

    // -- per-operation error translation (typed accessors named per the SDK contract) ---------------

    private static InvoicingProviderException TranslateCreate(SdkException<CreateInvoiceError> ex)
    {
        if (ex.Error.TryGetInvoicingV2InvoicesPost400Response1(out var b400)) return Provider(400, b400.Reason, b400.Message, ex);
        if (ex.Error.TryGetInvoicingV2InvoicesPost404Response1(out var b404)) return Provider(404, b404.Reason, b404.Message, ex);
        if (ex.Error.TryGetInvoicingV2InvoicesPost502Response1(out var b502)) return Provider(502, b502.Reason, b502.Message, ex);
        return FromRawError(ex.Error, ex);
    }

    private static InvoicingProviderException TranslateGet(SdkException<GetInvoiceError> ex)
    {
        if (ex.Error.TryGetInvoicingV2InvoicesGet400Response1(out var b400)) return Provider(400, b400.Reason, b400.Message, ex);
        if (ex.Error.TryGetInvoicingV2InvoicesGet404Response1(out var b404)) return Provider(404, b404.Reason, b404.Message, ex);
        if (ex.Error.TryGetInvoicingV2InvoicesGet502Response1(out var b502)) return Provider(502, b502.Reason, b502.Message, ex);
        return FromRawError(ex.Error, ex);
    }

    private static InvoicingProviderException TranslateUpdate(SdkException<UpdateInvoiceError> ex)
    {
        if (ex.Error.TryGetInvoicingV2InvoicesPut400Response1(out var b400)) return Provider(400, b400.Reason, b400.Message, ex);
        if (ex.Error.TryGetInvoicingV2InvoicesPut404Response1(out var b404)) return Provider(404, b404.Reason, b404.Message, ex);
        if (ex.Error.TryGetInvoicingV2InvoicesPut502Response1(out var b502)) return Provider(502, b502.Reason, b502.Message, ex);
        return FromRawError(ex.Error, ex);
    }

    private static InvoicingProviderException TranslateSend(SdkException<PerformSendActionError> ex)
    {
        if (ex.Error.TryGetInvoicingV2InvoicesSend400Response1(out var b400)) return Provider(400, b400.Reason, b400.Message, ex);
        if (ex.Error.TryGetInvoicingV2InvoicesSend404Response1(out var b404)) return Provider(404, b404.Reason, b404.Message, ex);
        if (ex.Error.TryGetInvoicingV2InvoicesSend502Response1(out var b502)) return Provider(502, b502.Reason, b502.Message, ex);
        return FromRawError(ex.Error, ex);
    }

    private static InvoicingProviderException TranslateCancel(SdkException<PerformCancelActionError> ex)
    {
        if (ex.Error.TryGetInvoicingV2InvoicesCancel400Response1(out var b400)) return Provider(400, b400.Reason, b400.Message, ex);
        if (ex.Error.TryGetInvoicingV2InvoicesCancel404Response1(out var b404)) return Provider(404, b404.Reason, b404.Message, ex);
        if (ex.Error.TryGetInvoicingV2InvoicesCancel502Response1(out var b502)) return Provider(502, b502.Reason, b502.Message, ex);
        return FromRawError(ex.Error, ex);
    }

    private static InvoicingProviderException TranslateList(SdkException<GetAllInvoicesError> ex)
    {
        if (ex.Error.TryGetInvoicingV2InvoicesAllGet400Response1(out var b400)) return Provider(400, b400.Reason, b400.Message, ex);
        if (ex.Error.TryGetInvoicingV2InvoicesAllGet404Response1(out var b404)) return Provider(404, b404.Reason, b404.Message, ex);
        if (ex.Error.TryGetInvoicingV2InvoicesAllGet502Response1(out var b502)) return Provider(502, b502.Reason, b502.Message, ex);
        return FromRawError(ex.Error, ex);
    }

    private static InvoicingProviderException FromRawError(ApiError error, Exception ex)
    {
        if (error.TryGetRawError(out RawError raw))
            return new InvoicingProviderException(
                "The invoicing provider rejected the request.", (int)raw.StatusCode, ex);

        return new InvoicingProviderException("The invoicing provider rejected the request.", statusCode: null, ex);
    }

    private static InvoicingProviderException Provider(int status, string? reason, string? message, Exception inner)
    {
        var detail = FirstNonEmpty(message, reason) ?? "The invoicing provider rejected the request.";
        return new InvoicingProviderException(detail, status, inner);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
