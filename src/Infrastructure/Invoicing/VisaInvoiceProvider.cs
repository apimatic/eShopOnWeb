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

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// The sole place the Visa/CyberSource invoicing SDK is used. It maps eShop's provider-agnostic
/// commands onto the SDK's request models and back, and converts every provider/transport/parse failure
/// into <see cref="InvoiceProviderException"/> so the rest of the app sees a single failure type carrying
/// the provider's HTTP status when there was one.
/// </summary>
public class VisaInvoiceProvider : IInvoiceProvider
{
    // A whole-call budget across the SDK's per-attempt retries, so a hung/looping provider can never pin a
    // request thread indefinitely (see dotnet-configuration-resilience). Linked to the caller's token.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);

    private readonly CyberSourceMergedSpecClient _client;

    public VisaInvoiceProvider(CyberSourceMergedSpecClient client)
    {
        _client = client;
    }

    public async Task<ProviderInvoice> RaiseAsync(RaiseInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        var request = new CreateInvoiceRequest
        {
            CustomerInformation = new CustomerInformation
            {
                Name = command.CustomerName,
                Email = command.CustomerEmail,
                MerchantCustomerId = command.MerchantReference
            },
            InvoiceInformation = new InvoiceInformation
            {
                Description = command.Description,
                DueDate = command.DueDate,
                SendImmediately = false
            },
            OrderInformation = new OrderInformation60
            {
                AmountDetails = new AmountDetails60
                {
                    TotalAmount = FormatAmount(command.Amount),
                    Currency = command.Currency
                },
                LineItems = command.Lines.Select(l => new LineItem17
                {
                    ProductSku = l.ProductSku,
                    ProductName = l.ProductName,
                    Quantity = l.Quantity,
                    UnitPrice = FormatAmount(l.UnitPrice),
                    TotalAmount = FormatAmount(l.TotalAmount)
                }).ToList()
            }
        };

        var response = await InvokeAsync(
            ct => _client.Invoices.CreateInvoice(request, ct: ct),
            ex => ex is SdkException<CreateInvoiceError> e ? MapCreate(e) : null,
            cancellationToken);

        return new ProviderInvoice(response.Id ?? string.Empty, response.Status,
            response.InvoiceInformation?.PaymentLink, Array.Empty<ProviderInvoiceEvent>());
    }

    public async Task<ProviderInvoice> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(
            ct => _client.Invoices.GetInvoice(providerInvoiceId, ct: ct),
            ex => ex is SdkException<GetInvoiceError> e ? MapGet(e) : null,
            cancellationToken);

        var history = response.InvoiceHistory?
            .Select(h => new ProviderInvoiceEvent(h.Event, h.Date))
            .ToList() ?? (IReadOnlyList<ProviderInvoiceEvent>)Array.Empty<ProviderInvoiceEvent>();

