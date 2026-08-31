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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Visa invoicing integration implemented on the CyberSource Invoicing v2 API through the
/// official CyberSource .NET SDK. This is the only type that references the SDK.
///
/// Every call is routed through <see cref="VisaOptions.BaseUrl"/>: its authority is fed to the
/// SDK as the run environment, so no host is hard-coded and the same build can run against a
/// different Visa address. Credentials are read from options (populated from user-secrets /
/// environment) and never logged; the shared secret is never written anywhere.
/// </summary>
public class CyberSourceInvoicingService : IInvoicingService
{
    private const int PageSize = 100;
    private const int MaxPages = 200; // safety bound while paging the provider's feed

    private readonly VisaOptions _options;
    private readonly ILogger<CyberSourceInvoicingService> _logger;

    public CyberSourceInvoicingService(
        IOptions<VisaOptions> options,
        ILogger<CyberSourceInvoicingService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ProviderInvoiceSnapshot> CreateInvoiceAsync(NewInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();

        // sendImmediately=false and no deliveryMode leaves the bill in DRAFT — it is not put to
        // the shopper and no payment link is exposed until it is issued.
        var invoiceInformation = new Invoicingv2invoicesInvoiceInformation(
            TransactionReferenceNumber: request.OrderReference,
            Description: request.Description,
            DueDate: request.DueDate.Date,
            SendImmediately: false);

        var createRequest = new CreateInvoiceRequest(
            CustomerInformation: new Invoicingv2invoicesCustomerInformation(
                Name: request.CustomerName,
                Email: request.CustomerEmail,
                MerchantCustomerId: request.CustomerId),
            InvoiceInformation: invoiceInformation,
            OrderInformation: BuildOrderInformation(request.TotalAmount, request.Currency, request.LineItems));

        InvoicingV2InvoicesPost201Response created;
        try
        {
            created = await api.CreateInvoiceAsync(createRequest);
        }
        catch (ApiException ex)
        {
            throw Translate(ex, "create the invoice");
        }
        catch (Exception ex)
        {
            throw WrapUnexpected(ex, "create the invoice");
        }

        var providerId = created?.Id;
        if (string.IsNullOrEmpty(providerId))
        {
            throw new InvoicingProviderException("The provider did not return an invoice identifier when creating the bill.");
        }

        // Read back the authoritative state so the snapshot is complete and consistent.
        return await GetInvoiceAsync(providerId, cancellationToken);
    }

    public async Task<ProviderInvoiceSnapshot> GetInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        try
        {
            var response = await api.GetInvoiceAsync(providerInvoiceId);
            return MapSnapshot(response);
        }
        catch (ApiException ex)
        {
            throw Translate(ex, "read the invoice");
        }
        catch (Exception ex) when (ex is not InvoicingProviderException)
        {
            throw WrapUnexpected(ex, "read the invoice");
        }
    }

    public async Task<ProviderInvoiceSnapshot> UpdateInvoiceAsync(string providerInvoiceId, InvoiceCorrection correction, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();

        var updateRequest = new UpdateInvoiceRequest(
            CustomerInformation: new Invoicingv2invoicesCustomerInformation(
                Name: correction.CustomerName,
                Email: correction.CustomerEmail,
                MerchantCustomerId: correction.CustomerId),
            InvoiceInformation: new Invoicingv2invoicesidInvoiceInformation(
                TransactionReferenceNumber: correction.OrderReference,
                Description: correction.Description,
                DueDate: correction.DueDate.Date),
            OrderInformation: BuildOrderInformation(correction.TotalAmount, correction.Currency, correction.LineItems));

        try
        {
            await api.UpdateInvoiceAsync(providerInvoiceId, updateRequest);
        }
        catch (ApiException ex)
        {
            throw Translate(ex, "correct the invoice");
        }
        catch (Exception ex)
        {
            throw WrapUnexpected(ex, "correct the invoice");
        }

        return await GetInvoiceAsync(providerInvoiceId, cancellationToken);
    }

    public async Task<ProviderInvoiceSnapshot> SendInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        try
        {
            await api.PerformSendActionAsync(providerInvoiceId);
        }
        catch (ApiException ex)
        {
            throw Translate(ex, "put the invoice to the shopper");
        }
        catch (Exception ex)
        {
            throw WrapUnexpected(ex, "put the invoice to the shopper");
        }

