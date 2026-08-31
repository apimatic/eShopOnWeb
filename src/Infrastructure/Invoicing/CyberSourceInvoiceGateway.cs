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
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// The Visa invoicing gateway, implemented over the CyberSource .NET SDK's <see cref="InvoicesApi"/>.
/// This is the only place the CyberSource SDK is used; every provider call is routed through the
/// configured <see cref="VisaInvoicingOptions.BaseUrl"/>. Authentication uses JWT with a shared secret;
/// the secret is passed to the SDK only and is never logged or surfaced.
/// </summary>
public class CyberSourceInvoiceGateway : IInvoiceGateway
{
    // The provider's invoice lifecycle statuses used to decide whether a refusal is a state issue.
    private const int InvoicePageSize = 100;
    private const int MaxInvoicePages = 100; // safety cap when paging a shared sandbox account
    private const int ProviderDetailConcurrency = 6; // bound on concurrent GetInvoice detail lookups

    private readonly VisaInvoicingOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<CyberSourceInvoiceGateway> _logger;

    public CyberSourceInvoiceGateway(
        VisaInvoicingOptions options,
        ILoggerFactory loggerFactory,
        ILogger<CyberSourceInvoiceGateway> logger)
    {
        _options = options;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public Task<ProviderInvoice> CreateInvoiceAsync(InvoiceDraft draft, CancellationToken cancellationToken = default)
    {
        var request = new CreateInvoiceRequest
        {
            CustomerInformation = new Invoicingv2invoicesCustomerInformation
            {
                Name = draft.CustomerName,
                Email = draft.CustomerEmail
            },
            InvoiceInformation = new Invoicingv2invoicesInvoiceInformation
            {
                Description = draft.Description,
                DueDate = draft.DueDate.Date,
                // Leave the bill in draft mode so it starts out not yet put to the shopper.
                SendImmediately = false
            },
            OrderInformation = new Invoicingv2invoicesOrderInformation
            {
                AmountDetails = new Invoicingv2invoicesOrderInformationAmountDetails
                {
                    TotalAmount = Money(draft.TotalAmount),
                    Currency = draft.Currency
                },
                LineItems = draft.Lines.Select(ToLineItem).ToList()
            }
        };

        return ExecuteAsync(
            async () =>
            {
                var api = CreateApi();
                var response = await api.CreateInvoiceAsync(request);
                _logger.LogInformation("Raised invoice {InvoiceId} (status {Status}) for order-derived draft.",
                    response.Id, response.Status);
                return new ProviderInvoice(
                    Id: response.Id,
                    Status: response.Status,
                    PaymentLink: response.InvoiceInformation?.PaymentLink,
                    CreatedDate: null,
                    CustomerName: response.CustomerInformation?.Name,
                    CustomerEmail: response.CustomerInformation?.Email,
                    Amount: null,
                    Currency: null,
                    History: Array.Empty<ProviderInvoiceEvent>());
            },
            operation: "create invoice",
            treat4xxAsStateRefusal: false);
    }

    public Task<ProviderInvoice> GetInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
        => ExecuteAsync(
            async () =>
            {
                var api = CreateApi();
                var response = await api.GetInvoiceAsync(providerInvoiceId);
                return MapGetResponse(response);
            },
            operation: "get invoice",
            treat4xxAsStateRefusal: false);

    public Task<ProviderInvoice> UpdateInvoiceAsync(string providerInvoiceId, InvoiceAmendment amendment, CancellationToken cancellationToken = default)
    {
        var request = new UpdateInvoiceRequest
        {
            CustomerInformation = new Invoicingv2invoicesCustomerInformation
            {
                Name = amendment.CustomerName,
                Email = amendment.CustomerEmail
            },
            InvoiceInformation = new Invoicingv2invoicesidInvoiceInformation
            {
                Description = amendment.Description,
                DueDate = amendment.DueDate.Date
            },
            OrderInformation = new Invoicingv2invoicesOrderInformation
            {
                AmountDetails = new Invoicingv2invoicesOrderInformationAmountDetails
                {
                    TotalAmount = Money(amendment.TotalAmount),
                    Currency = amendment.Currency
                },
                LineItems = amendment.Lines.Select(ToLineItem).ToList()
            }
        };

        return ExecuteAsync(
            async () =>
            {
                var api = CreateApi();
                await api.UpdateInvoiceAsync(providerInvoiceId, request);
                // Re-read to return the authoritative post-update state, history and payment link.
                var refreshed = await api.GetInvoiceAsync(providerInvoiceId);
                return MapGetResponse(refreshed);
            },
            operation: "update invoice",
            treat4xxAsStateRefusal: true);
    }

    public Task<ProviderInvoice> IssueInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
        => ExecuteAsync(
            async () =>
            {
                var api = CreateApi();
                // Put the bill to the shopper by delivering (sending) it.
                await api.PerformSendActionAsync(providerInvoiceId);
                var refreshed = await api.GetInvoiceAsync(providerInvoiceId);
                _logger.LogInformation("Issued invoice {InvoiceId} (status {Status}).", refreshed.Id, refreshed.Status);
                return MapGetResponse(refreshed);
            },
            operation: "issue invoice",
            treat4xxAsStateRefusal: true);

    public Task<ProviderInvoice> WithdrawInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
        => ExecuteAsync(
            async () =>
            {
                var api = CreateApi();
                await api.PerformCancelActionAsync(providerInvoiceId);
                var refreshed = await api.GetInvoiceAsync(providerInvoiceId);
                _logger.LogInformation("Withdrew invoice {InvoiceId} (status {Status}).", refreshed.Id, refreshed.Status);
                return MapGetResponse(refreshed);
            },
            operation: "withdraw invoice",
            treat4xxAsStateRefusal: true);

    public Task<IReadOnlyList<ProviderInvoiceSummary>> ListInvoicesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        => ExecuteAsync<IReadOnlyList<ProviderInvoiceSummary>>(
            async () =>
            {
                var api = CreateApi();

                // Page the provider's full list. The list items carry id/status/customer/amount but no
                // creation timestamp, and the endpoint has no date filter — so we collect them all first.
                var listed = new List<InvoicingV2InvoicesAllGet200ResponseInvoices>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var page = 0; page < MaxInvoicePages; page++)
                {
                    var offset = page * InvoicePageSize;
                    var response = await api.GetAllInvoicesAsync(offset, InvoicePageSize, null!);
                    var invoices = response?.Invoices;
                    if (invoices is null || invoices.Count == 0)
                    {
                        break;
                    }

                    foreach (var invoice in invoices)
                    {
                        if (invoice.Id is not null && seen.Add(invoice.Id))
                        {
                            listed.Add(invoice);
                        }
                    }

                    var total = response!.TotalInvoices;
                    if (invoices.Count < InvoicePageSize || (total.HasValue && offset + invoices.Count >= total.Value))
                    {
                        break;
                    }
                }

                // The invoice's creation moment is the date of its earliest history event, obtained per
                // invoice via GetInvoice. Resolve those with bounded concurrency, then bound by range.
                using var throttle = new SemaphoreSlim(ProviderDetailConcurrency);
                var tasks = listed.Select(async invoice =>
                {
                    await throttle.WaitAsync();
                    try
                    {
                        var created = await ResolveCreatedDateAsync(invoice.Id!);
                        return (invoice, created);
                    }
                    finally
                    {
                        throttle.Release();
                    }
                });
                var enriched = await Task.WhenAll(tasks);

                var results = new List<ProviderInvoiceSummary>();
                foreach (var (invoice, created) in enriched)
                {
                    // Cover the whole range: include when the created date is in range, or unknown.
                    if (created is not null && (created < from || created > to))
                    {
                        continue;
                    }

                    results.Add(new ProviderInvoiceSummary(
                        Id: invoice.Id!,
                        Status: invoice.Status ?? string.Empty,
                        CreatedDate: created,
                        CustomerName: invoice.CustomerInformation?.Name,
                        Amount: ParseAmount(invoice.OrderInformation?.AmountDetails?.TotalAmount),
                        Currency: invoice.OrderInformation?.AmountDetails?.Currency));
                }

                return results;
            },
            operation: "list invoices",
            treat4xxAsStateRefusal: false);

