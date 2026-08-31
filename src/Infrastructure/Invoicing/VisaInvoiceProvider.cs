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
/// Talks to Visa's invoicing product through the CyberSource REST SDK. Every call is routed through
/// the configured <c>Visa:BaseUrl</c> (used verbatim as the base address) and authenticated with the
/// JWT shared-secret credentials loaded from user-secrets. Provider SDK types never escape this class.
/// </summary>
public class VisaInvoiceProvider : IInvoiceProvider
{
    private readonly VisaSettings _settings;
    private readonly IAppLogger<VisaInvoiceProvider> _logger;
    private readonly IReadOnlyDictionary<string, string> _merchantConfig;

    public VisaInvoiceProvider(IOptions<VisaSettings> options, IAppLogger<VisaInvoiceProvider> logger)
    {
        _settings = options.Value;
        _logger = logger;
        _merchantConfig = BuildMerchantConfig(_settings);
    }

    public async Task<ProviderInvoice> CreateInvoiceAsync(CreateProviderInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new CreateInvoiceRequest
        {
            InvoiceInformation = new Invoicingv2invoicesInvoiceInformation
            {
                InvoiceNumber = request.InvoiceNumber,
                TransactionReferenceNumber = request.InvoiceNumber,
                Description = request.Description,
                DueDate = ToProviderDate(request.DueDate),
                // The bill starts out not yet put to the shopper: created in draft, no email dispatched.
                SendImmediately = false
            },
            OrderInformation = BuildOrderInformation(request.TotalAmount, request.Currency, request.LineItems),
            CustomerInformation = new Invoicingv2invoicesCustomerInformation
            {
                Name = request.CustomerName,
                Email = request.CustomerEmail
            }
        };

        return await ExecuteAsync(
            "create invoice",
            api => api.CreateInvoiceAsync(payload),
            MapCreateResponse);
    }

