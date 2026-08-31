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
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing.Visa;

/// <summary>
/// The Visa (CyberSource) implementation of <see cref="IInvoiceProvider"/>. It talks to Visa's
/// invoicing platform exclusively through the CyberSource REST SDK, routes every call through the
/// configured <c>Visa:BaseUrl</c>, and translates provider errors into the application's exception
/// vocabulary. State transitions the provider legitimately refuses surface as
/// <see cref="InvalidInvoiceOperationException"/>, not as integration faults.
/// </summary>
public class CyberSourceInvoiceProvider : IInvoiceProvider
{
    private const string HttpClientName = "cybersource-invoicing";
    private const int PageSize = 100;
    private const int MaxPages = 500;

    private readonly IOptions<VisaSettings> _settings;
    private readonly IHttpClientFactory _httpClientFactory;

    public CyberSourceInvoiceProvider(IOptions<VisaSettings> settings, IHttpClientFactory httpClientFactory)
    {
        _settings = settings;
        _httpClientFactory = httpClientFactory;
    }

    public Task<ProviderInvoiceResult> CreateDraftAsync(ProviderInvoiceDraft draft, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            var api = BuildApi();
            var request = new CreateInvoiceRequest(
                CustomerInformation: BuildCustomerInformation(draft),
                InvoiceInformation: new Invoicingv2invoicesInvoiceInformation(
                    Description: draft.Description,
                    DueDate: ToProviderDate(draft.DueDate),
                    SendImmediately: false,
                    AllowPartialPayments: false),
                OrderInformation: BuildOrderInformation(draft));

            var response = Require(await api.CreateInvoiceAsync(request));
            return new ProviderInvoiceResult(
                id: response.Id,
                status: response.Status,
                paymentLink: response.InvoiceInformation?.PaymentLink,
                dueDate: FromProviderDate(response.InvoiceInformation?.DueDate),
                amount: ParseAmount(response.OrderInformation?.AmountDetails?.TotalAmount),
                currencyCode: response.OrderInformation?.AmountDetails?.Currency,
                customerName: response.CustomerInformation?.Name,
                customerEmail: response.CustomerInformation?.Email,
                history: Array.Empty<ProviderInvoiceEvent>());
        });
    }

    public Task<ProviderInvoiceResult> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            var api = BuildApi();
            var response = Require(await api.GetInvoiceAsync(providerInvoiceId));
            return MapDetail(response);
        });
    }

    public Task<ProviderInvoiceResult> UpdateAsync(string providerInvoiceId, ProviderInvoiceDraft draft, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            var api = BuildApi();
            var request = new UpdateInvoiceRequest(
                CustomerInformation: BuildCustomerInformation(draft),
                InvoiceInformation: new Invoicingv2invoicesidInvoiceInformation(
                    Description: draft.Description,
                    DueDate: ToProviderDate(draft.DueDate),
                    SendImmediately: false,
                    AllowPartialPayments: false),
                OrderInformation: BuildOrderInformation(draft));

            await api.UpdateInvoiceAsync(providerInvoiceId, request);

            // Read the invoice back so the returned state is the provider's authoritative view.
            var response = Require(await api.GetInvoiceAsync(providerInvoiceId));
            return MapDetail(response);
        });
    }

    public Task<ProviderInvoiceResult> PublishAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            var api = BuildApi();
            await api.PerformPublishActionAsync(providerInvoiceId);

            // Publishing does not return the payment link; read it back once payable.
            var response = Require(await api.GetInvoiceAsync(providerInvoiceId));
            return MapDetail(response);
        });
    }

    public Task<ProviderInvoiceResult> CancelAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            var api = BuildApi();
            await api.PerformCancelActionAsync(providerInvoiceId);

            var response = Require(await api.GetInvoiceAsync(providerInvoiceId));
            return MapDetail(response);
        });
    }

    public Task<IReadOnlyList<ProviderInvoiceSummary>> ListCreatedBetweenAsync(
        DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync<IReadOnlyList<ProviderInvoiceSummary>>(async () =>
        {
            var api = BuildApi();
            var results = new List<ProviderInvoiceSummary>();

            var offset = 0;
            for (var page = 0; page < MaxPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var response = Require(await api.GetAllInvoicesAsync(offset, PageSize));
                var invoices = response.Invoices ?? new List<InvoicingV2InvoicesAllGet200ResponseInvoices>();
                if (invoices.Count == 0)
                {
                    break;
                }

                foreach (var invoice in invoices)
                {
                    var createdDate = ParseDateTimeOffset(invoice.CreatedDate);
                    if (createdDate is not null && (createdDate < fromInclusive || createdDate > toInclusive))
                    {
                        continue;
                    }

                    results.Add(new ProviderInvoiceSummary(
                        id: invoice.Id,
                        status: invoice.Status,
                        createdDate: createdDate,
                        amount: ParseAmount(invoice.OrderInformation?.AmountDetails?.TotalAmount),
                        currencyCode: invoice.OrderInformation?.AmountDetails?.Currency,
                        customerName: invoice.CustomerInformation?.Name));
                }

                offset += invoices.Count;
                var total = response.TotalInvoices ?? offset;
                if (invoices.Count < PageSize || offset >= total)
                {
                    break;
                }
            }

            return results;
        });
    }

    private InvoicesApi BuildApi()
    {
        var baseUrl = _settings.Value.BaseUrl;
        var runEnvironment = "apitest.cybersource.com";
        if (!string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            runEnvironment = baseUri.Host;
        }

        var merchantConfig = new Dictionary<string, string>
        {
            { "authenticationType", "jwt" },
            { "jwtKeyType", "SHARED_SECRET" },
            { "merchantID", _settings.Value.MerchantId },
            { "merchantKeyId", _settings.Value.KeyId },
            { "merchantsecretKey", _settings.Value.SecretKey },
            { "runEnvironment", runEnvironment },
            { "enableLog", "false" }
        };

        // Route every call through Visa:BaseUrl by handing the SDK an HttpClient whose pipeline
        // rewrites the request address to it (see VisaBaseUrlHandler).
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var configuration = new Configuration(merchConfigDictObj: merchantConfig, httpClient: httpClient);
        return new InvoicesApi(configuration);
    }

    private static Invoicingv2invoicesCustomerInformation BuildCustomerInformation(ProviderInvoiceDraft draft)
    {
        return new Invoicingv2invoicesCustomerInformation(
            Name: draft.CustomerName,
            Email: draft.CustomerEmail);
    }

    private static Invoicingv2invoicesOrderInformation BuildOrderInformation(ProviderInvoiceDraft draft)
    {
        var lineItems = draft.LineItems.Select(item => new Invoicingv2invoicesOrderInformationLineItems(
            ProductSku: item.ProductSku,
            ProductName: item.ProductName,
            Quantity: item.Quantity,
            UnitPrice: FormatAmount(item.UnitPrice),
            TotalAmount: FormatAmount(item.TotalAmount))).ToList();

        return new Invoicingv2invoicesOrderInformation(
            AmountDetails: new Invoicingv2invoicesOrderInformationAmountDetails(
                TotalAmount: FormatAmount(draft.TotalAmount),
                Currency: draft.CurrencyCode),
            LineItems: lineItems);
    }

    private static ProviderInvoiceResult MapDetail(InvoicingV2InvoicesGet200Response response)
    {
        var history = (response.InvoiceHistory ?? new List<InvoicingV2InvoicesGet200ResponseInvoiceHistory>())
            .Select(h => new ProviderInvoiceEvent(h.Event, ToDateTimeOffset(h.Date)))
            .ToList();

        return new ProviderInvoiceResult(
            id: response.Id,
            status: response.Status,
            paymentLink: response.InvoiceInformation?.PaymentLink,
            dueDate: FromProviderDate(response.InvoiceInformation?.DueDate),
            amount: ParseAmount(response.OrderInformation?.AmountDetails?.TotalAmount),
            currencyCode: response.OrderInformation?.AmountDetails?.Currency,
            customerName: response.CustomerInformation?.Name,
            customerEmail: response.CustomerInformation?.Email,
            history: history);
    }

    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (ApiException ex)
        {
            // A 4xx is the provider legitimately refusing given the state the bill is in (or a
            // bad request); anything else is a provider/transport failure.
            if (ex.ErrorCode >= 400 && ex.ErrorCode < 500)
            {
                throw new InvalidInvoiceOperationException(DescribeApiException(ex), ex);
            }

            throw new InvoiceProviderException(DescribeApiException(ex), ex);
        }
        catch (InvalidInvoiceOperationException)
        {
            throw;
        }
        catch (InvoiceProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvoiceProviderException($"The invoicing provider could not be reached: {ex.Message}", ex);
        }
    }

    private static string DescribeApiException(ApiException ex)
    {
        var detail = ex.ErrorContent?.ToString();
        if (!string.IsNullOrWhiteSpace(detail))
        {
            if (detail!.Length > 600)
            {
                detail = detail.Substring(0, 600);
            }

            return $"The invoicing provider rejected the request (HTTP {ex.ErrorCode}): {detail}";
        }

        return $"The invoicing provider rejected the request (HTTP {ex.ErrorCode}).";
    }

    private static T Require<T>(T? response) where T : class =>
        response ?? throw new InvoiceProviderException("The invoicing provider returned an empty response.");

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? amount) =>
        decimal.TryParse(amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static DateTime ToProviderDate(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static DateOnly? FromProviderDate(DateTime? date) =>
        date is null ? null : DateOnly.FromDateTime(date.Value);

    private static DateTimeOffset? ToDateTimeOffset(DateTime? date) =>
        date is null ? null : new DateTimeOffset(DateTime.SpecifyKind(date.Value, DateTimeKind.Utc));

    private static DateTimeOffset? ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
}
