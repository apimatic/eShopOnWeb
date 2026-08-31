using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CyberSource.Api;
using CyberSource.Client;
using CyberSource.Model;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// The Visa invoicing adapter. It is the only place in the codebase that talks to the
/// CyberSource SDK, translating the application's provider port to the SDK's Invoicing API.
///
/// Every request is routed through the configured <c>Visa:BaseUrl</c>: the SDK's
/// request host is set from that base address and nothing else, so no call carries a
/// hard-coded host. Authentication uses the JWT shared-secret credentials supplied from
/// the environment; the secret is never logged, returned, or written anywhere.
/// </summary>
public class VisaCyberSourceInvoiceGateway : IVisaInvoiceGateway
{
    private const int PageSize = 100;
    private const int MaxInvoicesScanned = 5000;
    private const int DetailFetchConcurrency = 5;

    private readonly VisaSettings _settings;
    private readonly Lazy<InvoicesApi> _api;

    public VisaCyberSourceInvoiceGateway(IOptions<VisaSettings> settings)
    {
        _settings = settings.Value;
        _api = new Lazy<InvoicesApi>(BuildApi, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string AccountCurrency =>
        string.IsNullOrWhiteSpace(_settings.Currency) ? "USD" : _settings.Currency.Trim().ToUpperInvariant();

    public async Task<ProviderInvoiceState> CreateDraftAsync(ProviderInvoiceDraft draft, CancellationToken cancellationToken = default)
    {
        var request = new CreateInvoiceRequest(
            InvoiceInformation: new Invoicingv2invoicesInvoiceInformation(
                InvoiceNumber: string.IsNullOrWhiteSpace(draft.InvoiceNumber) ? null : draft.InvoiceNumber,
                Description: draft.Description,
                DueDate: ToProviderDate(draft.DueDate),
                DeliveryMode: "None",
                SendImmediately: false,
                AllowPartialPayments: false),
            OrderInformation: BuildOrderInformation(draft),
            CustomerInformation: BuildCustomerInformation(draft));

        var response = await Invoke(() => _api.Value.CreateInvoiceAsync(request), "create invoice");
        return MapState(response.Id, response.Status, response.InvoiceInformation, response.OrderInformation, response.CustomerInformation, history: null);
    }

    public async Task<ProviderInvoiceState> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var response = await Invoke(() => _api.Value.GetInvoiceAsync(providerInvoiceId), "get invoice");
        return MapState(response.Id, response.Status, response.InvoiceInformation, response.OrderInformation, response.CustomerInformation, response.InvoiceHistory);
    }

    public async Task<ProviderInvoiceState> UpdateAsync(string providerInvoiceId, ProviderInvoiceDraft draft, CancellationToken cancellationToken = default)
    {
        var request = new UpdateInvoiceRequest(
            InvoiceInformation: new Invoicingv2invoicesidInvoiceInformation(
                Description: draft.Description,
                DueDate: ToProviderDate(draft.DueDate)),
            OrderInformation: BuildOrderInformation(draft),
            CustomerInformation: BuildCustomerInformation(draft));

        var response = await Invoke(() => _api.Value.UpdateInvoiceAsync(providerInvoiceId, request), "update invoice");
        return MapState(response.Id, response.Status, response.InvoiceInformation, response.OrderInformation, response.CustomerInformation, history: null);
    }

    public async Task<ProviderInvoiceState> SendAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var response = await Invoke(() => _api.Value.PerformSendActionAsync(providerInvoiceId), "send invoice");
        return MapState(response.Id, response.Status, response.InvoiceInformation, response.OrderInformation, response.CustomerInformation, history: null);
    }