        return new ProviderInvoice(providerInvoiceId, response.Status,
            response.InvoiceInformation?.PaymentLink, history);
    }

    public async Task<ProviderInvoice> CorrectAsync(string providerInvoiceId, CorrectInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateInvoiceRequest
        {
            CustomerInformation = new CustomerInformation
            {
                Name = command.CustomerName,
                Email = command.CustomerEmail,
                MerchantCustomerId = command.MerchantReference
            },
            // Update is not a partial patch — description and amount are required and re-sent unchanged.
            InvoiceInformation = new InvoiceInformation4
            {
                Description = command.Description,
                DueDate = command.DueDate
            },
            OrderInformation = new OrderInformation60
            {
                AmountDetails = new AmountDetails60
                {
                    TotalAmount = FormatAmount(command.Amount),
                    Currency = command.Currency
                }
            }
        };

        var response = await InvokeAsync(
            ct => _client.Invoices.UpdateInvoice(providerInvoiceId, request, ct: ct),
            ex => ex is SdkException<UpdateInvoiceError> e ? MapUpdate(e) : null,
            cancellationToken);

        return new ProviderInvoice(providerInvoiceId, response.Status,
            response.InvoiceInformation?.PaymentLink, Array.Empty<ProviderInvoiceEvent>());
    }

    public async Task<ProviderInvoice> IssueAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(
            ct => _client.Invoices.PerformSendAction(providerInvoiceId, ct: ct),
            ex => ex is SdkException<PerformSendActionError> e ? MapSend(e) : null,
            cancellationToken);

        return new ProviderInvoice(providerInvoiceId, response.Status,
            response.InvoiceInformation?.PaymentLink, Array.Empty<ProviderInvoiceEvent>());
    }

    public async Task<ProviderInvoice> WithdrawAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(
            ct => _client.Invoices.PerformCancelAction(providerInvoiceId, ct: ct),
            ex => ex is SdkException<PerformCancelActionError> e ? MapCancel(e) : null,
            cancellationToken);

        return new ProviderInvoice(providerInvoiceId, response.Status,
            response.InvoiceInformation?.PaymentLink, Array.Empty<ProviderInvoiceEvent>());
    }

    public async Task<ProviderInvoicePage> ListAsync(int offset, int limit, CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(
            ct => _client.Invoices.GetAllInvoices(offset: offset, limit: limit, status: null, ct: ct),
            ex => ex is SdkException<GetAllInvoicesError> e ? MapList(e) : null,
            cancellationToken);

        var items = response.Invoices?
            .Select(inv => new ProviderInvoiceSummary(
                inv.Id,
                inv.Status,
                inv.CreatedDate,
                inv.CustomerInformation?.MerchantCustomerId,
                inv.CustomerInformation?.Name,
                inv.OrderInformation?.AmountDetails?.TotalAmount,
                inv.OrderInformation?.AmountDetails?.Currency))
            .ToList() ?? new List<ProviderInvoiceSummary>();

        return new ProviderInvoicePage(items, response.TotalInvoices ?? items.Count);
    }

    /// <summary>
    /// Runs one SDK call under a whole-call budget and a single failure-translation boundary. The
    /// per-operation <paramref name="translateSdk"/> recognises that operation's typed
    /// <c>SdkException&lt;{Operation}Error&gt;</c> and reads its status/message; transport, timeout and
    /// unprocessable-body failures are converted here.
    /// </summary>
    private async Task<T> InvokeAsync<T>(Func<CancellationToken, Task<T>> call,
        Func<Exception, InvoiceProviderException?> translateSdk, CancellationToken callerToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cts.CancelAfter(CallBudget);

        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw; // The caller aborted — propagate cancellation rather than reporting a provider failure.
        }
        catch (JsonException ex)
        {
            // A drifted 2xx body, or an error body that didn't match its generated shape (which replaces the
            // SdkException). Either way the response could not be processed.
            throw new InvoiceProviderException(
                "The payment provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex)
        {
            var translated = translateSdk(ex);
            if (translated is not null)
            {
                throw translated;
            }

            if (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                throw new InvoiceProviderException(
                    "The payment provider could not be reached or the request timed out.", null, ex);
            }

            throw;
        }
    }

    // ── Per-operation typed-error mapping ─────────────────────────────────────────────────────────────
    // Every typed 400/404/502 body shares the same shape (Reason/Message). Which accessor fires tells us
    // the HTTP status; TryGetRawError is the last resort for any other status.

    private static InvoiceProviderException MapCreate(SdkException<CreateInvoiceError> ex)
    {
        var e = ex.Error;
        if (e.TryGetInvoicingV2InvoicesPost400Response1(out var b400)) return Provider(400, b400?.Message ?? b400?.Reason, b400?.Details, ex);
        if (e.TryGetInvoicingV2InvoicesPost404Response1(out var b404)) return Provider(404, b404?.Message ?? b404?.Reason, b404?.Details, ex);
        if (e.TryGetInvoicingV2InvoicesPost502Response1(out var b502)) return Provider(502, b502?.Message ?? b502?.Reason, null, ex);
        return FromRaw(e, ex);
    }

    private static InvoiceProviderException MapGet(SdkException<GetInvoiceError> ex)
    {
        var e = ex.Error;
        if (e.TryGetInvoicingV2InvoicesGet400Response1(out var b400)) return Provider(400, b400?.Message ?? b400?.Reason, b400?.Details, ex);
        if (e.TryGetInvoicingV2InvoicesGet404Response1(out var b404)) return Provider(404, b404?.Message ?? b404?.Reason, b404?.Details, ex);
        if (e.TryGetInvoicingV2InvoicesGet502Response1(out var b502)) return Provider(502, b502?.Message ?? b502?.Reason, null, ex);
        return FromRaw(e, ex);
    }

    private static InvoiceProviderException MapUpdate(SdkException<UpdateInvoiceError> ex)
    {
        var e = ex.Error;
        if (e.TryGetInvoicingV2InvoicesPut400Response1(out var b400)) return Provider(400, b400?.Message ?? b400?.Reason, b400?.Details, ex);
        if (e.TryGetInvoicingV2InvoicesPut404Response1(out var b404)) return Provider(404, b404?.Message ?? b404?.Reason, b404?.Details, ex);
        if (e.TryGetInvoicingV2InvoicesPut502Response1(out var b502)) return Provider(502, b502?.Message ?? b502?.Reason, null, ex);
        return FromRaw(e, ex);
    }

    private static InvoiceProviderException MapSend(SdkException<PerformSendActionError> ex)
    {
        var e = ex.Error;
        if (e.TryGetInvoicingV2InvoicesSend400Response1(out var b400)) return Provider(400, b400?.Message ?? b400?.Reason, b400?.Details, ex);
        if (e.TryGetInvoicingV2InvoicesSend404Response1(out var b404)) return Provider(404, b404?.Message ?? b404?.Reason, b404?.Details, ex);
        if (e.TryGetInvoicingV2InvoicesSend502Response1(out var b502)) return Provider(502, b502?.Message ?? b502?.Reason, null, ex);
        return FromRaw(e, ex);
    }

    private static InvoiceProviderException MapCancel(SdkException<PerformCancelActionError> ex)
    {
        var e = ex.Error;
        if (e.TryGetInvoicingV2InvoicesCancel400Response1(out var b400)) return Provider(400, b400?.Message ?? b400?.Reason, b400?.Details, ex);
        if (e.TryGetInvoicingV2InvoicesCancel404Response1(out var b404)) return Provider(404, b404?.Message ?? b404?.Reason, b404?.Details, ex);
        if (e.TryGetInvoicingV2InvoicesCancel502Response1(out var b502)) return Provider(502, b502?.Message ?? b502?.Reason, null, ex);
        return FromRaw(e, ex);
    }

    private static InvoiceProviderException MapList(SdkException<GetAllInvoicesError> ex)
    {
        var e = ex.Error;
        if (e.TryGetInvoicingV2InvoicesAllGet400Response1(out var b400)) return Provider(400, b400?.Message ?? b400?.Reason, b400?.Details, ex);
        if (e.TryGetInvoicingV2InvoicesAllGet404Response1(out var b404)) return Provider(404, b404?.Message ?? b404?.Reason, b404?.Details, ex);
        if (e.TryGetInvoicingV2InvoicesAllGet502Response1(out var b502)) return Provider(502, b502?.Message ?? b502?.Reason, null, ex);
        return FromRaw(e, ex);
    }

    private static InvoiceProviderException FromRaw(ApiError error, Exception ex)
    {
        if (error.TryGetRawError(out RawError raw))
        {
            return Provider((int)raw.StatusCode, null, null, ex);
        }
        return Provider(null, null, null, ex);
    }

    private static InvoiceProviderException Provider(int? status, string? providerMessage,
        IReadOnlyList<Detail>? details, Exception inner)
    {
        var message = string.IsNullOrWhiteSpace(providerMessage)
            ? "The payment provider reported an error."
            : $"The payment provider rejected the request: {providerMessage}";

        var fields = details?
            .Where(d => !string.IsNullOrWhiteSpace(d.Field))
            .Select(d => string.IsNullOrWhiteSpace(d.Reason) ? d.Field! : $"{d.Field} ({d.Reason})")
            .ToList();
        if (fields is { Count: > 0 })
        {
            message += $" [{string.Join("; ", fields)}]";
        }

        return new InvoiceProviderException(message, status, inner);
    }

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);
}
