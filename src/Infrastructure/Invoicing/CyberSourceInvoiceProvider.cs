using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CyberSource.Api;
using CyberSource.Client;
using CyberSource.Model;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// The Visa / CyberSource implementation of <see cref="IInvoiceProvider"/>, built on the CyberSource
/// REST SDK's Invoicing API. Every call is authenticated with a JWT signed from the shared-secret
/// credentials and routed through the configured <c>Visa:BaseUrl</c> (the SDK targets the URL's host
/// and a base-address handler forces the full base address verbatim). Domain shapes are mapped to and
/// from the SDK's models here so that the rest of the application never sees the SDK.
/// </summary>
public sealed class CyberSourceInvoiceProvider : IInvoiceProvider
{
    // Bounds the enumeration of the shared provider account during reconciliation.
    private const int PageSize = 100;
    private const int MaxInvoicesToEnumerate = 2000;
    private const int MaxConcurrentDetailReads = 6;

    private readonly VisaSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAppLogger<CyberSourceInvoiceProvider> _logger;

    public CyberSourceInvoiceProvider(
        IOptions<VisaSettings> settings,
        IHttpClientFactory httpClientFactory,
        IAppLogger<CyberSourceInvoiceProvider> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ProviderInvoiceRef> CreateDraftAsync(ProviderInvoiceDraft draft, CancellationToken cancellationToken = default)
    {
        var request = new CreateInvoiceRequest(
            CustomerInformation: new Invoicingv2invoicesCustomerInformation(
                Name: draft.CustomerName,
                Email: draft.CustomerEmail,
                MerchantCustomerId: draft.MerchantReference),
            InvoiceInformation: new Invoicingv2invoicesInvoiceInformation(
                InvoiceNumber: draft.InvoiceNumber,
                Description: draft.Description,
                DueDate: ToProviderDate(draft.DueDate),
                // The bill starts out not yet put to the shopper: created as a draft, no email dispatched.
                SendImmediately: false,
                DeliveryMode: "None"),
            OrderInformation: BuildOrderInformation(draft.Amount, draft.CurrencyCode, draft.LineItems));

        var api = CreateApi();
        var response = await Execute(() => api.CreateInvoiceAsync(request), "create invoice", cancellationToken);
        return new ProviderInvoiceRef(
            response.Id ?? draft.InvoiceNumber,
            response.InvoiceInformation?.InvoiceNumber ?? draft.InvoiceNumber,
            response.Status ?? "DRAFT");
    }

    public async Task<ProviderInvoiceDetails> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        var response = await Execute(() => api.GetInvoiceAsync(providerInvoiceId), "get invoice", cancellationToken);

        return new ProviderInvoiceDetails(
            response.Id ?? providerInvoiceId,
            response.InvoiceInformation?.InvoiceNumber ?? providerInvoiceId,
            response.Status ?? string.Empty,
            NullIfEmpty(response.InvoiceInformation?.PaymentLink),
            ParseAmount(response.OrderInformation?.AmountDetails?.TotalAmount),
            response.OrderInformation?.AmountDetails?.Currency,
            ToDateOnly(response.InvoiceInformation?.DueDate),
            response.CustomerInformation?.Name,
            response.CustomerInformation?.Email,
            MapHistory(response.InvoiceHistory));
    }

    public async Task<ProviderInvoiceRef> UpdateAsync(string providerInvoiceId, ProviderInvoiceUpdate update, CancellationToken cancellationToken = default)
    {
        var request = new UpdateInvoiceRequest(
            CustomerInformation: new Invoicingv2invoicesCustomerInformation(
                Name: update.CustomerName,
                Email: update.CustomerEmail,
                MerchantCustomerId: update.MerchantReference),
            InvoiceInformation: new Invoicingv2invoicesidInvoiceInformation(
                Description: update.Description,
                DueDate: ToProviderDate(update.DueDate)),
            OrderInformation: BuildOrderInformation(update.Amount, update.CurrencyCode, update.LineItems));

        var api = CreateApi();
        var response = await Execute(() => api.UpdateInvoiceAsync(providerInvoiceId, request), "update invoice", cancellationToken);
        return new ProviderInvoiceRef(
            response.Id ?? providerInvoiceId,
            response.InvoiceInformation?.InvoiceNumber ?? update.InvoiceNumber,
            response.Status ?? string.Empty);
    }

