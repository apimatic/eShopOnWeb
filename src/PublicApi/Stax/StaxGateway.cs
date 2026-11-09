using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using StaxFattMerchantApi;
using StaxFattMerchantApi.Core;
using StaxFattMerchantApi.Core.ErrorResponse;
using StaxFattMerchantApi.Core.Exceptions;
using StaxFattMerchantApi.Core.Hooks;
using StaxFattMerchantApi.Models;
using StaxFattMerchantApi.Requests.Charge;
using StaxFattMerchantApi.Requests.Customer;
using StaxFattMerchantApi.Requests.InvoiceSchedule;
using StaxFattMerchantApi.Requests.MerchantAdmin;
using StaxFattMerchantApi.Requests.PaymentMethod;
using StaxFattMerchantApi.Requests.Transaction;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>An error answered by (or while talking to) Stax, already translated into an API status.</summary>
public class StaxException : ApiProblemException
{
    /// <summary>The HTTP status Stax answered with (0 when Stax could not be reached).</summary>
    public int StaxStatus { get; }

    public StaxException(int statusCode, string message, int staxStatus) : base(statusCode, message)
    {
        StaxStatus = staxStatus;
    }
}

/// <summary>
/// The only place that talks to Stax. All calls go through the Stax SDK (<c>sdk/stax</c>), except the
/// surcharge preview, which the SDK does not offer.
/// </summary>
public class StaxGateway
{
    private const string DefaultBaseUrl = "https://apiprod.fattlabs.com";

    private readonly IOptions<StaxOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly object _gate = new();
    private StaxFattMerchantApiClient? _client;
    private StaxFattMerchantApiClient? _platformClient;

    public StaxGateway(IOptions<StaxOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
    }

    public bool HasPlatformKey => !string.IsNullOrWhiteSpace(PlatformKey);

    private string? ApiKey => First(_options.Value.ApiKey, Environment.GetEnvironmentVariable("STAX_API_KEY"));

    private string? PlatformKey => First(_options.Value.PlatformApiKey, Environment.GetEnvironmentVariable("STAX_PLATFORM_API_KEY"));

    private string? BaseUrl => string.IsNullOrWhiteSpace(_options.Value.BaseUrl) ? null : _options.Value.BaseUrl;

    private static string? First(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private StaxFattMerchantApiClient Create(string key)
    {
        var options = new StaxFattMerchantApiClientOptions { BearerAuth = key };
        if (BaseUrl is { } baseUrl)
        {
            options.Server.Default.Production.BaseUrl = baseUrl;
        }

        return new StaxFattMerchantApiClient(_httpClientFactory.CreateClient(), options);
    }

    private StaxFattMerchantApiClient Client
    {
        get
        {
            lock (_gate)
            {
                if (_client is null)
                {
                    var key = ApiKey ?? throw new ApiProblemException(424,
                        "Payments are not configured: the Stax API key (Stax:ApiKey) is missing.");
                    _client = Create(key);
                }

                return _client;
            }
        }
    }

    private StaxFattMerchantApiClient PlatformClient
    {
        get
        {
            lock (_gate)
            {
                if (_platformClient is null)
                {
                    var key = PlatformKey ?? throw new ApiProblemException(409,
                        "Seller onboarding is not available: the shop has no Stax platform key (Stax:PlatformApiKey).");
                    _platformClient = Create(key);
                }

                return _platformClient;
            }
        }
    }

    // ---- customers ------------------------------------------------------------------------------

    public Task<JsonElement> FindOrCreateCustomerAsync(string email, string firstName, string lastName,
        string? company, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["email"] = email,
            ["firstname"] = firstName,
            ["lastname"] = lastName,
        };
        if (!string.IsNullOrWhiteSpace(company))
        {
            body["company"] = company;
        }

        return Guard(async () => StaxJson.ToElement(await Client.Customer
            .PostFindOrCreateCustomer(new PostFindOrCreateCustomerRequest { Body = body }, null, ct)));
    }

    public Task<JsonElement> GetCustomerAsync(Guid id, CancellationToken ct = default) =>
        Guard(async () => StaxJson.ToElement(await Client.Customer
            .GetCustomer(new GetCustomerRequest { Id = id }, null, ct)));

