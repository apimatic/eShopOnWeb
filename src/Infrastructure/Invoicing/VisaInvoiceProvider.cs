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
using Microsoft.Extensions.Options;
using CyberSourceConfiguration = CyberSource.Client.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Talks to Visa's CyberSource Invoicing API (the <c>/invoicing/v2/invoices</c> resource) through the
/// official CyberSource .NET SDK. This is the single seam through which the application reaches the
/// provider: every provider call is issued by the one <see cref="InvoicesApi"/> built here, whose host
/// is taken verbatim from <c>Visa:BaseUrl</c>, so no call carries a hard-coded host or bypasses the
/// configured base address.
/// </summary>
public class VisaInvoiceProvider : IInvoiceProvider
{
    // The provider's list endpoint offers no date filter and does not return a creation date, so the
    // account is paged through (newest first) and each invoice is dated from its own history. These cap
    // how far that walk will go on the shared sandbox account.
    private const int PageSize = 100;
    private const int MaxPages = 100;
    private const int MaxDetailLookups = 500;

    private readonly VisaSettings _settings;
    private readonly IAppLogger<VisaInvoiceProvider> _logger;
    private readonly Lazy<InvoicesApi> _api;

    public VisaInvoiceProvider(IOptions<VisaSettings> settings, IAppLogger<VisaInvoiceProvider> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _api = new Lazy<InvoicesApi>(CreateApi, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private InvoicesApi Api => _api.Value;

    public async Task<ProviderInvoiceState> RaiseAsync(InvoiceProviderRequest request, CancellationToken cancellationToken = default)
    {
        var createRequest = new CreateInvoiceRequest(
            CustomerInformation: BuildCustomerInformation(request),
            InvoiceInformation: new Invoicingv2invoicesInvoiceInformation(
                InvoiceNumber: request.InvoiceNumber,
                Description: request.Description,
                DueDate: ToProviderDate(request.DueDate),
                SendImmediately: false),
            OrderInformation: BuildOrderInformation(request));

        var response = await InvokeAsync(() => Api.CreateInvoiceAsync(createRequest), "raise the invoice");
        return BuildState(response.Id, response.Status, response.InvoiceInformation, response.OrderInformation, history: null);
    }

    public async Task<ProviderInvoiceState> UpdateAsync(string providerInvoiceId, InvoiceProviderRequest request, CancellationToken cancellationToken = default)
    {
        var updateRequest = new UpdateInvoiceRequest(
            CustomerInformation: BuildCustomerInformation(request),
            InvoiceInformation: new Invoicingv2invoicesidInvoiceInformation(
                Description: request.Description,
                DueDate: ToProviderDate(request.DueDate)),
            OrderInformation: BuildOrderInformation(request));

        var response = await InvokeAsync(() => Api.UpdateInvoiceAsync(providerInvoiceId, updateRequest), "update the invoice");
        return BuildState(response.Id, response.Status, response.InvoiceInformation, response.OrderInformation, history: null);
    }

    public async Task<ProviderInvoiceState> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(() => Api.GetInvoiceAsync(providerInvoiceId), "read the invoice");
        return BuildState(response.Id, response.Status, response.InvoiceInformation, response.OrderInformation, response.InvoiceHistory);
    }

    public async Task<ProviderInvoiceState> IssueAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        await InvokeAsync(() => Api.PerformSendActionAsync(providerInvoiceId), "issue the invoice");
        // Re-read so the caller gets the definitive state, including the payment link that becomes
        // available once the invoice has been put to the shopper.
        return await GetAsync(providerInvoiceId, cancellationToken);
    }

