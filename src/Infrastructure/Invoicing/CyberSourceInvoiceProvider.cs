using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CyberSource.Api;
using CyberSource.Client;
using CyberSource.Model;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using STJ = System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Talks to Visa's CyberSource invoicing API (<c>/invoicing/v2/invoices</c>) via the official
/// CyberSource .NET SDK. Every call is routed through <see cref="VisaOptions.BaseUrl"/>; credentials
/// come from configuration/user-secrets and the shared secret is never logged.
///
/// Responses are read from the SDK models' own JSON so a single mapping serves every operation
/// regardless of the specific response type the SDK returns.
/// </summary>
public class CyberSourceInvoiceProvider : IInvoiceProvider
{
    private const int PageSize = 100;
    private const int DetailFetchConcurrency = 6;

    private readonly VisaOptions _options;
    private readonly IAppLogger<CyberSourceInvoiceProvider> _logger;

    public CyberSourceInvoiceProvider(IOptions<VisaOptions> options, IAppLogger<CyberSourceInvoiceProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<ProviderInvoice> CreateInvoiceAsync(ProviderInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        var body = new CreateInvoiceRequest
        {
            InvoiceInformation = new Invoicingv2invoicesInvoiceInformation
            {
                InvoiceNumber = request.InvoiceNumber,
                Description = request.Description,
                DueDate = ToDate(request.DueDate),
                SendImmediately = false, // The bill starts as a draft, not yet put to the shopper.
                AllowPartialPayments = false
            },
            OrderInformation = BuildOrderInformation(request),
            CustomerInformation = BuildCustomerInformation(request)
        };

        return ExecuteAsync(
            () => api.CreateInvoiceAsync(body),
            result => ParseInvoice(result.ToJson()),
            $"create bill {request.InvoiceNumber}");
    }

    public Task<ProviderInvoice> GetInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        return ExecuteAsync(
            () => api.GetInvoiceAsync(providerInvoiceId),
            result => ParseInvoice(result.ToJson()),
            $"read bill {providerInvoiceId}");
    }

    public Task<ProviderInvoice> UpdateInvoiceAsync(string providerInvoiceId, ProviderInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        var body = new UpdateInvoiceRequest
        {
            InvoiceInformation = new Invoicingv2invoicesidInvoiceInformation
            {
                Description = request.Description,
                DueDate = ToDate(request.DueDate),
                AllowPartialPayments = false
            },
            OrderInformation = BuildOrderInformation(request),
            CustomerInformation = BuildCustomerInformation(request)
        };

        return ExecuteAsync(
            () => api.UpdateInvoiceAsync(providerInvoiceId, body),
            result => ParseInvoice(result.ToJson()),
            $"correct bill {providerInvoiceId}");
    }