    public Task UpdateCustomerAsync(Guid id, IDictionary<string, object?> fields, CancellationToken ct = default) =>
        Guard(async () =>
        {
            await Client.Customer.PutCustomer(new PutCustomerRequest { Id = id, Body = fields }, null, ct);
            return true;
        });

    public Task DeleteCustomerAsync(Guid id, CancellationToken ct = default) =>
        Guard(async () =>
        {
            await Client.Customer.DeleteCustomer(new DeleteCustomerRequest { Id = id }, null, ct);
            return true;
        });

    // ---- payment methods ------------------------------------------------------------------------

    public Task<JsonElement> CreatePaymentMethodAsync(IDictionary<string, object?> body, CancellationToken ct = default) =>
        Guard(async () => StaxJson.ToElement(await Client.PaymentMethod
            .PostPaymentMethod(new PostPaymentMethodRequest { Body = body }, null, ct)));

    /// <summary>Also answers for removed payment methods (they carry <c>deleted_at</c>).</summary>
    public Task<JsonElement> GetPaymentMethodAsync(Guid id, CancellationToken ct = default) =>
        Guard(async () => StaxJson.ToElement(await Client.PaymentMethod
            .GetPaymentMethod(new GetPaymentMethodRequest { Id = id }, null, ct)));

    /// <summary>The customer's payment methods that have not been removed.</summary>
    public Task<List<JsonElement>> ListCustomerPaymentMethodsAsync(Guid customerId, CancellationToken ct = default) =>
        Guard(async () =>
        {
            var result = await Client.Customer
                .GetCustomerPaymentMethods(new GetCustomerPaymentMethodsRequest { Id = customerId }, null, ct);
            var list = new List<JsonElement>();
            foreach (var item in result)
            {
                list.Add(StaxJson.ToElement(item));
            }

            return list;
        });

    public Task DeletePaymentMethodAsync(Guid id, CancellationToken ct = default) =>
        Guard(async () =>
        {
            await Client.PaymentMethod.DeletePaymentMethod(new DeletePaymentMethodRequest { Id = id }, null, ct);
            return true;
        });

    // ---- charges, refunds -----------------------------------------------------------------------

    /// <summary>Asks Stax which surcharge (card processing fee) it applies to a charge. The SDK has no operation for it.</summary>
    public async Task<JsonElement> ReviewSurchargeAsync(Guid paymentMethodId, decimal total, CancellationToken ct = default)
    {
        var key = ApiKey ?? throw new ApiProblemException(424,
            "Payments are not configured: the Stax API key (Stax:ApiKey) is missing.");
        var url = string.Concat(BaseUrl ?? DefaultBaseUrl, "/surcharge/review?payment_method_id=",
            paymentMethodId.ToString("D"), "&total=", total.ToString("0.00", CultureInfo.InvariantCulture));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _httpClientFactory.CreateClient().SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw Translate((int)response.StatusCode, text);
            }

