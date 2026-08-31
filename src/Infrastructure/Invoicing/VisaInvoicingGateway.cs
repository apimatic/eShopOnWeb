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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Talks to the Visa invoicing product (CyberSource Invoicing v2) through the official CyberSource
/// REST SDK. This is the only place in the application that calls Visa. Every call is routed through
/// the configured <see cref="VisaOptions.BaseUrl"/> — the SDK's run environment (host) is derived
/// solely from it, so no provider call carries a hard-coded host, and the same build can run against
/// a different address by changing configuration alone.
/// </summary>
public class VisaInvoicingGateway : IVisaInvoicingGateway
{
    private const string DeliveryModeNone = "None";
    private const int PageSize = 100;
    private const int MaxInvoicesToScan = 5000;
    private const int ReconciliationConcurrency = 6;

    private readonly VisaOptions _options;
    private readonly IAppLogger<VisaInvoicingGateway> _logger;

    public VisaInvoicingGateway(IOptions<VisaOptions> options, IAppLogger<VisaInvoicingGateway> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GatewayInvoice> RaiseAsync(GatewayInvoiceDraft draft, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        var request = new CreateInvoiceRequest
        {
            CustomerInformation = new Invoicingv2invoicesCustomerInformation
            {
                Name = draft.CustomerName,
                Email = draft.CustomerEmail
            },
            InvoiceInformation = new Invoicingv2invoicesInvoiceInformation
            {
                InvoiceNumber = draft.InvoiceNumber,
                Description = draft.Description,
                DueDate = ToProviderDate(draft.DueDate),
                // Start not yet put to the shopper: a draft, with no email dispatched.
                DeliveryMode = DeliveryModeNone,
                SendImmediately = false
            },
            OrderInformation = BuildOrderInformation(draft.Currency, draft.TotalAmount, draft.Lines)
        };

        return await ExecuteAsync(
            () => api.CreateInvoiceAsync(request),
            r => new GatewayInvoice(r.Id, r.InvoiceInformation?.InvoiceNumber, r.Status, r.InvoiceInformation?.PaymentLink,
                null, null, null, null, null, Array.Empty<GatewayHistoryEntry>()),
            "raise a bill");
    }

    public async Task<GatewayInvoice> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        return await ExecuteAsync(
            () => api.GetInvoiceAsync(providerInvoiceId),
            MapDetail,
            $"read bill {providerInvoiceId}");
    }

    public async Task<GatewayInvoice> CorrectAsync(string providerInvoiceId, GatewayInvoiceCorrection correction, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        var request = new UpdateInvoiceRequest
        {
            InvoiceInformation = new Invoicingv2invoicesidInvoiceInformation
            {
                Description = correction.Description,
                DueDate = ToProviderDate(correction.DueDate)
            },
            CustomerInformation = new Invoicingv2invoicesCustomerInformation
            {
                Name = correction.CustomerName,
                Email = correction.CustomerEmail
            },
            OrderInformation = BuildOrderInformation(correction.Currency, correction.TotalAmount, correction.Lines)
        };

        return await ExecuteAsync(
            () => api.UpdateInvoiceAsync(providerInvoiceId, request),
            r => new GatewayInvoice(r.Id, r.InvoiceInformation?.InvoiceNumber, r.Status, r.InvoiceInformation?.PaymentLink,
                null, null, null, null, null, Array.Empty<GatewayHistoryEntry>()),
            $"correct bill {providerInvoiceId}");
    }

