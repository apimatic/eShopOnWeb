using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CyberSource.Api;
using CyberSource.Model;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.Extensions.Options;
using ClientConfiguration = CyberSource.Client.Configuration;
using ApiException = CyberSource.Client.ApiException;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Talks to Visa's CyberSource Invoicing API (<c>/invoicing/v2/invoices</c>) via the CyberSource .NET
/// REST SDK. This is the single place the application reaches the provider.
///
/// <para>Authentication is JWT with a shared secret (HS256), built from the configured merchant id, key
/// id and secret key. The base address is taken verbatim from <see cref="VisaSettings.BaseUrl"/>: its
/// authority becomes the SDK <c>runEnvironment</c>, so every provider call is routed through the
/// configured host with no hard-coded default. The secret key is only ever handed to the SDK to sign
/// requests; it is never logged nor returned.</para>
/// </summary>
public class CyberSourceInvoicingService : IVisaInvoicingService
{
    // Reconciliation paging/enrichment bounds. The provider's list endpoint returns invoices
    // newest-first but carries no per-invoice creation date, so the creation timestamp is read from
    // each invoice's history via a follow-up GetInvoice. These caps keep the report bounded on a
    // shared sandbox with a large, ever-growing history.
    private const int ReconciliationPageSize = 100;
    private const int ReconciliationMaxPages = 50;
    private const int ReconciliationMaxLookups = 500;

    private readonly VisaSettings _settings;
    private readonly IAppLogger<CyberSourceInvoicingService> _logger;
    private readonly object _configLock = new();
    private ClientConfiguration? _clientConfiguration;

    public CyberSourceInvoicingService(IOptions<VisaSettings> settings, IAppLogger<CyberSourceInvoicingService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<VisaInvoiceState> RaiseInvoiceAsync(VisaInvoiceDraft draft, CancellationToken cancellationToken = default)
    {
        var request = new CreateInvoiceRequest
        {
            CustomerInformation = new Invoicingv2invoicesCustomerInformation
            {
                Name = draft.Customer.Name,
                Email = draft.Customer.Email
            },
            InvoiceInformation = new Invoicingv2invoicesInvoiceInformation
            {
                Description = draft.Description,
                DueDate = draft.DueDate.ToDateTime(TimeOnly.MinValue),
                // Draft only: do not send, and never dispatch an email to the invented fixture address.
                SendImmediately = false,
                AllowPartialPayments = false,
                DeliveryMode = "None"
            },
            OrderInformation = new Invoicingv2invoicesOrderInformation
            {
                AmountDetails = new Invoicingv2invoicesOrderInformationAmountDetails
                {
                    TotalAmount = FormatAmount(draft.Amount),
                    Currency = draft.Currency
                },
                LineItems = BuildLineItems(draft.Lines)
            }
        };

        var api = CreateApi();
        _logger.LogInformation("Raising invoice with provider for a bill of {Amount} {Currency}.", draft.Amount, draft.Currency);
        var response = await ExecuteAsync(() => api.CreateInvoiceAsync(request), "raise the invoice");
        return MapState(response.Id, response.Status, response.SubmitTimeUtc, response.InvoiceInformation, response.OrderInformation, response.CustomerInformation, history: null);
    }

    public async Task<VisaInvoiceState> GetInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        var response = await ExecuteAsync(() => api.GetInvoiceAsync(providerInvoiceId), "read the invoice");
        return MapState(response.Id, response.Status, response.SubmitTimeUtc, response.InvoiceInformation, response.OrderInformation, response.CustomerInformation, response.InvoiceHistory);
    }

    public async Task<VisaInvoiceState> UpdateInvoiceAsync(string providerInvoiceId, VisaInvoiceDraft draft, CancellationToken cancellationToken = default)
    {
        var request = new UpdateInvoiceRequest
        {
            CustomerInformation = new Invoicingv2invoicesCustomerInformation
            {
                Name = draft.Customer.Name,
                Email = draft.Customer.Email
            },
            InvoiceInformation = new Invoicingv2invoicesidInvoiceInformation
            {
                Description = draft.Description,
                DueDate = draft.DueDate.ToDateTime(TimeOnly.MinValue),
                DeliveryMode = "None"
            },
            // The amount is re-supplied from the order so a correction can never change what is billed.
            OrderInformation = new Invoicingv2invoicesOrderInformation
            {
                AmountDetails = new Invoicingv2invoicesOrderInformationAmountDetails
                {
                    TotalAmount = FormatAmount(draft.Amount),
                    Currency = draft.Currency
                },
                LineItems = BuildLineItems(draft.Lines)
            }
        };

        var api = CreateApi();
        await ExecuteAsync(() => api.UpdateInvoiceAsync(providerInvoiceId, request), "correct the invoice");
        // Read back the authoritative state after the correction.
        return await GetInvoiceAsync(providerInvoiceId, cancellationToken);
    }