        return await GetInvoiceAsync(providerInvoiceId, cancellationToken);
    }

    public async Task<ProviderInvoiceSnapshot> CancelInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        try
        {
            await api.PerformCancelActionAsync(providerInvoiceId);
        }
        catch (ApiException ex)
        {
            throw Translate(ex, "withdraw the invoice");
        }
        catch (Exception ex)
        {
            throw WrapUnexpected(ex, "withdraw the invoice");
        }

        return await GetInvoiceAsync(providerInvoiceId, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderInvoiceListItem>> ListInvoicesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        var results = new List<ProviderInvoiceListItem>();

        for (var page = 0; page < MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = page * PageSize;

            InvoicingV2InvoicesAllGet200Response response;
            try
            {
                response = await api.GetAllInvoicesAsync(offset, PageSize, null);
            }
            catch (ApiException ex)
            {
                throw Translate(ex, "list invoices for reconciliation");
            }
            catch (Exception ex)
            {
                throw WrapUnexpected(ex, "list invoices for reconciliation");
            }

            var invoices = response?.Invoices;
            if (invoices == null || invoices.Count == 0)
            {
                break;
            }

            foreach (var invoice in invoices)
            {
                var created = ParseDateTimeOffset(invoice.CreatedDate);
                if (created.HasValue && (created.Value < from || created.Value > to))
                {
                    continue; // outside the requested range
                }

                results.Add(new ProviderInvoiceListItem
                {
                    Id = invoice.Id ?? string.Empty,
                    Status = invoice.Status,
                    CreatedDate = created,
                    Amount = ParseDecimal(invoice.OrderInformation?.AmountDetails?.TotalAmount),
                    Currency = invoice.OrderInformation?.AmountDetails?.Currency,
                    CustomerName = invoice.CustomerInformation?.Name,
                    CustomerId = invoice.CustomerInformation?.MerchantCustomerId
                });
            }

            // Stop once we have reached the end of the feed.
            if (invoices.Count < PageSize)
            {
                break;
            }

            if (response.TotalInvoices.HasValue && offset + invoices.Count >= response.TotalInvoices.Value)
            {
                break;
            }
        }

        return results;
    }

    // ---- helpers -------------------------------------------------------------------------

    private Invoicingv2invoicesOrderInformation BuildOrderInformation(
        decimal totalAmount, string currency, IReadOnlyList<ProviderLineItem> lineItems)
    {
        List<Invoicingv2invoicesOrderInformationLineItems>? mappedLineItems = null;
        if (lineItems != null && lineItems.Count > 0)
        {
            mappedLineItems = lineItems
                .Select(li => new Invoicingv2invoicesOrderInformationLineItems(
                    ProductSku: li.Sku,
                    ProductName: li.ProductName,
                    Quantity: li.Quantity,
                    UnitPrice: FormatAmount(li.UnitPrice),
                    TotalAmount: FormatAmount(li.TotalAmount)))
                .ToList();
        }

        return new Invoicingv2invoicesOrderInformation(
            AmountDetails: new Invoicingv2invoicesOrderInformationAmountDetails(
                TotalAmount: FormatAmount(totalAmount),
                Currency: currency),
            LineItems: mappedLineItems);
    }

    private ProviderInvoiceSnapshot MapSnapshot(InvoicingV2InvoicesGet200Response response)
    {
        var history = response.InvoiceHistory?
            .Select(h => new ProviderInvoiceHistoryEvent(h.Event, ToDateTimeOffset(h.Date)))
            .ToList() ?? new List<ProviderInvoiceHistoryEvent>();

        return new ProviderInvoiceSnapshot
        {
            Id = response.Id ?? string.Empty,
            Status = response.Status ?? "UNKNOWN",
            PaymentLink = response.InvoiceInformation?.PaymentLink,
            DueDate = response.InvoiceInformation?.DueDate,
            Amount = ParseDecimal(response.OrderInformation?.AmountDetails?.TotalAmount),
            Currency = response.OrderInformation?.AmountDetails?.Currency,
            CustomerName = response.CustomerInformation?.Name,
            CustomerEmail = response.CustomerInformation?.Email,
            SubmitTimeUtc = ParseDateTimeOffset(response.SubmitTimeUtc),
            History = history
        };
    }

    /// <summary>
    /// Builds a CyberSource client configured to talk to the provider at the authority of
    /// <see cref="VisaOptions.BaseUrl"/>, so every provider call is routed through the configured
    /// base address and no host is hard-coded.
    /// </summary>
    private InvoicesApi CreateApi()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvoicingProviderException("Visa:BaseUrl is not configured; the provider base address is required.");
        }
        if (string.IsNullOrWhiteSpace(_options.MerchantId) ||
            string.IsNullOrWhiteSpace(_options.KeyId) ||
            string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            throw new InvoicingProviderException("Visa credentials are not configured (merchant id, key id and secret key are required).");
        }

        var runEnvironment = ToRunEnvironment(_options.BaseUrl);
        _logger.LogDebug("Routing CyberSource invoicing calls to base address {BaseUrl} (runEnvironment {RunEnvironment}).",
            _options.BaseUrl, runEnvironment);

        var merchantConfig = new Dictionary<string, string>
        {
            { "authenticationType", "http_signature" },
            { "merchantID", _options.MerchantId },
            { "merchantKeyId", _options.KeyId },
            { "merchantsecretKey", _options.SecretKey },
            { "runEnvironment", runEnvironment },
            { "enableClientCert", "false" },
            { "timeout", "300000" }
        };

        // No ILoggerFactory is supplied to the SDK, so the SDK performs no request/response
        // logging. This guarantees the shared secret is never written to any log sink.
        var clientConfig = new Configuration(merchConfigDictObj: merchantConfig);

        return new InvoicesApi(clientConfig);
    }

    /// <summary>
    /// Extracts the host[:port] authority the SDK expects for <c>runEnvironment</c> from the
    /// configured base URL, so the run environment always follows Visa:BaseUrl verbatim.
    /// </summary>
    private static string ToRunEnvironment(string baseUrl)
    {
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        }

        // Fall back to the raw value with any scheme/trailing slash stripped.
        return baseUrl.Replace("https://", string.Empty)
                      .Replace("http://", string.Empty)
                      .TrimEnd('/');
    }

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : (decimal?)null;

    private static DateTimeOffset? ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result)
            ? result
            : (DateTimeOffset?)null;

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value) =>
        value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : (DateTimeOffset?)null;

    /// <summary>
    /// Translates a CyberSource <see cref="ApiException"/> into a provider exception, extracting a
    /// caller-safe message and flagging state-driven refusals (HTTP 400/409/422) distinctly from
    /// genuine integration/transport failures.
    /// </summary>
    private InvoicingProviderException Translate(ApiException ex, string action)
    {
        int status = ex.ErrorCode;
        var isRefusal = status is 400 or 409 or 422;
        // ApiException.ErrorContent is declared as `dynamic`; cast to object so the result is a
        // statically-typed string and downstream logging/interpolation binds normally.
        var detail = ExtractMessage((object?)ex.ErrorContent) ?? ex.Message;

        _logger.LogWarning("CyberSource refused or failed to {Action}. HTTP {Status}: {Detail}", action, status, detail);

        var message = isRefusal
            ? $"The provider would not {action} in the invoice's current state: {detail}"
            : $"The provider failed to {action}: {detail}";

        return new InvoicingProviderException(message, isRefusal, ex);
    }

    /// <summary>
    /// Wraps a non-<see cref="ApiException"/> failure (transport error, unreachable base address,
    /// SDK fault) as a non-refusal provider exception so callers receive a clean gateway error
    /// rather than an unhandled 500. Never includes the shared secret.
    /// </summary>
    private InvoicingProviderException WrapUnexpected(Exception ex, string action)
    {
        _logger.LogError(ex, "CyberSource call to {Action} failed unexpectedly.", action);
        return new InvoicingProviderException(
            $"The provider could not be reached to {action}: {ex.Message}", isRefusal: false, innerException: ex);
    }

    private static string? ExtractMessage(object? errorContent)
    {
        if (errorContent is null)
        {
            return null;
        }

        var raw = errorContent.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            foreach (var name in new[] { "message", "reason", "detail" })
            {
                if (root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                {
                    return prop.GetString();
                }
            }

            // CyberSource often nests reasons under "responseStatus".
            if (root.TryGetProperty("responseStatus", out var responseStatus) &&
                responseStatus.ValueKind == JsonValueKind.Object &&
                responseStatus.TryGetProperty("message", out var nested) &&
                nested.ValueKind == JsonValueKind.String)
            {
                return nested.GetString();
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through to returning a trimmed raw string.
        }

        return raw.Length > 400 ? raw.Substring(0, 400) : raw;
    }
}