    public async Task<ProviderInvoice> GetInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(
            "get invoice",
            api => api.GetInvoiceAsync(providerInvoiceId),
            MapGetResponse);
    }

    public async Task<ProviderInvoice> UpdateInvoiceAsync(string providerInvoiceId, UpdateProviderInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new UpdateInvoiceRequest
        {
            InvoiceInformation = new Invoicingv2invoicesidInvoiceInformation
            {
                TransactionReferenceNumber = providerInvoiceId,
                Description = request.Description,
                DueDate = ToProviderDate(request.DueDate)
            },
            OrderInformation = BuildOrderInformation(request.TotalAmount, request.Currency, request.LineItems),
            CustomerInformation = new Invoicingv2invoicesCustomerInformation
            {
                Name = request.CustomerName,
                Email = request.CustomerEmail
            }
        };

        return await ExecuteAsync(
            "update invoice",
            api => api.UpdateInvoiceAsync(providerInvoiceId, payload),
            MapUpdateResponse);
    }

    public async Task<ProviderInvoice> IssueInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(
            "issue invoice",
            api => api.PerformSendActionAsync(providerInvoiceId),
            MapSendResponse);
    }

    public async Task<ProviderInvoice> WithdrawInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(
            "withdraw invoice",
            api => api.PerformCancelActionAsync(providerInvoiceId),
            MapCancelResponse);
    }

    public async Task<IReadOnlyList<ProviderInvoiceSummary>> ListInvoicesCreatedBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // The provider's list is not filterable by date and its entries carry no timestamp, so gather
        // every invoice id, then resolve each one's creation date from its history to filter the range.
        var ids = await CollectAllInvoiceIdsAsync(cancellationToken);

        var pageSize = Math.Max(1, _settings.ReconciliationConcurrency);
        var results = new List<ProviderInvoiceSummary>();

        using var throttle = new SemaphoreSlim(pageSize);
        var tasks = ids.Select(async id =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var invoice = await GetInvoiceAsync(id, cancellationToken);
                var createdDate = invoice.CreatedDate;
                if (createdDate is null || createdDate < from || createdDate > to)
                {
                    return (ProviderInvoiceSummary?)null;
                }

                return new ProviderInvoiceSummary
                {
                    Id = invoice.Id,
                    Status = invoice.Status,
                    CreatedDate = createdDate,
                    TotalAmount = invoice.TotalAmount,
                    Currency = invoice.Currency,
                    CustomerName = invoice.CustomerName,
                    DueDate = invoice.DueDate
                };
            }
            finally
            {
                throttle.Release();
            }
        });

        foreach (var summary in await Task.WhenAll(tasks))
        {
            if (summary is not null)
            {
                results.Add(summary);
            }
        }

        return results
            .OrderByDescending(s => s.CreatedDate)
            .ToList();
    }

    private async Task<List<string>> CollectAllInvoiceIdsAsync(CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        var offset = 0;
        var pageSize = Math.Max(1, _settings.ListPageSize);
        var cap = _settings.ReconciliationMaxInvoices;

        while (true)
        {
            var page = await ExecuteAsync(
                "list invoices",
                api => api.GetAllInvoicesAsync(offset, pageSize, null),
                response => response);

            var invoices = page.Invoices;
            if (invoices is null || invoices.Count == 0)
            {
                break;
            }

            foreach (var invoice in invoices)
            {
                if (!string.IsNullOrEmpty(invoice.Id))
                {
                    ids.Add(invoice.Id);
                }
            }

            if (cap > 0 && ids.Count >= cap)
            {
                _logger.LogWarning("Reconciliation reached the scan cap of {Cap} provider invoices; older invoices were not scanned.", cap);
                return ids.Take(cap).ToList();
            }

            var total = page.TotalInvoices ?? 0;
            offset += pageSize;
            if (offset >= total || invoices.Count < pageSize)
            {
                break;
            }
        }

        return ids;
    }

    /// <summary>Runs one SDK call on a fresh api instance and maps the result, translating failures.</summary>
    private async Task<TResult> ExecuteAsync<TResponse, TResult>(
        string operation,
        Func<InvoicesApi, Task<TResponse>> call,
        Func<TResponse, TResult> map)
    {
        try
        {
            var api = new InvoicesApi(new Configuration(merchConfigDictObj: _merchantConfig));
            var response = await call(api);
            return map(response);
        }
        catch (ApiException ex)
        {
            throw TranslateProviderError(operation, ex);
        }
        catch (InvoiceProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never surface raw exception detail that could carry configuration; keep it generic.
            _logger.LogWarning("Visa {Operation} failed unexpectedly: {ExceptionType}", operation, ex.GetType().Name);
            throw new InvoiceProviderException($"The invoicing provider could not {operation}.", 502, innerException: ex);
        }
    }

    private InvoiceProviderException TranslateProviderError(string operation, ApiException ex)
    {
        var (reason, providerStatus) = ParseProviderError(ex);
        _logger.LogWarning("Visa {Operation} refused with status {Status}: {Reason}", operation, providerStatus, reason);

        // A 4xx is the provider legitimately refusing given the state (or a bad request); surface it as
        // that status. Anything else is treated as an upstream failure.
        var suggested = providerStatus is >= 400 and < 500 ? providerStatus.Value : 502;
        var message = providerStatus is >= 400 and < 500
            ? $"The invoicing provider refused to {operation}: {reason}"
            : $"The invoicing provider could not {operation}.";

        return new InvoiceProviderException(message, suggested, providerStatus, ex);
    }

    private static (string Reason, int? Status) ParseProviderError(ApiException ex)
    {
        var status = ex.ErrorCode == 0 ? (int?)null : ex.ErrorCode;
        var body = ex.ErrorContent?.ToString();
        if (string.IsNullOrWhiteSpace(body))
        {
            return ("no additional detail", status);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var parts = new List<string>();

            if (root.TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.String)
            {
                parts.Add(message.GetString()!);
            }

            if (root.TryGetProperty("reason", out JsonElement reason) && reason.ValueKind == JsonValueKind.String)
            {
                parts.Add($"({reason.GetString()})");
            }

            if (root.TryGetProperty("details", out JsonElement details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    var field = detail.TryGetProperty("field", out JsonElement f) ? f.GetString() : null;
                    var reasonText = detail.TryGetProperty("reason", out JsonElement r) ? r.GetString() : null;
                    if (field is not null || reasonText is not null)
                    {
                        parts.Add($"[{field}: {reasonText}]");
                    }
                }
            }

            var summary = parts.Count > 0 ? string.Join(" ", parts) : "no additional detail";
            return (Truncate(summary, 500), status);
        }
        catch (JsonException)
        {
            return ("provider returned an unparseable error", status);
        }
    }

    // ----- Response mapping -------------------------------------------------------------------

    private static ProviderInvoice MapCreateResponse(InvoicingV2InvoicesPost201Response r) =>
        new()
        {
            Id = r.Id ?? string.Empty,
            Status = r.Status ?? string.Empty,
            PaymentLink = r.InvoiceInformation?.PaymentLink,
            DueDate = FromProviderDate(r.InvoiceInformation?.DueDate),
            CreatedDate = ParseTimestamp(r.SubmitTimeUtc),
            TotalAmount = ParseAmount(r.OrderInformation?.AmountDetails?.TotalAmount),
            Currency = r.OrderInformation?.AmountDetails?.Currency,
            CustomerName = r.CustomerInformation?.Name,
            CustomerEmail = r.CustomerInformation?.Email
        };

    private static ProviderInvoice MapUpdateResponse(InvoicingV2InvoicesPut200Response r) =>
        new()
        {
            Id = r.Id ?? string.Empty,
            Status = r.Status ?? string.Empty,
            PaymentLink = r.InvoiceInformation?.PaymentLink,
            DueDate = FromProviderDate(r.InvoiceInformation?.DueDate),
            CreatedDate = ParseTimestamp(r.SubmitTimeUtc),
            TotalAmount = ParseAmount(r.OrderInformation?.AmountDetails?.TotalAmount),
            Currency = r.OrderInformation?.AmountDetails?.Currency,
            CustomerName = r.CustomerInformation?.Name,
            CustomerEmail = r.CustomerInformation?.Email
        };

    private static ProviderInvoice MapSendResponse(InvoicingV2InvoicesSend200Response r) =>
        new()
        {
            Id = r.Id ?? string.Empty,
            Status = r.Status ?? string.Empty,
            PaymentLink = r.InvoiceInformation?.PaymentLink,
            DueDate = FromProviderDate(r.InvoiceInformation?.DueDate),
            CreatedDate = ParseTimestamp(r.SubmitTimeUtc),
            TotalAmount = ParseAmount(r.OrderInformation?.AmountDetails?.TotalAmount),
            Currency = r.OrderInformation?.AmountDetails?.Currency,
            CustomerName = r.CustomerInformation?.Name,
            CustomerEmail = r.CustomerInformation?.Email
        };

    private static ProviderInvoice MapCancelResponse(InvoicingV2InvoicesCancel200Response r) =>
        new()
        {
            Id = r.Id ?? string.Empty,
            Status = r.Status ?? string.Empty,
            PaymentLink = null,
            DueDate = FromProviderDate(r.InvoiceInformation?.DueDate),
            CreatedDate = ParseTimestamp(r.SubmitTimeUtc),
            TotalAmount = ParseAmount(r.OrderInformation?.AmountDetails?.TotalAmount),
            Currency = r.OrderInformation?.AmountDetails?.Currency,
            CustomerName = r.CustomerInformation?.Name,
            CustomerEmail = r.CustomerInformation?.Email
        };

    private static ProviderInvoice MapGetResponse(InvoicingV2InvoicesGet200Response r)
    {
        var history = (r.InvoiceHistory ?? new List<InvoicingV2InvoicesGet200ResponseInvoiceHistory>())
            .Select(h => new ProviderInvoiceEvent(h.Event ?? string.Empty, ToUtcOffset(h.Date)))
            .ToList();

        // The creation time is the earliest recorded history event (submitTimeUtc reflects the read).
        var createdDate = history
            .Where(h => h.Date is not null)
            .Select(h => h.Date)
            .DefaultIfEmpty(ParseTimestamp(r.SubmitTimeUtc))
            .Min();

        return new ProviderInvoice
        {
            Id = r.Id ?? string.Empty,
            Status = r.Status ?? string.Empty,
            PaymentLink = r.InvoiceInformation?.PaymentLink,
            DueDate = FromProviderDate(r.InvoiceInformation?.DueDate),
            CreatedDate = createdDate,
            TotalAmount = ParseAmount(r.OrderInformation?.AmountDetails?.TotalAmount),
            Currency = r.OrderInformation?.AmountDetails?.Currency,
            CustomerName = r.CustomerInformation?.Name,
            CustomerEmail = r.CustomerInformation?.Email,
            History = history
        };
    }

    // ----- Building request payloads ----------------------------------------------------------

    private static Invoicingv2invoicesOrderInformation BuildOrderInformation(decimal totalAmount, string currency, IReadOnlyList<ProviderLineItem> lineItems)
    {
        return new Invoicingv2invoicesOrderInformation
        {
            AmountDetails = new Invoicingv2invoicesOrderInformationAmountDetails
            {
                TotalAmount = FormatAmount(totalAmount),
                Currency = currency
            },
            LineItems = lineItems.Select(item => new Invoicingv2invoicesOrderInformationLineItems
            {
                ProductSku = item.ProductSku,
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                UnitPrice = FormatAmount(item.UnitPrice)
            }).ToList()
        };
    }

    private static IReadOnlyDictionary<string, string> BuildMerchantConfig(VisaSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvoiceProviderException("Visa:BaseUrl is not configured.", 500);
        }

        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvoiceProviderException("Visa:BaseUrl must be an absolute URL.", 500);
        }

        if (string.IsNullOrWhiteSpace(settings.MerchantId) ||
            string.IsNullOrWhiteSpace(settings.KeyId) ||
            string.IsNullOrWhiteSpace(settings.SecretKey))
        {
            throw new InvoiceProviderException("Visa credentials (MerchantId/KeyId/SecretKey) are not configured.", 500);
        }

        // The SDK connects to https://<runEnvironment>; deriving it from the configured base address
        // means every call is routed through Visa:BaseUrl with no host hard-coded anywhere.
        return new Dictionary<string, string>
        {
            ["merchantID"] = settings.MerchantId,
            ["merchantKeyId"] = settings.KeyId,
            ["merchantsecretKey"] = settings.SecretKey,
            ["authenticationType"] = "jwt",
            ["jwtKeyType"] = "SHARED_SECRET",
            ["runEnvironment"] = baseUri.Authority,
            ["isSDK"] = "true",
            ["timeout"] = settings.RequestTimeoutMs.ToString(CultureInfo.InvariantCulture)
        };
    }

    // ----- Conversion helpers -----------------------------------------------------------------

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? amount) =>
        decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static DateTime ToProviderDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private static DateOnly? FromProviderDate(DateTime? date) =>
        date.HasValue ? DateOnly.FromDateTime(date.Value) : null;

    private static DateTimeOffset? ToUtcOffset(DateTime? date)
    {
        if (!date.HasValue)
        {
            return null;
        }

        var value = date.Value;
        return value.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(value),
            DateTimeKind.Local => new DateTimeOffset(value).ToUniversalTime(),
            _ => new DateTimeOffset(value, TimeSpan.Zero)
        };
    }

    private static DateTimeOffset? ParseTimestamp(string? timestamp) =>
        DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : null;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