    /// <summary>
    /// The provider's invoice list carries no creation timestamp, so the creation moment is taken as the
    /// earliest event date in the invoice's history (e.g. the DRAFT event). Returns null if it cannot be
    /// determined, in which case the caller keeps the invoice so the range is fully covered.
    /// </summary>
    private async Task<DateTimeOffset?> ResolveCreatedDateAsync(string providerInvoiceId)
    {
        try
        {
            var api = CreateApi();
            var detail = await api.GetInvoiceAsync(providerInvoiceId);
            var dates = detail.InvoiceHistory?
                .Select(h => ToOffset(h.Date))
                .Where(d => d is not null)
                .Select(d => d!.Value)
                .ToList();
            if (dates is { Count: > 0 })
            {
                return dates.Min();
            }
        }
        catch (ApiException)
        {
            // Cannot read this invoice's history — leave the creation date unknown.
        }
        return null;
    }

    /// <summary>
    /// Builds a CyberSource API client bound to the configured base address. The base address is taken
    /// verbatim from <see cref="VisaInvoicingOptions.BaseUrl"/> (as the SDK's run environment host), so no
    /// provider call carries a hard-coded host.
    /// </summary>
    private InvoicesApi CreateApi()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvoicingProviderException("Visa:BaseUrl is not configured; the provider base address must come from configuration.");
        }
        if (string.IsNullOrWhiteSpace(_options.MerchantId) ||
            string.IsNullOrWhiteSpace(_options.KeyId) ||
            string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            throw new InvoicingProviderException("Visa merchant credentials are not configured.");
        }

        var merchantConfig = new Dictionary<string, string>
        {
            { "authenticationType", "jwt" },
            { "jwtKeyType", "SHARED_SECRET" },
            { "merchantID", _options.MerchantId },
            { "merchantKeyId", _options.KeyId },
            { "merchantsecretKey", _options.SecretKey },
            { "runEnvironment", ResolveRunEnvironment(_options.BaseUrl) },
            { "isSDK", "true" }
        };

        var clientConfiguration = new Configuration(
            merchConfigDictObj: merchantConfig,
            loggerFactory: _loggerFactory);

        return new InvoicesApi(clientConfiguration);
    }

    /// <summary>
    /// Resolves the SDK run-environment host from the configured base URL. The SDK's base address is
    /// "https://" + runEnvironment, so the URL's authority (host and, if present, port) is used verbatim.
    /// </summary>
    internal static string ResolveRunEnvironment(string baseUrl)
    {
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return uri.Authority;
        }
        // Already a bare host (no scheme) — use as-is.
        return baseUrl.Trim().TrimEnd('/');
    }

    private static ProviderInvoice MapGetResponse(InvoicingV2InvoicesGet200Response response)
    {
        var history = response.InvoiceHistory?
            .Select(h => new ProviderInvoiceEvent(h.Event, ToOffset(h.Date)))
            .ToList() ?? new List<ProviderInvoiceEvent>();

        return new ProviderInvoice(
            Id: response.Id,
            Status: response.Status ?? string.Empty,
            PaymentLink: response.InvoiceInformation?.PaymentLink,
            CreatedDate: null,
            CustomerName: response.CustomerInformation?.Name,
            CustomerEmail: response.CustomerInformation?.Email,
            Amount: ParseAmount(response.OrderInformation?.AmountDetails?.TotalAmount),
            Currency: response.OrderInformation?.AmountDetails?.Currency,
            History: history);
    }

    private static Invoicingv2invoicesOrderInformationLineItems ToLineItem(InvoiceLine line) =>
        new()
        {
            ProductName = line.ProductName,
            ProductSku = line.Sku,
            Quantity = line.Quantity,
            UnitPrice = Money(line.UnitPrice)
        };

    private async Task<T> ExecuteAsync<T>(Func<Task<T>> action, string operation, bool treat4xxAsStateRefusal)
    {
        try
        {
            return await action();
        }
        catch (ApiException ex)
        {
            if (treat4xxAsStateRefusal && ex.ErrorCode >= 400 && ex.ErrorCode < 500)
            {
                // A legitimate refusal of the transition given the state the bill is in.
                throw new InvoiceStateException(
                    $"The provider refused to {operation}: {ExtractProviderMessage(ex)}");
            }

            throw new InvoicingProviderException(
                $"The provider could not {operation} (HTTP {ex.ErrorCode}): {ExtractProviderMessage(ex)}", ex);
        }
    }

    /// <summary>
    /// Extracts a concise, safe message from a provider error. Only the provider's own error text is
    /// surfaced; request payloads and credentials are never included.
    /// </summary>
    private static string ExtractProviderMessage(ApiException ex)
    {
        var content = ex.ErrorContent?.ToString();
        if (string.IsNullOrWhiteSpace(content))
        {
            return "no additional detail";
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString()!;
            }
            if (root.TryGetProperty("reason", out JsonElement reason) && reason.ValueKind == JsonValueKind.String)
            {
                return reason.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through to a length-capped raw message.
        }

        return content.Length > 300 ? content[..300] : content;
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null;
}