            return JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (HttpRequestException ex)
        {
            throw new StaxException(502, "Stax could not be reached: " + ex.Message, 0);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new StaxException(504, "Stax did not answer in time.", 0);
        }
    }

    public Task<JsonElement> ChargeAsync(Guid paymentMethodId, decimal total, IDictionary<string, object?> meta,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["payment_method_id"] = paymentMethodId,
            ["total"] = total,
            ["pre_auth"] = false,
            ["meta"] = meta,
        };
        return Guard(async () => StaxJson.ToElement(await Client.Charge
            .PostCharge(new PostChargeRequest { Body = body }, null, ct)));
    }

    public Task<JsonElement> GetTransactionAsync(Guid id, CancellationToken ct = default) =>
        Guard(async () => StaxJson.ToElement(await Client.Transaction
            .GetTransaction(new GetTransactionRequest { Id = id }, null, ct)));

    public Task<JsonElement> RefundAsync(Guid transactionId, decimal total, CancellationToken ct = default) =>
        Guard(async () => StaxJson.ToElement(await Client.Transaction.PostRefund(new PostRefundRequest
        {
            Id = transactionId,
            Body = new PostRefundRequestBodyJson { Total = (double)total },
        }, null, ct)));

    // ---- invoice schedules ----------------------------------------------------------------------

    public Task<JsonElement> CreateInvoiceScheduleAsync(IDictionary<string, object?> body, CancellationToken ct = default) =>
        Guard(() => CaptureAsync(options => Client.InvoiceSchedule
            .PostInvoiceSchedule(new PostInvoiceScheduleRequest { Body = body }, options, ct)));

    public Task<JsonElement> SetInvoiceScheduleActiveAsync(Guid id, bool active, CancellationToken ct = default) =>
        Guard(() => CaptureAsync(options => Client.InvoiceSchedule.PutInvoiceSchedule(new PutInvoiceScheduleRequest
        {
            Id = id,
            Body = new Dictionary<string, object?> { ["active"] = active },
        }, options, ct)));

    // ---- marketplace ----------------------------------------------------------------------------

    public Task<JsonElement> CreateMerchantAsync(IDictionary<string, object?> body, CancellationToken ct = default)
    {
        var client = PlatformClient; // fails with a clear 4xx when the shop has no platform key
        return Guard(() => CaptureAsync(options => client.MerchantAdmin
            .AdminPostMerchant(new AdminPostMerchantRequest { Body = body }, options, ct)));
    }

    // ---- plumbing -------------------------------------------------------------------------------

    /// <summary>
    /// A few SDK operations are typed as returning nothing although Stax answers with the created or
    /// updated resource. A response hook captures the body for them.
    /// </summary>
    private static async Task<JsonElement> CaptureAsync(Func<RequestOptions, Task> call)
    {
        string? captured = null;
        var hook = SdkHook.OnResponse(async (response, _, _) =>
        {
            if (response.IsSuccessStatusCode && response.Content is not null)
            {
                await response.Content.LoadIntoBufferAsync();
                captured = await response.Content.ReadAsStringAsync();
            }
        });
        await call(new RequestOptions { Hooks = [hook] });
        if (string.IsNullOrWhiteSpace(captured))
        {
            return default;
        }

        return JsonDocument.Parse(captured).RootElement.Clone();
    }

    private static async Task<T> Guard<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ApiProblemException)
        {
            throw;
        }
        catch (ApiException<RawError> ex)
        {
            throw Translate((int)ex.StatusCode, ex.Error.ReadAsString());
        }
        catch (SdkTimeoutException)
        {
            throw new StaxException(504, "Stax did not answer in time.", 0);
        }
        catch (SdkException ex)
        {
            throw new StaxException(502, "Stax could not be reached: " + ex.Message, 0);
        }
    }

    private static async Task Guard(Func<Task<bool>> call) => await Guard<bool>(call);

    public static StaxException Translate(int staxStatus, string body)
    {
        var message = ExtractMessage(body) ?? "Stax rejected the request.";
        var status = staxStatus switch
        {
            404 => 404,
            401 or 403 => 502,
            429 => 503,
            >= 500 => 502,
            _ => 422,
        };
        if (staxStatus is 401 or 403)
        {
            message = "Stax refused the shop's credentials: " + message;
        }

        return new StaxException(status, message, staxStatus);
    }

    private static void Collect(JsonElement value, string name, List<string> parts)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                parts.Add(value.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(name is "error" or "errors" or "message" ? item.GetString()! : $"{name}: {item.GetString()}");
                    }
                    else
                    {
                        Collect(item, name, parts);
                    }
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    Collect(property.Value, name is "error" or "errors" ? property.Name : name + "." + property.Name, parts);
                }

                break;
        }
    }

    /// <summary>Stax answers either <c>{"message": ...}</c>, <c>{"error": [...]}</c> or <c>{"field": ["reason"]}</c>.</summary>
    public static string? ExtractMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return root.ValueKind == JsonValueKind.String ? root.GetString() : null;
            }

            var parts = new List<string>();
            foreach (var property in root.EnumerateObject())
            {
                Collect(property.Value, property.Name, parts);
            }

            if (parts.Count == 0)
            {
                return null;
            }

            var sb = new StringBuilder();
            foreach (var part in new HashSet<string>(parts))
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(part);
            }

            return sb.ToString();
        }
        catch (JsonException)
        {
            return body.Length > 300 ? body[..300] : body;
        }
    }
}