    public Task<ProviderInvoice> IssueInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        return ExecuteAsync(
            () => api.PerformSendActionAsync(providerInvoiceId),
            result => ParseInvoice(result.ToJson()),
            $"issue bill {providerInvoiceId}");
    }

    public Task<ProviderInvoice> WithdrawInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        return ExecuteAsync(
            () => api.PerformCancelActionAsync(providerInvoiceId),
            result => ParseInvoice(result.ToJson()),
            $"withdraw bill {providerInvoiceId}");
    }

    public async Task<IReadOnlyList<ProviderInvoiceRecord>> ListInvoicesAsync(CancellationToken cancellationToken = default)
    {
        var api = CreateApi();

        // 1. Page the whole account. The list itself does not carry creation dates.
        var summaries = new List<(string Id, string Status, decimal? Amount, string? Currency, DateOnly? DueDate, string? CustomerName)>();
        var offset = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            InvoicingV2InvoicesAllGet200Response page;
            try
            {
                page = await api.GetAllInvoicesAsync(offset, PageSize, null).ConfigureAwait(false);
            }
            catch (ApiException ex)
            {
                throw Translate(ex, "list bills");
            }

            var invoices = page.Invoices ?? new List<InvoicingV2InvoicesAllGet200ResponseInvoices>();
            foreach (var invoice in invoices)
            {
                var parsed = ParseInvoice(invoice.ToJson());
                summaries.Add((parsed.Id, parsed.Status, parsed.TotalAmount, parsed.Currency, parsed.DueDate, parsed.CustomerName));
            }

            offset += invoices.Count;
            var total = page.TotalInvoices ?? summaries.Count;
            if (invoices.Count == 0 || offset >= total)
            {
                break;
            }
        }

        // 2. The provider only reveals when a bill was raised in its per-invoice history, so fetch
        //    each bill's detail (with bounded concurrency) to establish its creation date.
        var createdDates = new Dictionary<string, DateTimeOffset?>(StringComparer.OrdinalIgnoreCase);
        using var gate = new SemaphoreSlim(DetailFetchConcurrency);
        var tasks = summaries.Select(async summary =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var detail = await GetInvoiceAsync(summary.Id, cancellationToken).ConfigureAwait(false);
                var created = detail.History
                    .Where(h => h.Date.HasValue)
                    .Select(h => h.Date!.Value)
                    .DefaultIfEmpty()
                    .Min();
                return (summary.Id, Created: created == default ? (DateTimeOffset?)null : created);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not read creation date for bill {summary.Id}: {ex.Message}");
                return (summary.Id, Created: (DateTimeOffset?)null);
            }
            finally
            {
                gate.Release();
            }
        });

        foreach (var (id, created) in await Task.WhenAll(tasks).ConfigureAwait(false))
        {
            createdDates[id] = created;
        }

        return summaries.Select(summary => new ProviderInvoiceRecord
        {
            Id = summary.Id,
            Status = summary.Status,
            CreatedDate = createdDates.TryGetValue(summary.Id, out var created) ? created : null,
            TotalAmount = summary.Amount,
            Currency = summary.Currency,
            DueDate = summary.DueDate,
            CustomerName = summary.CustomerName
        }).ToList();
    }

    private static Invoicingv2invoicesOrderInformation BuildOrderInformation(ProviderInvoiceRequest request) => new()
    {
        AmountDetails = new Invoicingv2invoicesOrderInformationAmountDetails
        {
            TotalAmount = request.TotalAmount.ToString("F2", CultureInfo.InvariantCulture),
            Currency = request.Currency
        },
        LineItems = request.Lines.Select(line => new Invoicingv2invoicesOrderInformationLineItems
        {
            ProductSku = line.ProductSku,
            ProductName = line.ProductName,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice.ToString("F2", CultureInfo.InvariantCulture)
        }).ToList()
    };

    private static Invoicingv2invoicesCustomerInformation BuildCustomerInformation(ProviderInvoiceRequest request) => new()
    {
        Name = request.CustomerName,
        Email = request.CustomerEmail
    };

    private InvoicesApi CreateApi() => new InvoicesApi(new Configuration(merchConfigDictObj: BuildMerchantConfig()));

    /// <summary>
    /// Build the CyberSource SDK merchant configuration. The request host comes solely from
    /// <see cref="VisaOptions.BaseUrl"/> — there is no hard-coded host — so every provider call is
    /// routed through the configured base address.
    /// </summary>
    private Dictionary<string, string> BuildMerchantConfig()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvalidOperationException("Visa:BaseUrl is not configured.");
        }
        if (string.IsNullOrWhiteSpace(_options.MerchantId) ||
            string.IsNullOrWhiteSpace(_options.KeyId) ||
            string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            throw new InvalidOperationException(
                "Visa credentials are not configured. Supply Visa:MerchantId, Visa:KeyId and Visa:SecretKey via user-secrets.");
        }

        var host = ResolveHost(_options.BaseUrl);

        return new Dictionary<string, string>
        {
            { "merchantID", _options.MerchantId },
            { "runEnvironment", host },
            { "authenticationType", "jwt" },
            { "jwtKeyType", "SHARED_SECRET" },
            { "merchantKeyId", _options.KeyId },
            { "merchantsecretKey", _options.SecretKey },
            { "isSDK", "true" },
            { "timeout", "300000" }
        };
    }

    /// <summary>
    /// Reduce the configured base URL to the host[:port] the SDK uses as its request host, so a
    /// value such as <c>https://apitest.cybersource.com</c> is honoured whatever its scheme.
    /// </summary>
    private static string ResolveHost(string baseUrl)
    {
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var absolute))
        {
            return absolute.Authority;
        }
        // Already host-only (e.g. "apitest.cybersource.com"); strip any stray path/scheme remnants.
        return baseUrl.Trim().TrimEnd('/');
    }

    private async Task<ProviderInvoice> ExecuteAsync<TResponse>(
        Func<Task<TResponse>> call,
        Func<TResponse, ProviderInvoice> map,
        string action)
    {
        try
        {
            var response = await call().ConfigureAwait(false);
            return map(response);
        }
        catch (ApiException ex)
        {
            throw Translate(ex, action);
        }
    }

    /// <summary>
    /// Translate a CyberSource API error. A 4xx from the provider on a state-changing action is a
    /// legitimate refusal of the transition (for example, cancelling a paid bill) — surfaced as a
    /// state conflict, not an integration fault. The shared secret is never part of the error.
    /// </summary>
    private Exception Translate(ApiException ex, string action)
    {
        var message = ExtractProviderMessage(ex.ErrorContent);
        _logger.LogWarning($"Provider refused to {action}: HTTP {ex.ErrorCode}. {message}");

        if (ex.ErrorCode >= 400 && ex.ErrorCode < 500)
        {
            return new ProviderOperationRefusedException(
                $"The provider could not {action}: {message}", ex);
        }

        return new Exception($"The provider failed to {action} (HTTP {ex.ErrorCode}).", ex);
    }

    private static string ExtractProviderMessage(object? errorContent)
    {
        var raw = errorContent?.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "no further detail was provided.";
        }

        try
        {
            using var doc = STJ.JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var m) && m.ValueKind == STJ.JsonValueKind.String)
            {
                return m.GetString()!;
            }
            if (root.TryGetProperty("reason", out var r) && r.ValueKind == STJ.JsonValueKind.String)
            {
                return r.GetString()!;
            }
        }
        catch (STJ.JsonException)
        {
            // Fall through to the raw (trimmed) content.
        }

        return raw.Length > 500 ? raw.Substring(0, 500) : raw;
    }

    private static ProviderInvoice ParseInvoice(string json)
    {
        using var doc = STJ.JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? paymentLink = null, invoiceNumber = null, currency = null, customerName = null, customerEmail = null;
        decimal? total = null;
        DateOnly? dueDate = null;

        if (root.TryGetProperty("invoiceInformation", out var ii) && ii.ValueKind == STJ.JsonValueKind.Object)
        {
            invoiceNumber = GetString(ii, "invoiceNumber");
            paymentLink = GetString(ii, "paymentLink");
            dueDate = ParseDate(GetString(ii, "dueDate"));
        }

        if (root.TryGetProperty("orderInformation", out var oi) && oi.ValueKind == STJ.JsonValueKind.Object &&
            oi.TryGetProperty("amountDetails", out var ad) && ad.ValueKind == STJ.JsonValueKind.Object)
        {
            total = ParseDecimal(GetString(ad, "totalAmount"));
            currency = GetString(ad, "currency");
        }

        if (root.TryGetProperty("customerInformation", out var ci) && ci.ValueKind == STJ.JsonValueKind.Object)
        {
            customerName = GetString(ci, "name");
            customerEmail = GetString(ci, "email");
        }

        var history = new List<ProviderInvoiceEvent>();
        if (root.TryGetProperty("invoiceHistory", out var hist) && hist.ValueKind == STJ.JsonValueKind.Array)
        {
            foreach (var entry in hist.EnumerateArray())
            {
                history.Add(new ProviderInvoiceEvent(
                    GetString(entry, "event") ?? "UNKNOWN",
                    ParseDateTime(GetString(entry, "date"))));
            }
        }

        return new ProviderInvoice
        {
            Id = GetString(root, "id") ?? throw new InvalidOperationException("The provider returned a bill with no id."),
            InvoiceNumber = invoiceNumber,
            Status = GetString(root, "status") ?? InvoiceStatusUnknown,
            PaymentLink = paymentLink,
            TotalAmount = total,
            Currency = currency,
            DueDate = dueDate,
            CustomerName = customerName,
            CustomerEmail = customerEmail,
            History = history
        };
    }

    private const string InvoiceStatusUnknown = "UNKNOWN";

    private static string? GetString(STJ.JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == STJ.JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static DateTimeOffset? ParseDateTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt
            : null;

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTime ToDate(DateOnly date) =>
        new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc);
}