    public async Task<ProviderInvoiceState> WithdrawAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync(() => Api.PerformCancelActionAsync(providerInvoiceId), "withdraw the invoice");
        return BuildState(response.Id, response.Status, response.InvoiceInformation, response.OrderInformation, history: null);
    }

    public async Task<IReadOnlyList<ProviderInvoiceSummary>> ListRaisedBetweenAsync(DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken cancellationToken = default)
    {
        var summaries = new List<ProviderInvoiceSummary>();
        var total = int.MaxValue;
        var lookups = 0;

        // The list returns invoices newest-first but carries no creation date, so each invoice is dated
        // from its own history. Because the order is newest-first, once an invoice older than the range
        // start is reached, every remaining invoice is older too and the walk can stop.
        for (var page = 0; page < MaxPages; page++)
        {
            var offset = page * PageSize;
            if (offset >= total)
            {
                break;
            }

            var response = await InvokeAsync(() => Api.GetAllInvoicesAsync(offset, PageSize), "list invoices for reconciliation");
            total = response.TotalInvoices ?? 0;
            var invoices = response.Invoices ?? new List<InvoicingV2InvoicesAllGet200ResponseInvoices>();
            if (invoices.Count == 0)
            {
                break;
            }

            foreach (var invoice in invoices)
            {
                if (lookups >= MaxDetailLookups)
                {
                    return summaries;
                }

                var createdDate = await GetCreationDateAsync(invoice.Id);
                lookups++;

                if (createdDate.HasValue && createdDate.Value < fromInclusive)
                {
                    // Newest-first ordering: nothing after this can be in range.
                    return summaries;
                }

                if (!createdDate.HasValue || createdDate.Value > toInclusive)
                {
                    continue;
                }

                summaries.Add(new ProviderInvoiceSummary(
                    invoice.Id,
                    InvoiceNumber: null, // the list endpoint does not return the invoice number
                    CustomerReference: invoice.CustomerInformation?.MerchantCustomerId,
                    invoice.Status ?? "UNKNOWN",
                    createdDate,
                    ParseAmount(invoice.OrderInformation?.AmountDetails?.TotalAmount),
                    invoice.OrderInformation?.AmountDetails?.Currency,
                    invoice.CustomerInformation?.Name));
            }

            if (invoices.Count < PageSize)
            {
                break;
            }
        }

        return summaries;
    }

    /// <summary>
    /// The provider does not return a creation date in either the list or the invoice detail, but the
    /// invoice history records a dated event for every state it has been through. The earliest of those
    /// events is when the bill was raised.
    /// </summary>
    private async Task<DateTimeOffset?> GetCreationDateAsync(string providerInvoiceId)
    {
        var response = await InvokeAsync(() => Api.GetInvoiceAsync(providerInvoiceId), "date the invoice for reconciliation");
        var earliest = response.InvoiceHistory?
            .Where(h => h.Date.HasValue)
            .Select(h => h.Date!.Value)
            .DefaultIfEmpty()
            .Min();

        return earliest is { } value && value != default
            ? new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero)
            : null;
    }

    private InvoicesApi CreateApi()
    {
        if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            throw new InvalidOperationException("Visa:BaseUrl must be configured for the invoicing integration.");
        }

        if (string.IsNullOrWhiteSpace(_settings.MerchantId) ||
            string.IsNullOrWhiteSpace(_settings.KeyId) ||
            string.IsNullOrWhiteSpace(_settings.SecretKey))
        {
            throw new InvalidOperationException(
                "Visa credentials (MerchantId, KeyId, SecretKey) must be configured for the invoicing integration.");
        }

        var runEnvironment = ToRunEnvironment(_settings.BaseUrl);

        var configDictionary = new Dictionary<string, string>
        {
            { "authenticationType", "jwt" },
            { "jwtKeyType", "SHARED_SECRET" },
            { "merchantID", _settings.MerchantId },
            { "merchantKeyId", _settings.KeyId },
            { "merchantsecretKey", _settings.SecretKey },
            { "runEnvironment", runEnvironment },
            { "isSDK", "true" }
        };

        // Log the host we will route through (never any credential) so the configured base address is
        // visible in the logs.
        _logger.LogInformation("Visa invoicing integration will route every call through host '{0}'.", runEnvironment);

        var clientConfiguration = new CyberSourceConfiguration(merchConfigDictObj: configDictionary);
        return new InvoicesApi(clientConfiguration);
    }

    /// <summary>
    /// Turns the configured base URL into the host the SDK routes through. The value is used verbatim as
    /// the base address in place of any SDK default; only the scheme is normalised away because the SDK
    /// keys off the host (it always uses HTTPS to CyberSource).
    /// </summary>
    private static string ToRunEnvironment(string baseUrl)
    {
        var trimmed = baseUrl.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return uri.Authority;
        }

        // Already a bare host (optionally with a path); strip any scheme separator defensively.
        return trimmed.Replace("https://", string.Empty).Replace("http://", string.Empty).TrimEnd('/');
    }

    private static Invoicingv2invoicesCustomerInformation BuildCustomerInformation(InvoiceProviderRequest request) =>
        new Invoicingv2invoicesCustomerInformation(
            Name: request.CustomerName,
            Email: request.CustomerEmail,
            MerchantCustomerId: request.CustomerReference);

    private static Invoicingv2invoicesOrderInformation BuildOrderInformation(InvoiceProviderRequest request) =>
        new Invoicingv2invoicesOrderInformation(
            AmountDetails: new Invoicingv2invoicesOrderInformationAmountDetails(
                TotalAmount: Money(request.TotalAmount),
                Currency: request.Currency),
            LineItems: request.LineItems
                .Select(line => new Invoicingv2invoicesOrderInformationLineItems(
                    ProductName: line.ProductName,
                    ProductSku: line.Sku,
                    Quantity: line.Quantity,
                    UnitPrice: Money(line.UnitPrice),
                    TotalAmount: Money(line.UnitPrice * line.Quantity)))
                .ToList());

    private static ProviderInvoiceState BuildState(
        string id,
        string status,
        InvoicingV2InvoicesPost201ResponseInvoiceInformation? invoiceInformation,
        InvoicingV2InvoicesPost201ResponseOrderInformation? orderInformation,
        List<InvoicingV2InvoicesGet200ResponseInvoiceHistory>? history)
    {
        var historyLines = history?
            .Select(h => h.Date.HasValue
                ? $"{h.Date.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ} {h.Event}"
                : (h.Event ?? string.Empty))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList() ?? new List<string>();

        DateOnly? dueDate = invoiceInformation?.DueDate.HasValue == true
            ? DateOnly.FromDateTime(invoiceInformation.DueDate!.Value)
            : null;

        return new ProviderInvoiceState(
            id,
            status ?? "UNKNOWN",
            invoiceInformation?.PaymentLink,
            invoiceInformation?.InvoiceNumber,
            ParseAmount(orderInformation?.AmountDetails?.TotalAmount),
            orderInformation?.AmountDetails?.Currency,
            dueDate,
            historyLines);
    }

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static DateTime ToProviderDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private static decimal? ParseAmount(string? amount) =>
        decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    private async Task<T> InvokeAsync<T>(Func<Task<T>> call, string action)
    {
        try
        {
            return await call();
        }
        catch (CyberSource.Client.ApiException ex)
        {
            // The provider reported a problem. Surface its status and reason (never any secret) so the
            // API layer can distinguish a state refusal from a genuine provider fault.
            var detail = ex.ErrorContent?.ToString();
            var message = string.IsNullOrWhiteSpace(detail)
                ? $"The payment provider refused to {action}."
                : $"The payment provider refused to {action}: {detail}";
            _logger.LogWarning("Visa invoicing call failed to {0} (status {1}).", action, ex.ErrorCode);
            throw new InvoiceProviderException(message, ex.ErrorCode, ex);
        }
    }
}