    public async Task<VisaInvoiceState> IssueInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        await ExecuteAsync(() => api.PerformPublishActionAsync(providerInvoiceId), "issue the invoice");
        return await GetInvoiceAsync(providerInvoiceId, cancellationToken);
    }

    public async Task<VisaInvoiceState> WithdrawInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        await ExecuteAsync(() => api.PerformCancelActionAsync(providerInvoiceId), "withdraw the invoice");
        return await GetInvoiceAsync(providerInvoiceId, cancellationToken);
    }

    public async Task<IReadOnlyList<VisaProviderInvoice>> ListInvoicesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        var results = new List<VisaProviderInvoice>();
        var lookups = 0;

        // The provider's list endpoint pages by offset/limit, returns invoices newest-first, and does
        // not carry a per-invoice creation date. We therefore page from the newest invoice, read each
        // invoice's creation date from its history (a follow-up GetInvoice), keep the ones whose
        // creation date falls in the range, and stop once we reach invoices older than the range —
        // covering the whole range without walking the entire (shared) ledger.
        for (var page = 0; page < ReconciliationMaxPages; page++)
        {
            var offset = page * ReconciliationPageSize;
            var response = await ExecuteAsync(() => api.GetAllInvoicesAsync(offset, ReconciliationPageSize), "list invoices for reconciliation");

            var invoices = response.Invoices;
            if (invoices is null || invoices.Count == 0)
            {
                return results;
            }

            foreach (var invoice in invoices)
            {
                var id = invoice.Id;
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                if (lookups >= ReconciliationMaxLookups)
                {
                    // Safety cap reached; return what we have rather than hammering the shared sandbox.
                    return results;
                }

                var created = await GetCreationDateAsync(api, id);
                lookups++;

                if (created is null)
                {
                    continue;
                }

                if (created < from)
                {
                    // Newest-first: this invoice and every one after it is older than the range.
                    return results;
                }

                if (created > to)
                {
                    continue;
                }

                results.Add(new VisaProviderInvoice(
                    ProviderInvoiceId: id,
                    Status: invoice.Status ?? string.Empty,
                    CreatedUtc: created,
                    Amount: ParseAmount(invoice.OrderInformation?.AmountDetails?.TotalAmount),
                    Currency: invoice.OrderInformation?.AmountDetails?.Currency,
                    CustomerName: invoice.CustomerInformation?.Name));
            }

            if (invoices.Count < ReconciliationPageSize)
            {
                return results;
            }
        }

        return results;
    }

    /// <summary>
    /// The date a bill was raised, read from the earliest event in its history. The provider's list
    /// endpoint does not carry this, so it is fetched per invoice with GetInvoice.
    /// </summary>
    private async Task<DateTimeOffset?> GetCreationDateAsync(InvoicesApi api, string id)
    {
        var response = await ExecuteAsync(() => api.GetInvoiceAsync(id), "read an invoice's creation date");

        DateTimeOffset? earliest = null;
        if (response.InvoiceHistory is not null)
        {
            foreach (var history in response.InvoiceHistory)
            {
                if (history.Date is { } date)
                {
                    var when = new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc));
                    if (earliest is null || when < earliest)
                    {
                        earliest = when;
                    }
                }
            }
        }

        // Fall back to null (not SubmitTimeUtc, which is the read time, not the creation time) so an
        // invoice with no history is simply not placed in the range rather than mis-dated to "now".
        return earliest;
    }

    private static List<Invoicingv2invoicesOrderInformationLineItems> BuildLineItems(IReadOnlyList<VisaInvoiceLine> lines) =>
        lines.Select(line => new Invoicingv2invoicesOrderInformationLineItems
        {
            ProductName = line.ProductName,
            ProductSku = line.Sku,
            Quantity = line.Quantity,
            UnitPrice = FormatAmount(line.UnitPrice),
            TotalAmount = FormatAmount(line.UnitPrice * line.Quantity)
        }).ToList();

    private VisaInvoiceState MapState(
        string? id,
        string? status,
        string? submitTimeUtc,
        InvoicingV2InvoicesPost201ResponseInvoiceInformation? invoiceInformation,
        InvoicingV2InvoicesPost201ResponseOrderInformation? orderInformation,
        Invoicingv2invoicesCustomerInformation? customerInformation,
        List<InvoicingV2InvoicesGet200ResponseInvoiceHistory>? history)
    {
        var events = history?
            .Select(h => new VisaInvoiceEvent(h.Event, h.Date.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(h.Date.Value, DateTimeKind.Utc)) : null))
            .ToList() ?? new List<VisaInvoiceEvent>();

        DateOnly? dueDate = invoiceInformation?.DueDate is { } due ? DateOnly.FromDateTime(due) : null;

        return new VisaInvoiceState(
            ProviderInvoiceId: id ?? string.Empty,
            Status: status ?? string.Empty,
            PaymentLink: invoiceInformation?.PaymentLink,
            Amount: ParseAmount(orderInformation?.AmountDetails?.TotalAmount),
            Currency: orderInformation?.AmountDetails?.Currency,
            DueDate: dueDate,
            CustomerName: customerInformation?.Name,
            CustomerEmail: customerInformation?.Email,
            Description: invoiceInformation?.Description,
            SubmittedUtc: ParseTimestamp(submitTimeUtc),
            History: events);
    }

    private InvoicesApi CreateApi() => new(GetClientConfiguration());

    private ClientConfiguration GetClientConfiguration()
    {
        if (_clientConfiguration is not null)
        {
            return _clientConfiguration;
        }

        lock (_configLock)
        {
            _clientConfiguration ??= BuildClientConfiguration();
        }

        return _clientConfiguration;
    }

    private ClientConfiguration BuildClientConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            throw new VisaInvoicingException("Visa:BaseUrl is not configured; the provider base address must be supplied through configuration.");
        }

        if (!Uri.TryCreate(_settings.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new VisaInvoicingException($"Visa:BaseUrl '{_settings.BaseUrl}' is not a valid absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(_settings.MerchantId) ||
            string.IsNullOrWhiteSpace(_settings.KeyId) ||
            string.IsNullOrWhiteSpace(_settings.SecretKey))
        {
            throw new VisaInvoicingException("Visa credentials are not configured; MerchantId, KeyId and SecretKey must be supplied through user-secrets or environment.");
        }

        // The SDK targets https://{runEnvironment}. Feeding it the configured base URL's authority
        // (host[:port], no scheme) routes every call through the configured address — nothing is
        // hard-coded and a different Visa:BaseUrl re-targets the whole integration.
        var runEnvironment = baseUri.Authority;

        var merchantConfig = new Dictionary<string, string>
        {
            ["merchantID"] = _settings.MerchantId,
            ["runEnvironment"] = runEnvironment,
            ["authenticationType"] = "jwt",
            ["jwtKeyType"] = "SHARED_SECRET",
            ["merchantKeyId"] = _settings.KeyId,
            ["merchantsecretKey"] = _settings.SecretKey,
            ["isSDK"] = "true"
        };

        _logger.LogInformation("Visa invoicing configured against provider host {Host}.", runEnvironment);
        return new ClientConfiguration(merchConfigDictObj: merchantConfig);
    }

    private async Task<T> ExecuteAsync<T>(Func<Task<T>> action, string operation)
    {
        try
        {
            return await action();
        }
        catch (ApiException ex)
        {
            var reason = DescribeProviderError(ex);
            // The provider status is preserved so callers can distinguish a legitimate state-based
            // refusal (4xx) from a provider outage (5xx).
            _logger.LogWarning("Provider refused/failed attempt to {Operation}: HTTP {Status}. {Reason}", operation, ex.ErrorCode, reason);
            throw new VisaInvoicingException($"The invoicing provider could not {operation}.", ex.ErrorCode, reason);
        }
        catch (VisaInvoicingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Unexpected error trying to {Operation}: {Error}", operation, ex.Message);
            throw new VisaInvoicingException($"Could not {operation} because of an unexpected error contacting the invoicing provider.", ex);
        }
    }

    private static string? DescribeProviderError(ApiException ex)
    {
        var content = ex.ErrorContent?.ToString();
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        // The provider error body is safe to surface (it carries no credentials); trim it to keep
        // responses and logs bounded.
        content = content.Trim();
        return content.Length > 1000 ? content[..1000] : content;
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
}
