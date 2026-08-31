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
using Microsoft.Extensions.Options;
using CsConfiguration = CyberSource.Client.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// The Visa invoicing integration, built on the CyberSource REST SDK's Invoicing v2 API. Every call
/// is routed through the configured <c>Visa:BaseUrl</c>; authentication uses the JWT shared-secret
/// credentials supplied out of band. Provider refusals are surfaced as
/// <see cref="InvoiceProviderException"/> with a caller-safe message; the shared secret is never
/// logged or returned.
/// </summary>
public class CyberSourceInvoiceProvider : IInvoiceProvider
{
    // Raise a bill so the provider publishes it (status CREATED) but dispatches no email — the bill
    // is not yet "put to the shopper" until it is issued.
    private const string DeliveryModeNone = "None";

    private const int PageSize = 100;
    private const int MaxPages = 500; // safety bound while paging the whole range
    private const int DetailFetchConcurrency = 8; // bounded parallelism for per-bill date reads

    private readonly VisaSettings _settings;
    private readonly IAppLogger<CyberSourceInvoiceProvider> _logger;
    private readonly object _gate = new();
    private InvoicesApi? _api;

    public CyberSourceInvoiceProvider(IOptions<VisaSettings> settings, IAppLogger<CyberSourceInvoiceProvider> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ProviderInvoice> RaiseAsync(RaiseInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var createRequest = new CreateInvoiceRequest
        {
            CustomerInformation = new Invoicingv2invoicesCustomerInformation
            {
                Name = request.CustomerName,
                Email = request.CustomerEmail
            },
            InvoiceInformation = new Invoicingv2invoicesInvoiceInformation
            {
                Description = request.Description,
                DueDate = ToProviderDate(request.DueDate),
                TransactionReferenceNumber = request.InvoiceReference,
                // Publish the bill without emailing anyone; it is put to the shopper later, on issue.
                DeliveryMode = DeliveryModeNone,
                SendImmediately = false
            },
            OrderInformation = BuildOrderInformation(request.Amount, request.Currency, request.LineItems)
        };

        var response = await InvokeAsync(() => Api().CreateInvoiceAsync(createRequest), "raise invoice");
        _logger.LogInformation("Provider raised invoice {0} (status {1}).", response.Id, response.Status ?? "unknown");
        return MapInvoice(response.Id, response.Status, response.InvoiceInformation?.PaymentLink, response.SubmitTimeUtc, history: null);
    }

    public async Task<ProviderInvoice> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = await InvokeAsync(() => Api().GetInvoiceAsync(providerInvoiceId), "get invoice");
        return MapInvoice(response.Id, response.Status, response.InvoiceInformation?.PaymentLink, response.SubmitTimeUtc, response.InvoiceHistory);
    }

    public async Task<ProviderInvoice> UpdateAsync(string providerInvoiceId, Microsoft.eShopWeb.ApplicationCore.Interfaces.UpdateInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var updateRequest = new CyberSource.Model.UpdateInvoiceRequest
        {
            CustomerInformation = new Invoicingv2invoicesCustomerInformation
            {
                Name = request.CustomerName,
                Email = request.CustomerEmail
            },
            InvoiceInformation = new Invoicingv2invoicesidInvoiceInformation
            {
                Description = request.Description,
                DueDate = ToProviderDate(request.DueDate)
            },
            OrderInformation = BuildOrderInformation(request.Amount, request.Currency, request.LineItems)
        };

        var response = await InvokeAsync(() => Api().UpdateInvoiceAsync(providerInvoiceId, updateRequest), "update invoice");
        return MapInvoice(response.Id, response.Status, response.InvoiceInformation?.PaymentLink, response.SubmitTimeUtc, history: null);
    }

