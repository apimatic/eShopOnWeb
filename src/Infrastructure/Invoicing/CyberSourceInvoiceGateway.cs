using System;
using System.Collections.Generic;
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
using SdkInvoiceHistory = CyberSourceMergedSpec.Models.InvoiceHistory;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// The single seam through which eShop talks to the Visa/CyberSource invoicing API. Translates the
/// domain to/from the SDK's wire model, routes every call through the configured base URL, and surfaces
/// a single failure type (<see cref="InvoiceProviderException"/>) carrying the provider status where one
/// was returned. No secret is ever logged or returned.
/// </summary>
public class CyberSourceInvoiceGateway : IInvoiceProviderGateway
{
    private const int PageSize = 100;
    private const int MaxPages = 100; // hard backstop — never rely on the provider's own stop signal

    private readonly CyberSourceMergedSpecClient _client;
    private readonly VisaSettings _settings;

    public CyberSourceInvoiceGateway(CyberSourceMergedSpecClient client, IOptions<VisaSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<InvoiceReceipt> RaiseAsync(NewInvoiceRequest request, CancellationToken cancellationToken)
    {
        var body = new CreateInvoiceRequest
        {
            CustomerInformation = new CustomerInformation
            {
                Name = request.CustomerName,
                Email = request.CustomerEmail,
                MerchantCustomerId = request.MerchantCustomerId,
            },
            InvoiceInformation = new InvoiceInformation
            {
                InvoiceNumber = request.InvoiceNumber,
                Description = request.Description,
                DueDate = request.DueDate,
                SendImmediately = false, // keep it a draft — not put to the shopper yet
            },
            OrderInformation = new OrderInformation60
            {
                AmountDetails = new AmountDetails60
                {
                    TotalAmount = request.TotalAmount,
                    Currency = request.Currency,
                },
                LineItems = request.Lines.Select(ToLineItem).ToList(),
            },
        };

        using var cts = CreateBudget(cancellationToken);
        try
        {
            var response = await _client.Invoices.CreateInvoice(body, ct: cts.Token);
            var id = response.Id ?? throw new InvoiceProviderException("The provider did not return an invoice id.", null);
            return new InvoiceReceipt(id, response.Status);
        }
        catch (SdkException<CreateInvoiceError> ex)
        {
            var status = ex.Error.TryGetInvoicingV2InvoicesPost400Response1(out _) ? 400
                : ex.Error.TryGetInvoicingV2InvoicesPost404Response1(out _) ? 404
                : ex.Error.TryGetInvoicingV2InvoicesPost502Response1(out _) ? 502
                : ex.Error.TryGetRawError(out var raw) ? (int)raw.StatusCode
                : (int?)null;
            throw Provider("Failed to raise the invoice with the provider.", status, ex);
        }
        catch (Exception ex) when (IsUnprocessable(ex))
        {
            throw Unprocessable(ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport(ex);
        }
    }

    public async Task<InvoiceState> GetAsync(string providerInvoiceId, CancellationToken cancellationToken)
    {
        using var cts = CreateBudget(cancellationToken);
        try
        {
            var response = await _client.Invoices.GetInvoice(providerInvoiceId, ct: cts.Token);
            var history = (response.InvoiceHistory ?? new List<SdkInvoiceHistory>())
                .Select(h => new InvoiceHistoryItem(h.Event, h.Date))
                .ToList();
            return new InvoiceState(response.Id ?? providerInvoiceId, response.Status,
                response.InvoiceInformation?.PaymentLink, history);
        }
        catch (SdkException<GetInvoiceError> ex)
        {
            var status = ex.Error.TryGetInvoicingV2InvoicesGet400Response1(out _) ? 400
                : ex.Error.TryGetInvoicingV2InvoicesGet404Response1(out _) ? 404
                : ex.Error.TryGetInvoicingV2InvoicesGet502Response1(out _) ? 502
                : ex.Error.TryGetRawError(out var raw) ? (int)raw.StatusCode
                : (int?)null;
            throw Provider("Failed to read the invoice from the provider.", status, ex);
        }
        catch (Exception ex) when (IsUnprocessable(ex))
        {
            throw Unprocessable(ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport(ex);
        }
    }

    public async Task CorrectAsync(InvoiceCorrection correction, CancellationToken cancellationToken)
    {
        var body = new UpdateInvoiceRequest
        {
            CustomerInformation = new CustomerInformation
            {
                Name = correction.CustomerName,
                Email = correction.CustomerEmail,
                MerchantCustomerId = correction.MerchantCustomerId,
            },
            InvoiceInformation = new InvoiceInformation4
            {
                Description = correction.Description,
                DueDate = correction.DueDate,
                SendImmediately = false,
            },
            OrderInformation = new OrderInformation60
            {
                AmountDetails = new AmountDetails60
                {
                    TotalAmount = correction.TotalAmount,
                    Currency = correction.Currency,
                },
                LineItems = correction.Lines.Select(ToLineItem).ToList(),
            },
        };

        using var cts = CreateBudget(cancellationToken);
        try
        {
            await _client.Invoices.UpdateInvoice(correction.ProviderInvoiceId, body, ct: cts.Token);
        }
        catch (SdkException<UpdateInvoiceError> ex)
        {
            var status = ex.Error.TryGetInvoicingV2InvoicesPut400Response1(out _) ? 400
                : ex.Error.TryGetInvoicingV2InvoicesPut404Response1(out _) ? 404
                : ex.Error.TryGetInvoicingV2InvoicesPut502Response1(out _) ? 502
                : ex.Error.TryGetRawError(out var raw) ? (int)raw.StatusCode
                : (int?)null;
            throw Provider("Failed to correct the invoice with the provider.", status, ex);
        }
        catch (Exception ex) when (IsUnprocessable(ex))
        {
            throw Unprocessable(ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport(ex);
        }
    }

    public async Task<InvoiceState> IssueAsync(string providerInvoiceId, CancellationToken cancellationToken)
    {
        using var cts = CreateBudget(cancellationToken);
        try
        {
            var response = await _client.Invoices.PerformSendAction(providerInvoiceId, ct: cts.Token);
            return new InvoiceState(response.Id ?? providerInvoiceId, response.Status,
                response.InvoiceInformation?.PaymentLink, Array.Empty<InvoiceHistoryItem>());
        }
        catch (SdkException<PerformSendActionError> ex)
        {
            var status = ex.Error.TryGetInvoicingV2InvoicesSend400Response1(out _) ? 400
                : ex.Error.TryGetInvoicingV2InvoicesSend404Response1(out _) ? 404
                : ex.Error.TryGetInvoicingV2InvoicesSend502Response1(out _) ? 502
                : ex.Error.TryGetRawError(out var raw) ? (int)raw.StatusCode
                : (int?)null;
            throw Provider("Failed to issue the invoice with the provider.", status, ex);
        }
        catch (Exception ex) when (IsUnprocessable(ex))
        {
            throw Unprocessable(ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport(ex);
        }
    }

    public async Task WithdrawAsync(string providerInvoiceId, CancellationToken cancellationToken)
    {
        using var cts = CreateBudget(cancellationToken);
        try
        {
            await _client.Invoices.PerformCancelAction(providerInvoiceId, ct: cts.Token);
        }
        catch (SdkException<PerformCancelActionError> ex)
        {
            var status = ex.Error.TryGetInvoicingV2InvoicesCancel400Response1(out _) ? 400
                : ex.Error.TryGetInvoicingV2InvoicesCancel404Response1(out _) ? 404
                : ex.Error.TryGetInvoicingV2InvoicesCancel502Response1(out _) ? 502
                : ex.Error.TryGetRawError(out var raw) ? (int)raw.StatusCode
                : (int?)null;
            throw Provider("Failed to withdraw the invoice with the provider.", status, ex);
        }
        catch (Exception ex) when (IsUnprocessable(ex))
        {
            throw Unprocessable(ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport(ex);
        }
    }

    public async Task<IReadOnlyList<ProviderInvoiceRecord>> ListAllAsync(CancellationToken cancellationToken)
    {
        var results = new List<ProviderInvoiceRecord>();
        var offset = 0;

        for (var page = 0; page < MaxPages; page++)
        {
            InvoicingV2InvoicesAllGet200Response response;
            using (var cts = CreateBudget(cancellationToken))
            {
                try
                {
                    // status: null == all statuses. Named args: 'status' has no C# default.
                    response = await _client.Invoices.GetAllInvoices(offset: offset, limit: PageSize, status: null, ct: cts.Token);
                }
                catch (SdkException<GetAllInvoicesError> ex)
                {
                    var status = ex.Error.TryGetInvoicingV2InvoicesAllGet400Response1(out _) ? 400
                        : ex.Error.TryGetInvoicingV2InvoicesAllGet404Response1(out _) ? 404
                        : ex.Error.TryGetInvoicingV2InvoicesAllGet502Response1(out _) ? 502
                        : ex.Error.TryGetRawError(out var raw) ? (int)raw.StatusCode
                        : (int?)null;
                    throw Provider("Failed to list invoices from the provider.", status, ex);
                }
                catch (Exception ex) when (IsUnprocessable(ex))
                {
                    throw Unprocessable(ex);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (IsTransport(ex))
                {
                    throw Transport(ex);
                }
            }

            var invoices = response.Invoices ?? new List<Invoice1>();
            if (invoices.Count == 0)
                break;

            foreach (var invoice in invoices)
            {
                results.Add(new ProviderInvoiceRecord(
                    ProviderInvoiceId: invoice.Id,
                    Status: invoice.Status,
                    MerchantCustomerId: invoice.CustomerInformation?.MerchantCustomerId,
                    TotalAmount: invoice.OrderInformation?.AmountDetails?.TotalAmount,
                    Currency: invoice.OrderInformation?.AmountDetails?.Currency));
            }

            offset += PageSize;
            var total = response.TotalInvoices ?? 0;
            if (offset >= total)
                break;
        }

        return results;
    }

    private CancellationTokenSource CreateBudget(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds));
        return cts;
    }

    private static LineItem17 ToLineItem(InvoiceLine line) => new LineItem17
    {
        ProductSku = line.Sku,           // required by the provider on every line
        ProductName = line.ProductName,
        UnitPrice = line.UnitPrice,
        Quantity = line.Quantity,
    };

    private static bool IsUnprocessable(Exception ex) => ex is JsonException;

    private static bool IsTransport(Exception ex) => ex is HttpRequestException or TaskCanceledException;

    private static InvoiceProviderException Provider(string message, int? status, Exception inner) =>
        new(message, status, inner);

    // A drifted/malformed body from the provider (a JsonException) is our-side-unknown → no status (mapped 5xx).
    private static InvoiceProviderException Unprocessable(Exception ex) =>
        new("The invoicing provider returned a response that could not be processed.", null, ex);

    // Host unreachable, DNS/socket failure, or our per-call budget elapsing → no provider status.
    private static InvoiceProviderException Transport(Exception ex) =>
        new("The invoicing provider could not be reached.", null, ex);
}