    public async Task<ProviderInvoiceState> CancelAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var response = await Invoke(() => _api.Value.PerformCancelActionAsync(providerInvoiceId), "cancel invoice");
        return MapState(response.Id, response.Status, response.InvoiceInformation, response.OrderInformation, response.CustomerInformation, history: null);
    }

    public async Task<IReadOnlyList<ProviderInvoiceState>> ListRaisedBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // The provider's list endpoint does not expose a creation date or a date filter,
        // so page the whole account for ids, then read each invoice's detail to obtain the
        // authoritative "raised" timestamp (its earliest history event) and filter by range.
        var ids = new List<string>();
        for (var offset = 0; offset < MaxInvoicesScanned; offset += PageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await Invoke(() => _api.Value.GetAllInvoicesAsync(offset, PageSize, null), "list invoices");
            var invoices = page.Invoices ?? new List<InvoicingV2InvoicesAllGet200ResponseInvoices>();
            ids.AddRange(invoices.Where(i => !string.IsNullOrEmpty(i.Id)).Select(i => i.Id));

            var total = page.TotalInvoices ?? invoices.Count;
            if (invoices.Count < PageSize || offset + PageSize >= total)
            {
                break;
            }
        }

        var details = await FetchDetailsAsync(ids, cancellationToken);

        return details
            .Where(d => d.CreatedDate is { } created && created >= from && created <= to)
            .OrderBy(d => d.CreatedDate)
            .ToList();
    }

    private async Task<IReadOnlyList<ProviderInvoiceState>> FetchDetailsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        var results = new ProviderInvoiceState[ids.Count];
        using var throttle = new SemaphoreSlim(DetailFetchConcurrency);

        var tasks = ids.Select(async (id, index) =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                results[index] = await GetAsync(id, cancellationToken);
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results;
    }

    // ----- request builders -----

    private Invoicingv2invoicesOrderInformation BuildOrderInformation(ProviderInvoiceDraft draft) =>
        new Invoicingv2invoicesOrderInformation(
            AmountDetails: new Invoicingv2invoicesOrderInformationAmountDetails(
                TotalAmount: FormatAmount(draft.TotalAmount),
                Currency: string.IsNullOrWhiteSpace(draft.CurrencyCode) ? AccountCurrency : draft.CurrencyCode),
            LineItems: draft.Lines.Select(l => new Invoicingv2invoicesOrderInformationLineItems(
                ProductSku: l.ProductSku,
                ProductName: l.ProductName,
                Quantity: l.Quantity,
                UnitPrice: FormatAmount(l.UnitPrice))).ToList());

    private static Invoicingv2invoicesCustomerInformation BuildCustomerInformation(ProviderInvoiceDraft draft) =>
        new Invoicingv2invoicesCustomerInformation(Name: draft.CustomerName, Email: draft.CustomerEmail);

    // ----- response mapping -----

    private static ProviderInvoiceState MapState(
        string id,
        string status,
        InvoicingV2InvoicesPost201ResponseInvoiceInformation? invoiceInformation,
        InvoicingV2InvoicesPost201ResponseOrderInformation? orderInformation,
        Invoicingv2invoicesCustomerInformation? customerInformation,
        IEnumerable<InvoicingV2InvoicesGet200ResponseInvoiceHistory>? history)
    {
        var events = (history ?? Enumerable.Empty<InvoicingV2InvoicesGet200ResponseInvoiceHistory>())
            .Select(h => new ProviderInvoiceEvent(
                h.Event ?? "UNKNOWN",
                h.Date.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(h.Date.Value, DateTimeKind.Utc)) : null))
            .ToList();

        DateTimeOffset? createdDate = events.Where(e => e.Date.HasValue).Select(e => e.Date).DefaultIfEmpty(null).Min();

        var amountDetails = orderInformation?.AmountDetails;

        return new ProviderInvoiceState(
            Id: id,
            InvoiceNumber: invoiceInformation?.InvoiceNumber,
            Status: status ?? string.Empty,
            PaymentLink: invoiceInformation?.PaymentLink,
            DueDate: FromProviderDate(invoiceInformation?.DueDate),
            TotalAmount: ParseAmount(amountDetails?.TotalAmount),
            CurrencyCode: amountDetails?.Currency,
            CustomerName: customerInformation?.Name,
            CustomerEmail: customerInformation?.Email,
            Description: invoiceInformation?.Description,
            CreatedDate: createdDate,
            History: events);
    }

    // ----- helpers -----

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? amount) =>
        decimal.TryParse(amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static DateTime ToProviderDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private static DateOnly? FromProviderDate(DateTime? date) => date.HasValue ? DateOnly.FromDateTime(date.Value) : null;

    /// <summary>
    /// Invokes an SDK call and translates a provider failure into a
    /// <see cref="VisaInvoiceProviderException"/> that carries the provider's status and
    /// reason. The exception message never contains any credential value.
    /// </summary>
    private static async Task<T> Invoke<T>(Func<Task<T>> call, string action)
    {
        try
        {
            return await call();
        }
        catch (ApiException ex)
        {
            string? content = ex.ErrorContent?.ToString();
            var (reason, message) = ParseError(content);
            throw new VisaInvoiceProviderException(
                message ?? $"Failed to {action} at the invoicing provider.",
                statusCode: ex.ErrorCode,
                reason: reason,
                innerException: ex);
        }
        catch (Exception ex) when (ex is not VisaInvoiceProviderException && ex is not OperationCanceledException)
        {
            // Connectivity or SDK-level faults (an unreachable base address, a transport
            // error) are provider-side problems, not caller mistakes. Surface a clean
            // message without any provider internals or credentials.
            throw new VisaInvoiceProviderException(
                $"Unable to reach the invoicing provider to {action}.",
                innerException: ex);
        }
    }

    private static (string? Reason, string? Message) ParseError(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            string? reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null;
            string? message = root.TryGetProperty("message", out var m) ? m.GetString() : null;

            if (message is null && root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                message = string.Join("; ", details.EnumerateArray()
                    .Select(d => d.TryGetProperty("reason", out var dr) ? dr.GetString() : null)
                    .Where(s => !string.IsNullOrEmpty(s)));
                if (message.Length == 0)
                {
                    message = null;
                }
            }

            return (reason, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    // ----- SDK client construction -----

    private InvoicesApi BuildApi()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(_settings.BaseUrl)) missing.Add("Visa:BaseUrl");
        if (string.IsNullOrWhiteSpace(_settings.MerchantId)) missing.Add("Visa:MerchantId (VISA_MERCHANT_ID)");
        if (string.IsNullOrWhiteSpace(_settings.KeyId)) missing.Add("Visa:KeyId (VISA_KEY_ID)");
        if (string.IsNullOrWhiteSpace(_settings.SecretKey)) missing.Add("Visa:SecretKey (VISA_SECRET_KEY)");
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "The Visa invoicing integration is not configured. Missing: " + string.Join(", ", missing) + ".");
        }

        if (!Uri.TryCreate(_settings.BaseUrl, UriKind.Absolute, out var baseUri) ||
            !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Visa:BaseUrl must be an absolute https URL. Value was '{_settings.BaseUrl}'.");
        }

        // The SDK builds the request URL as https://{runEnvironment} and signs that host,
        // so route every call through the configured base address by taking its authority
        // (host and, if present, port). No host is hard-coded anywhere.
        var runEnvironment = baseUri.Authority;

        var configuration = new Dictionary<string, string>
        {
            { "authenticationType", "jwt" },
            { "jwtKeyType", "SHARED_SECRET" },
            { "merchantID", _settings.MerchantId! },
            { "merchantKeyId", _settings.KeyId! },
            { "merchantsecretKey", _settings.SecretKey! },
            { "runEnvironment", runEnvironment },
            { "isSDK", "true" }
        };

        var clientConfig = new Configuration(merchConfigDictObj: configuration);
        return new InvoicesApi(clientConfig);
    }
}