    public async Task<GatewayInvoice> IssueAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        // "Send" publishes and puts the bill to the shopper; it is accepted whether the bill is a
        // fresh draft or was already published by a prior correction.
        return await ExecuteAsync(
            () => api.PerformSendActionAsync(providerInvoiceId),
            r => new GatewayInvoice(r.Id, null, r.Status, null, null, null, null, null, null, Array.Empty<GatewayHistoryEntry>()),
            $"issue bill {providerInvoiceId}");
    }

    public async Task<GatewayInvoice> WithdrawAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var api = CreateApi();
        return await ExecuteAsync(
            () => api.PerformCancelActionAsync(providerInvoiceId),
            r => new GatewayInvoice(r.Id, null, r.Status, null, null, null, null, null, null, Array.Empty<GatewayHistoryEntry>()),
            $"withdraw bill {providerInvoiceId}");
    }

    public async Task<IReadOnlyList<GatewayInvoiceSummary>> ListRaisedBetweenAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // The provider's list endpoint carries neither a creation date nor a date filter, so gather
        // every bill on the account and establish each one's raised-date from its own history, then
        // keep those raised within the range.
        var ids = new List<string>();
        var api = CreateApi();
        int offset = 0;
        while (offset < MaxInvoicesToScan)
        {
            var page = await ExecuteAsync(
                () => api.GetAllInvoicesAsync(offset, PageSize, null),
                r => r,
                "list bills");

            var invoices = page.Invoices;
            if (invoices is null || invoices.Count == 0)
            {
                break;
            }

            ids.AddRange(invoices.Where(i => i.Id is not null).Select(i => i.Id));
            offset += invoices.Count;

            if (page.TotalInvoices.HasValue && offset >= page.TotalInvoices.Value)
            {
                break;
            }
        }

        var results = new List<GatewayInvoiceSummary>();
        using var throttle = new SemaphoreSlim(ReconciliationConcurrency);
        var tasks = ids.Distinct(StringComparer.Ordinal).Select(async id =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var detail = await GetAsync(id, cancellationToken);
                if (detail.RaisedAt is DateTimeOffset raisedAt && raisedAt >= from && raisedAt <= to)
                {
                    return new GatewayInvoiceSummary(detail.Id, detail.InvoiceNumber, detail.Status,
                        detail.TotalAmount, detail.Currency, detail.CustomerName, raisedAt);
                }

                return null;
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

        return results;
    }

    // ----- provider request/response mapping -----

    private static Invoicingv2invoicesOrderInformation BuildOrderInformation(string currency, decimal totalAmount, IReadOnlyList<GatewayInvoiceLine> lines) =>
        new()
        {
            AmountDetails = new Invoicingv2invoicesOrderInformationAmountDetails
            {
                TotalAmount = FormatAmount(totalAmount),
                Currency = currency
            },
            LineItems = lines.Select(l => new Invoicingv2invoicesOrderInformationLineItems
            {
                ProductSku = l.ProductSku,
                ProductName = l.ProductName,
                Quantity = l.Quantity,
                UnitPrice = FormatAmount(l.UnitPrice)
            }).ToList()
        };

    private static GatewayInvoice MapDetail(InvoicingV2InvoicesGet200Response r)
    {
        var history = (r.InvoiceHistory ?? new List<InvoicingV2InvoicesGet200ResponseInvoiceHistory>())
            .Select(h => new GatewayHistoryEntry(h.Event, ToOffset(h.Date)))
            .ToList();

        DateTimeOffset? raisedAt = history
            .Where(h => h.Date.HasValue)
            .Select(h => h.Date!.Value)
            .DefaultIfEmpty()
            .Min();
        if (raisedAt == default(DateTimeOffset))
        {
            raisedAt = null;
        }

        return new GatewayInvoice(
            Id: r.Id,
            InvoiceNumber: r.InvoiceInformation?.InvoiceNumber,
            Status: r.Status,
            PaymentLink: r.InvoiceInformation?.PaymentLink,
            TotalAmount: ParseAmount(r.OrderInformation?.AmountDetails?.TotalAmount),
            Currency: r.OrderInformation?.AmountDetails?.Currency,
            CustomerName: r.CustomerInformation?.Name,
            DueDate: ToDateOnly(r.InvoiceInformation?.DueDate),
            RaisedAt: raisedAt,
            History: history);
    }

    // ----- SDK plumbing -----

    private InvoicesApi CreateApi()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(_options.BaseUrl)) missing.Add(nameof(VisaOptions.BaseUrl));
        if (string.IsNullOrWhiteSpace(_options.MerchantId)) missing.Add(nameof(VisaOptions.MerchantId));
        if (string.IsNullOrWhiteSpace(_options.KeyId)) missing.Add(nameof(VisaOptions.KeyId));
        if (string.IsNullOrWhiteSpace(_options.SecretKey)) missing.Add(nameof(VisaOptions.SecretKey));
        if (missing.Count > 0)
        {
            // Never echoes any secret value — only the names of what is missing.
            throw new VisaInvoicingException(
                $"Visa configuration is incomplete. Missing: {string.Join(", ", missing)}.",
                providerRejected: false);
        }

        var merchantConfig = new Dictionary<string, string>
        {
            { "authenticationType", "jwt" },
            { "jwtKeyType", "SHARED_SECRET" },
            { "merchantID", _options.MerchantId! },
            { "merchantKeyId", _options.KeyId! },
            { "merchantsecretKey", _options.SecretKey! },
            { "runEnvironment", ResolveRunEnvironment(_options.BaseUrl!) },
            { "isSDK", "true" }
        };

        return new InvoicesApi(new Configuration(merchConfigDictObj: merchantConfig));
    }

    /// <summary>
    /// Derives the SDK run environment (the host every call is sent to) from the configured base
    /// address. Accepts either a full URL or a bare host so the value is honored verbatim as the
    /// base address in place of any default the SDK would otherwise use.
    /// </summary>
    private static string ResolveRunEnvironment(string baseUrl)
    {
        var trimmed = baseUrl.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return uri.Authority;
        }

        return trimmed.TrimEnd('/');
    }

    private async Task<TResult> ExecuteAsync<TResponse, TResult>(
        Func<Task<TResponse>> call, Func<TResponse, TResult> map, string action)
    {
        try
        {
            var response = await call();
            return map(response);
        }
        catch (ApiException ex)
        {
            throw Translate(ex, action);
        }
    }

    private VisaInvoicingException Translate(ApiException ex, string action)
    {
        var reason = ExtractReason(ex.ErrorContent);
        var rejected = ex.ErrorCode >= 400 && ex.ErrorCode < 500;
        // Log the outcome without any secret material.
        _logger.LogWarning($"Provider refused to {action}: HTTP {ex.ErrorCode}{(reason is null ? string.Empty : $" ({reason})")}.");
        return new VisaInvoicingException(
            $"Failed to {action}.",
            providerRejected: rejected,
            providerReason: reason,
            httpStatusCode: ex.ErrorCode,
            inner: ex);
    }

    private static string? ExtractReason(object? errorContent)
    {
        var content = errorContent?.ToString();
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("reason", out var reason) &&
                reason.ValueKind == JsonValueKind.String)
            {
                return reason.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON body; nothing structured to extract.
        }

        return null;
    }

    // ----- value conversions -----

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? amount) =>
        decimal.TryParse(amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static DateTime ToProviderDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private static DateOnly? ToDateOnly(DateTime? value) => value.HasValue ? DateOnly.FromDateTime(value.Value) : null;

    private static DateTimeOffset? ToOffset(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        // The provider reports history timestamps in UTC.
        var dt = value.Value;
        return dt.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc))
            : new DateTimeOffset(dt.ToUniversalTime());
    }
}
