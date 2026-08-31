using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// The Visa (CyberSource) implementation of <see cref="IInvoiceProvider"/>, backed by the CyberSource
/// .NET SDK's Invoicing v2 API. This is the only place in the app that talks to Visa.
///
/// <para>Every call is routed through <c>Visa:BaseUrl</c>: the SDK addresses requests (and signs
/// them) using the host given in <c>runEnvironment</c>, which is derived verbatim from the configured
/// base address, so no provider call carries a hard-coded host.</para>
/// </summary>
public class VisaInvoiceProvider : IInvoiceProvider
{
    private const int PageSize = 100;

    private readonly InvoicesApi _api;

    public VisaInvoiceProvider(IOptions<VisaSettings> options, ILoggerFactory loggerFactory)
    {
        var settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvoiceProviderException("Visa:BaseUrl is not configured.");
        }
        if (string.IsNullOrWhiteSpace(settings.MerchantId) ||
            string.IsNullOrWhiteSpace(settings.KeyId) ||
            string.IsNullOrWhiteSpace(settings.SecretKey))
        {
            throw new InvoiceProviderException(
                "Visa credentials are not configured (MerchantId / KeyId / SecretKey).");
        }

        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvoiceProviderException($"Visa:BaseUrl '{settings.BaseUrl}' is not a valid absolute URL.");
        }

        // The SDK builds and signs every request against the host in `runEnvironment`. Deriving it
        // from the configured base address (host[:port]) is what routes all provider traffic through
        // Visa:BaseUrl and lets the same build run against a different address.
        var runEnvironment = baseUri.Authority;

        var merchantConfig = new Dictionary<string, string>
        {
            { "merchantID", settings.MerchantId },
            { "runEnvironment", runEnvironment },
            { "authenticationType", "jwt" },
            { "jwtKeyType", "SHARED_SECRET" },
            { "merchantKeyId", settings.KeyId },
            { "merchantsecretKey", settings.SecretKey },
            { "isSDK", "true" },
            { "timeout", "60000" },
        };

        // The SDK always masks sensitive values (secret key, PII) before logging.
        var configuration = new Configuration(merchConfigDictObj: merchantConfig, loggerFactory: loggerFactory);
        _api = new InvoicesApi(configuration);
    }

    public async Task<ProviderInvoice> CreateDraftInvoiceAsync(NewInvoice invoice, CancellationToken cancellationToken = default)
    {
        var request = new CreateInvoiceRequest
        {
            CustomerInformation = new Invoicingv2invoicesCustomerInformation
            {
                Name = invoice.Customer.Name,
                Email = invoice.Customer.Email
            },
            InvoiceInformation = new Invoicingv2invoicesInvoiceInformation
            {
                Description = invoice.Description,
                DueDate = ToUtcDate(invoice.DueDate),
                TransactionReferenceNumber = invoice.ReferenceNumber,
                // Keep the bill a DRAFT (not yet put to the shopper). Leaving DeliveryMode unset and
                // SendImmediately=false means no email is sent and the bill stays in DRAFT until issued.
                SendImmediately = false
            },
            OrderInformation = BuildOrderInformation(invoice.Currency, invoice.TotalAmount, invoice.Lines)
        };

        return await InvokeAsync(() => _api.CreateInvoiceAsync(request), "create the bill");
    }

    public async Task<ProviderInvoice> GetInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default) =>
        await InvokeAsync(() => _api.GetInvoiceAsync(providerInvoiceId), "read the bill");

    public async Task<ProviderInvoice> UpdateInvoiceAsync(string providerInvoiceId, InvoiceAmendment amendment, CancellationToken cancellationToken = default)
    {
        var request = new UpdateInvoiceRequest
        {
            CustomerInformation = new Invoicingv2invoicesCustomerInformation
            {
                Name = amendment.Customer.Name,
                Email = amendment.Customer.Email
            },
            InvoiceInformation = new Invoicingv2invoicesidInvoiceInformation
            {
                Description = amendment.Description,
                DueDate = ToUtcDate(amendment.DueDate)
            },
            OrderInformation = BuildOrderInformation(amendment.Currency, amendment.TotalAmount, amendment.Lines)
        };

        return await InvokeAsync(() => _api.UpdateInvoiceAsync(providerInvoiceId, request), "correct the bill");
    }

    public async Task<ProviderInvoice> PublishInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default) =>
        await InvokeAsync(() => _api.PerformPublishActionAsync(providerInvoiceId), "put the bill to the shopper");

    public async Task<ProviderInvoice> CancelInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default) =>
        await InvokeAsync(() => _api.PerformCancelActionAsync(providerInvoiceId), "withdraw the bill");

    public async Task<IReadOnlyList<ProviderInvoiceSummary>> ListInvoicesCreatedBetweenAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        // GetAllInvoices has no date filter, so the whole account is paged and filtered here on the
        // provider's own created-date. The list endpoint does not always populate the created-date
        // (the sandbox leaves it empty), so where it is missing the bill's creation time is sourced
        // from its own history via GetInvoice. This keeps every date the provider's, not invented.
        var listed = new List<ProviderInvoiceSummary>();
        var offset = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = await InvokeRawAsync(
                () => _api.GetAllInvoicesAsync(offset, PageSize, null!), "list bills for reconciliation");

            var invoices = GetProp(page, "Invoices") as IEnumerable;
            var total = GetProp(page, "TotalInvoices") as int?;

            var pageCount = 0;
            if (invoices is not null)
            {
                foreach (var item in invoices)
                {
                    pageCount++;
                    var orderInfo = GetProp(item, "OrderInformation");
                    var amountDetails = orderInfo is null ? null : GetProp(orderInfo, "AmountDetails");
                    var customerInfo = GetProp(item, "CustomerInformation");

                    listed.Add(new ProviderInvoiceSummary(
                        Id: GetString(item, "Id") ?? string.Empty,
                        Status: GetString(item, "Status"),
                        CreatedDate: GetDateTimeOffset(item, "CreatedDate"),
                        CustomerName: GetString(customerInfo, "Name"),
                        Amount: GetDecimal(amountDetails, "TotalAmount"),
                        Currency: GetString(amountDetails, "Currency")));
                }
            }

            offset += PageSize;
            if (pageCount < PageSize || (total is not null && offset >= total.Value) || pageCount == 0)
            {
                break;
            }
        }

        // Fill in any missing created-dates from each bill's history, with bounded concurrency.
        using var throttle = new SemaphoreSlim(6);
        var dated = await Task.WhenAll(listed.Select(async summary =>
        {
            if (summary.CreatedDate is not null || string.IsNullOrEmpty(summary.Id))
            {
                return summary;
            }

            await throttle.WaitAsync(cancellationToken);
            try
            {
                var detail = await InvokeRawAsync(() => _api.GetInvoiceAsync(summary.Id), "read a bill for reconciliation");
                var created = EarliestHistoryDate(GetProp(detail, "InvoiceHistory"));
                return summary with { CreatedDate = created };
            }
            finally
            {
                throttle.Release();
            }
        }));

        return dated
            .Where(s => s.CreatedDate is not null && s.CreatedDate >= fromUtc && s.CreatedDate <= toUtc)
            .ToList();
    }

    private static DateTimeOffset? EarliestHistoryDate(object? history)
    {
        DateTimeOffset? earliest = null;
        if (history is IEnumerable items)
        {
            foreach (var item in items)
            {
                var date = GetDateTimeOffset(item, "Date");
                if (date is not null && (earliest is null || date < earliest))
                {
                    earliest = date;
                }
            }
        }
        return earliest;
    }

    // ---- request building ----

    private static Invoicingv2invoicesOrderInformation BuildOrderInformation(
        string currency, decimal totalAmount, IReadOnlyList<NewInvoiceLine> lines) =>
        new()
        {
            AmountDetails = new Invoicingv2invoicesOrderInformationAmountDetails
            {
                TotalAmount = FormatAmount(totalAmount),
                Currency = currency
            },
            LineItems = lines.Select(l => new Invoicingv2invoicesOrderInformationLineItems
            {
                ProductName = l.ProductName,
                ProductSku = l.Sku,
                Quantity = l.Quantity,
                UnitPrice = FormatAmount(l.UnitPrice),
                TotalAmount = FormatAmount(l.TotalAmount)
            }).ToList()
        };

    private static string FormatAmount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static DateTime ToUtcDate(DateOnly date) =>
        new(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc);

    // ---- SDK invocation + error mapping ----

    private async Task<ProviderInvoice> InvokeAsync<T>(Func<Task<T>> call, string action)
    {
        var response = await InvokeRawAsync(call, action);
        return ToProviderInvoice(response!);
    }

    private static async Task<T> InvokeRawAsync<T>(Func<Task<T>> call, string action)
    {
        try
        {
            return await call();
        }
        catch (ApiException ex)
        {
            throw MapApiException(ex, action);
        }
        catch (InvoiceStateConflictException)
        {
            throw;
        }
        catch (InvoiceNotFoundAtProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The SDK surfaces connectivity failures as low-level exceptions; present them as a clean
            // provider error rather than leaking an opaque stack trace.
            throw new InvoiceProviderException($"Unable to {action} with the invoicing provider.", ex);
        }
    }

    private static Exception MapApiException(ApiException ex, string action)
    {
        var (reason, message) = ParseError((object?)ex.ErrorContent);

        if (ex.ErrorCode == 404)
        {
            return new InvoiceNotFoundAtProviderException($"The invoicing provider has no record of this bill.");
        }

        // 4xx responses from state-changing calls mean the provider legitimately refused given the
        // state the bill is in (e.g. ACTION_NOT_ALLOWED on a withdrawn or already-issued bill).
        if (ex.ErrorCode >= 400 && ex.ErrorCode < 500)
        {
            var detail = message ?? reason ?? "the request was refused";
            return new InvoiceStateConflictException($"The provider refused to {action}: {detail}.");
        }

        return new InvoiceProviderException($"The invoicing provider failed to {action} (status {ex.ErrorCode}).");
    }

    private static (string? Reason, string? Message) ParseError(object? errorContent)
    {
        var raw = errorContent?.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            string? reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null;
            string? message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            return (reason, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    // ---- response mapping (reflection tolerates the many near-identical SDK response types) ----

    private static ProviderInvoice ToProviderInvoice(object response)
    {
        var invoiceInfo = GetProp(response, "InvoiceInformation");
        var orderInfo = GetProp(response, "OrderInformation");
        var amountDetails = orderInfo is null ? null : GetProp(orderInfo, "AmountDetails");
        var customerInfo = GetProp(response, "CustomerInformation");

        return new ProviderInvoice(
            Id: GetString(response, "Id") ?? string.Empty,
            Status: GetString(response, "Status"),
            PaymentLink: GetString(invoiceInfo, "PaymentLink"),
            DueDate: GetDateOnly(invoiceInfo, "DueDate"),
            Amount: GetDecimal(amountDetails, "TotalAmount"),
            Currency: GetString(amountDetails, "Currency"),
            CustomerName: GetString(customerInfo, "Name"),
            CustomerEmail: GetString(customerInfo, "Email"),
            Description: GetString(invoiceInfo, "Description"),
            History: ExtractHistory(GetProp(response, "InvoiceHistory")));
    }

    private static IReadOnlyList<ProviderInvoiceEvent> ExtractHistory(object? history)
    {
        var events = new List<ProviderInvoiceEvent>();
        if (history is IEnumerable items)
        {
            foreach (var item in items)
            {
                events.Add(new ProviderInvoiceEvent(
                    Event: GetString(item, "Event"),
                    Date: GetDateTimeOffset(item, "Date")));
            }
        }
        return events;
    }

    private static object? GetProp(object? target, string name) =>
        target?.GetType()
            .GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?.GetValue(target);

    private static string? GetString(object? target, string name) => GetProp(target, name)?.ToString();

    private static decimal? GetDecimal(object? target, string name)
    {
        var value = GetProp(target, name);
        return value switch
        {
            null => null,
            decimal d => d,
            string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static DateOnly? GetDateOnly(object? target, string name) => GetProp(target, name) switch
    {
        DateTime dt => DateOnly.FromDateTime(dt),
        DateTimeOffset dto => DateOnly.FromDateTime(dto.UtcDateTime),
        string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed) => DateOnly.FromDateTime(parsed),
        _ => null
    };

    private static DateTimeOffset? GetDateTimeOffset(object? target, string name) => GetProp(target, name) switch
    {
        DateTime dt => new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt).ToUniversalTime(),
        DateTimeOffset dto => dto.ToUniversalTime(),
        string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) => parsed,
        _ => null
    };
}