    public async Task<ProviderInvoice> IssueAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = await InvokeAsync(() => Api().PerformSendActionAsync(providerInvoiceId), "issue invoice");
        _logger.LogInformation("Provider sent invoice {0} (status {1}).", response.Id, response.Status ?? "unknown");
        return MapInvoice(response.Id, response.Status, response.InvoiceInformation?.PaymentLink, response.SubmitTimeUtc, history: null);
    }

    public async Task<ProviderInvoice> WithdrawAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = await InvokeAsync(() => Api().PerformCancelActionAsync(providerInvoiceId), "withdraw invoice");
        _logger.LogInformation("Provider cancelled invoice {0} (status {1}).", response.Id, response.Status ?? "unknown");
        return MapInvoice(response.Id, response.Status, response.InvoiceInformation?.PaymentLink, response.SubmitTimeUtc, history: null);
    }

    public async Task<IReadOnlyList<ProviderInvoiceSummary>> ListCreatedBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // The provider list neither filters by date nor returns a creation date, so page the whole
        // account, then read each bill's creation date from its provider-owned history to cover the
        // entire range. Detail reads are bounded to keep provider load in check.
        var all = new List<InvoicingV2InvoicesAllGet200ResponseInvoices>();
        var offset = 0;
        for (var page = 0; page < MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await InvokeAsync(() => Api().GetAllInvoicesAsync(offset, PageSize), "list invoices");

            var batch = response.Invoices ?? new List<InvoicingV2InvoicesAllGet200ResponseInvoices>();
            if (batch.Count == 0)
            {
                break;
            }

            all.AddRange(batch);
            offset += batch.Count;
            var total = response.TotalInvoices ?? offset;
            if (offset >= total)
            {
                break;
            }
        }

        using var throttle = new SemaphoreSlim(DetailFetchConcurrency);
        var dated = await Task.WhenAll(all.Select(async invoice =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var created = await GetCreatedDateAsync(invoice.Id, cancellationToken);
                return (invoice, created);
            }
            finally
            {
                throttle.Release();
            }
        }));

        var results = new List<ProviderInvoiceSummary>();
        foreach (var (invoice, created) in dated)
        {
            if (created is null || created < from || created > to)
            {
                continue;
            }

            results.Add(new ProviderInvoiceSummary(
                Id: invoice.Id,
                Status: invoice.Status,
                CreatedDate: created,
                Amount: ParseAmount(invoice.OrderInformation?.AmountDetails?.TotalAmount),
                Currency: invoice.OrderInformation?.AmountDetails?.Currency,
                CustomerName: invoice.CustomerInformation?.Name));
        }

        _logger.LogInformation("Provider holds {0} invoice(s); {1} fall within the requested range.", all.Count, results.Count);
        return results;
    }

    /// <summary>Reads a bill's creation date as the earliest event in its provider-owned history.</summary>
    private async Task<DateTimeOffset?> GetCreatedDateAsync(string providerInvoiceId, CancellationToken cancellationToken)
    {
        var response = await InvokeAsync(() => Api().GetInvoiceAsync(providerInvoiceId), "get invoice");
        DateTimeOffset? earliest = null;
        foreach (var h in response.InvoiceHistory ?? new List<InvoicingV2InvoicesGet200ResponseInvoiceHistory>())
        {
            if (!h.Date.HasValue)
            {
                continue;
            }
            var when = new DateTimeOffset(DateTime.SpecifyKind(h.Date.Value, DateTimeKind.Utc));
            if (earliest is null || when < earliest)
            {
                earliest = when;
            }
        }
        return earliest;
    }

    private static Invoicingv2invoicesOrderInformation BuildOrderInformation(decimal amount, string currency, IReadOnlyList<ProviderLineItem> lineItems)
    {
        return new Invoicingv2invoicesOrderInformation
        {
            AmountDetails = new Invoicingv2invoicesOrderInformationAmountDetails
            {
                TotalAmount = FormatAmount(amount),
                Currency = currency
            },
            LineItems = lineItems.Select(li => new Invoicingv2invoicesOrderInformationLineItems
            {
                ProductName = li.ProductName,
                ProductSku = li.Sku,
                Quantity = li.Quantity,
                UnitPrice = FormatAmount(li.UnitPrice)
            }).ToList()
        };
    }

    private static ProviderInvoice MapInvoice(string id, string? status, string? paymentLink, string? submitTimeUtc, List<InvoicingV2InvoicesGet200ResponseInvoiceHistory>? history)
    {
        var events = history?
            .Select(h => new ProviderInvoiceEvent(h.Event, h.Date.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(h.Date.Value, DateTimeKind.Utc)) : (DateTimeOffset?)null))
            .ToList() ?? new List<ProviderInvoiceEvent>();

        return new ProviderInvoice(id, status, paymentLink, ParseDate(submitTimeUtc), events);
    }

    private static DateTime ToProviderDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? value)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : (decimal?)null;

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)
            ? dto
            : (DateTimeOffset?)null;

    /// <summary>Runs an SDK call, translating provider/transport failures into a caller-safe exception.</summary>
    private static async Task<T> InvokeAsync<T>(Func<Task<T>> call, string action)
    {
        try
        {
            return await call();
        }
        catch (ApiException ex)
        {
            var (message, reason, isStateConflict) = DescribeApiError(ex);
            throw new InvoiceProviderException(
                $"The invoicing provider could not {action}: {message}", reason, isStateConflict, ex);
        }
        catch (InvoiceProviderException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvoiceProviderException($"The invoicing provider could not {action}.", inner: ex);
        }
    }

    private static (string Message, string? Reason, bool IsStateConflict) DescribeApiError(ApiException ex)
    {
        var body = ex.ErrorContent?.ToString();
        string? reason = null;
        string? message = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("reason", out JsonElement r) && r.ValueKind == JsonValueKind.String)
                    {
                        reason = r.GetString();
                    }
                    if (doc.RootElement.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String)
                    {
                        message = m.GetString();
                    }
                }
            }
            catch (JsonException)
            {
                // Body was not JSON; fall back to a generic message below.
            }
        }

        // A refusal driven by the bill's current state (e.g. updating a cancelled bill) is an expected
        // outcome, not a fault.
        var isStateConflict = string.Equals(reason, "ACTION_NOT_ALLOWED", StringComparison.OrdinalIgnoreCase)
            || (reason?.Contains("NOT_ALLOWED", StringComparison.OrdinalIgnoreCase) ?? false);

        var safeMessage = message
            ?? reason
            ?? $"the request was rejected (HTTP {ex.ErrorCode}).";

        return (safeMessage, reason, isStateConflict);
    }

    /// <summary>Builds (once) the SDK client, routing every call through <c>Visa:BaseUrl</c>.</summary>
    private InvoicesApi Api()
    {
        if (_api is not null)
        {
            return _api;
        }

        lock (_gate)
        {
            if (_api is null)
            {
                var configuration = new CsConfiguration(merchConfigDictObj: BuildMerchantConfig());
                _api = new InvoicesApi(configuration);
            }
        }

        return _api;
    }

    private IReadOnlyDictionary<string, string> BuildMerchantConfig()
    {
        var host = ResolveHost(_settings.BaseUrl);
        RequireConfigured(_settings.MerchantId, "Visa:MerchantId");
        RequireConfigured(_settings.KeyId, "Visa:KeyId");
        RequireConfigured(_settings.SecretKey, "Visa:SecretKey");

        return new Dictionary<string, string>
        {
            ["merchantID"] = _settings.MerchantId,
            // The SDK builds every request URL as https://{runEnvironment}{path}; deriving it from the
            // configured base URL routes all calls through Visa:BaseUrl with no hard-coded host.
            ["runEnvironment"] = host,
            ["authenticationType"] = "jwt",
            ["jwtKeyType"] = "SHARED_SECRET",
            ["merchantKeyId"] = _settings.KeyId,
            ["merchantsecretKey"] = _settings.SecretKey,
            ["isSDK"] = "true"
        };
    }

    /// <summary>
    /// Turns the configured base URL into the authority the SDK uses as the request host. When set,
    /// the configured value is used verbatim (scheme stripped, since the SDK always calls over HTTPS).
    /// </summary>
    private static string ResolveHost(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvoiceProviderException("Visa:BaseUrl is not configured; no provider address is available.");
        }

        var trimmed = baseUrl.Trim();
        if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring("https://".Length);
        }
        else if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring("http://".Length);
        }

        return trimmed.TrimEnd('/');
    }

    private static void RequireConfigured(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvoiceProviderException($"{name} is not configured; the invoicing provider cannot be reached.");
        }
    }
}