    public async Task<ProviderInvoiceRef> IssueAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        // Putting the bill to the shopper = delivering (sending) the invoice: it becomes SENT and a
        // payment link is handed out.
        var api = CreateApi();
        var response = await Execute(() => api.PerformSendActionAsync(providerInvoiceId), "issue (send) invoice", cancellationToken);
        return new ProviderInvoiceRef(
            response.Id ?? providerInvoiceId,
            response.InvoiceInformation?.InvoiceNumber ?? providerInvoiceId,
            response.Status ?? string.Empty);
    }

    public async Task<ProviderInvoiceRef> WithdrawAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        var response = await Execute(() => api.PerformCancelActionAsync(providerInvoiceId), "withdraw (cancel) invoice", cancellationToken);
        return new ProviderInvoiceRef(
            response.Id ?? providerInvoiceId,
            response.InvoiceInformation?.InvoiceNumber ?? providerInvoiceId,
            response.Status ?? string.Empty);
    }

    public async Task<IReadOnlyList<ProviderInvoiceSummary>> ListRaisedBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();

        // 1) Enumerate the provider account. The list does not carry per-invoice creation dates, so
        //    we page through the summaries first, then resolve each bill's raised date from its history.
        var summaries = new List<InvoicingV2InvoicesAllGet200ResponseInvoices>();
        var offset = 0;
        while (offset < MaxInvoicesToEnumerate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await Execute(() => api.GetAllInvoicesAsync(offset, PageSize), "list invoices", cancellationToken);
            var invoices = page.Invoices ?? new List<InvoicingV2InvoicesAllGet200ResponseInvoices>();
            summaries.AddRange(invoices);

            var total = page.TotalInvoices ?? summaries.Count;
            offset += invoices.Count;
            if (invoices.Count == 0 || offset >= total)
            {
                break;
            }
        }

        // 2) Resolve each bill's raised date (earliest history event) and keep those within range.
        //    Bounded concurrency keeps the operator report responsive without hammering the provider.
        var gate = new SemaphoreSlim(MaxConcurrentDetailReads);
        var tasks = summaries.Select(async summary =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var details = await Execute(() => api.GetInvoiceAsync(summary.Id), "get invoice for reconciliation", cancellationToken);
                var raisedAt = EarliestHistoryDate(details.InvoiceHistory);
                if (raisedAt is null || raisedAt < from || raisedAt > to)
                {
                    return null;
                }

                return new ProviderInvoiceSummary(
                    summary.Id ?? details.Id ?? string.Empty,
                    details.InvoiceInformation?.InvoiceNumber ?? summary.Id ?? string.Empty,
                    summary.Status ?? details.Status ?? string.Empty,
                    ParseAmount(summary.OrderInformation?.AmountDetails?.TotalAmount ?? details.OrderInformation?.AmountDetails?.TotalAmount),
                    summary.OrderInformation?.AmountDetails?.Currency ?? details.OrderInformation?.AmountDetails?.Currency,
                    summary.CustomerInformation?.Name ?? details.CustomerInformation?.Name,
                    raisedAt);
            }
            finally
            {
                gate.Release();
            }
        });

        var resolved = await Task.WhenAll(tasks);
        return resolved.Where(s => s is not null).Select(s => s!).OrderByDescending(s => s.RaisedAt).ToList();
    }

    // ----- SDK plumbing -------------------------------------------------------------------------

    private InvoicesApi CreateApi() => new InvoicesApi(BuildConfiguration());

    private Configuration BuildConfiguration()
    {
        // The SDK signs the JWT for, and targets, the host of the configured base URL; the injected
        // HttpClient additionally forces the full base address verbatim on every request.
        var merchantConfig = new Dictionary<string, string>
        {
            { "authenticationType", "jwt" },
            { "jwtKeyType", "SHARED_SECRET" },
            { "merchantID", _settings.MerchantId },
            { "merchantKeyId", _settings.KeyId },
            { "merchantsecretKey", _settings.SecretKey },
            { "runEnvironment", new Uri(_settings.BaseUrl).Authority },
            { "isSDK", "true" },
            { "timeout", "60000" },
        };

        var httpClient = _httpClientFactory.CreateClient(VisaInvoicingRegistration.HttpClientName);
        return new Configuration(merchConfigDictObj: merchantConfig, httpClient: httpClient);
    }

    private async Task<T> Execute<T>(Func<Task<T>> call, string action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await call();
        }
        catch (ApiException ex)
        {
            // ex.ErrorContent is the provider's response body (validation/state reason) — it never
            // contains our credentials, which travel only in the request Authorization header.
            _logger.LogWarning("Provider call failed to {Action}. HTTP {Code}: {Body}", action, ex.ErrorCode, ex.ErrorContent ?? ex.Message);
            throw new InvoiceProviderException($"The invoicing provider rejected the request to {action}.", ex);
        }
        catch (InvoiceProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Provider call errored while trying to {Action}: {Message}", action, ex.Message);
            throw new InvoiceProviderException($"The invoicing provider could not be reached to {action}.", ex);
        }
    }

    // ----- mapping helpers ----------------------------------------------------------------------

    private static Invoicingv2invoicesOrderInformation BuildOrderInformation(decimal amount, string currency, IReadOnlyList<ProviderLineItem> lineItems) =>
        new Invoicingv2invoicesOrderInformation(
            AmountDetails: new Invoicingv2invoicesOrderInformationAmountDetails(
                TotalAmount: FormatAmount(amount),
                Currency: currency),
            LineItems: lineItems.Select(li => new Invoicingv2invoicesOrderInformationLineItems(
                ProductSku: li.Sku,
                ProductName: li.ProductName,
                Quantity: li.Quantity,
                UnitPrice: FormatAmount(li.UnitPrice))).ToList());

    private static IReadOnlyList<ProviderInvoiceEvent> MapHistory(List<InvoicingV2InvoicesGet200ResponseInvoiceHistory>? history) =>
        history is null
            ? Array.Empty<ProviderInvoiceEvent>()
            : history.Select(h => new ProviderInvoiceEvent(h.Event ?? string.Empty, ToDateTimeOffset(h.Date))).ToList();

    private static DateTimeOffset? EarliestHistoryDate(List<InvoicingV2InvoicesGet200ResponseInvoiceHistory>? history)
    {
        if (history is null || history.Count == 0)
        {
            return null;
        }

        DateTimeOffset? earliest = null;
        foreach (var h in history)
        {
            var at = ToDateTimeOffset(h.Date);
            if (at is not null && (earliest is null || at < earliest))
            {
                earliest = at;
            }
        }
        return earliest;
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? amount) =>
        decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static DateTime ToProviderDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private static DateOnly? ToDateOnly(DateTime? dateTime) => dateTime is null ? null : DateOnly.FromDateTime(dateTime.Value);

    private static DateTimeOffset? ToDateTimeOffset(DateTime? dateTime) =>
        dateTime is null ? null : new DateTimeOffset(DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Utc));

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
