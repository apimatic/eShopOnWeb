using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using StaxFattMerchantApi;
using StaxFattMerchantApi.Core.ErrorResponse;
using StaxFattMerchantApi.Core.Exceptions;
using StaxFattMerchantApi.Core.Hooks;
using StaxFattMerchantApi.Models;
using CustomerRequests = StaxFattMerchantApi.Requests.Customer;
using InvoiceScheduleRequests = StaxFattMerchantApi.Requests.InvoiceSchedule;
using PaymentMethodRequests = StaxFattMerchantApi.Requests.PaymentMethod;
using TeamRequests = StaxFattMerchantApi.Requests.Team;
using TransactionRequests = StaxFattMerchantApi.Requests.Transaction;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>An error that carries the HTTP status the shop API answers with.</summary>
public class PaymentApiException : Exception
{
    public int StatusCode { get; }
    public PaymentApiException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}

/// <summary>A failure reported by (or while reaching) Stax.</summary>
public class StaxException : Exception
{
    /// <summary>HTTP status Stax answered with, or 0 when Stax could not be reached.</summary>
    public int UpstreamStatus { get; }
    public bool IsTransient => UpstreamStatus == 0 || UpstreamStatus == 408 || UpstreamStatus == 429 || UpstreamStatus >= 500;
    public StaxException(int upstreamStatus, string message) : base(message) => UpstreamStatus = upstreamStatus;

    /// <summary>Maps a Stax failure to the status the shop answers with.</summary>
    public PaymentApiException ToApi(string action)
    {
        if (IsTransient) return new PaymentApiException(503, $"Stax is unavailable, {action} could not be completed: {Message}");
        return UpstreamStatus switch
        {
            404 => new PaymentApiException(404, $"{action}: {Message}"),
            401 or 403 => new PaymentApiException(502, $"Stax refused the shop's credentials: {Message}"),
            _ => new PaymentApiException(422, $"{action}: {Message}"),
        };
    }
}

public record StaxTransaction(string Id, bool Success, string Status, decimal Total, decimal TotalRefunded, bool IsVoided,
    DateTimeOffset? SettledAt, string? Message, string Method);

public record StaxPaymentMethod(string Id, string CustomerId, string Method, string? CardType, string? Last4, string? Expiration,
    string? BankName, string? BankType, string? BankHolderType, string? PersonName, string? BinType, bool Prepaid,
    DateTimeOffset? CreatedAt, DateTimeOffset? DeletedAt)
{
    public bool IsCard => Method == "card";
}

public record StaxCustomer(string Id, string? Email, string? Company);

public record StaxSchedule(string Id, bool Active, string? NextRunAt);

/// <summary>
/// The only place the shop talks to Stax. Everything goes through the Stax SDK; this class adds typed parsing of the
/// SDK's untyped (<see cref="object"/>) responses and captures the bodies of the operations the SDK declares void.
/// </summary>
public class StaxGateway
{
    private readonly StaxFattMerchantApiClient? _client;
    private readonly StaxFattMerchantApiClient? _platformClient;
    private static readonly AsyncLocal<StringBuilder?> _captured = new();

    public bool IsConfigured => _client != null;
    public bool IsPlatformConfigured => _platformClient != null;

    public StaxGateway(IOptions<StaxSettings> settings, IHttpClientFactory httpClientFactory)
    {
        var s = settings.Value;
        var apiKey = Pick(s.ApiKey, "STAX_API_KEY");
        var platformKey = Pick(s.PlatformApiKey, "STAX_PLATFORM_API_KEY");
        if (apiKey != null) _client = Create(httpClientFactory, apiKey, s.BaseUrl);
        if (platformKey != null) _platformClient = Create(httpClientFactory, platformKey, s.BaseUrl);
    }

    private static string? Pick(string? configured, string envName)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var env = Environment.GetEnvironmentVariable(envName);
        return string.IsNullOrWhiteSpace(env) ? null : env;
    }

    private static StaxFattMerchantApiClient Create(IHttpClientFactory factory, string key, string? baseUrl)
    {
        var options = new StaxFattMerchantApiClientOptions
        {
            BearerAuth = key,
            Hooks = [CaptureHook, QueryHook],
        };
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            options.Server.Default.Production.BaseUrl = baseUrl;
            options.Server.Default.Environment2.BaseUrl = baseUrl;
        }
        return new StaxFattMerchantApiClient(factory.CreateClient(), options);
    }

    // The SDK discards the bodies of operations it declares void (e.g. creating an invoice schedule) although Stax answers
    // with the created resource. This hook keeps a copy of the body so the id can be read.
    private static readonly SdkHook CaptureHook = SdkHook.OnResponse(async (response, _, ct) =>
    {
        var sink = _captured.Value;
        if (sink == null || response.Content == null) return;
        await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        lock (sink) { sink.Clear(); sink.Append(text); }
    });

    // Stax expects keyword searches as an array (keywords[]=...), the SDK sends a scalar.
    private static readonly SdkHook QueryHook = SdkHook.OnRequest((request, _) =>
    {
        var uri = request.RequestUri;
        if (uri == null || uri.Query.Length == 0 || !uri.Query.Contains("keywords=")) return;
        var builder = new UriBuilder(uri) { Query = uri.Query.TrimStart('?').Replace("keywords=", "keywords%5B%5D=") };
        request.RequestUri = builder.Uri;
    });

    private StaxFattMerchantApiClient Client => _client ??
        throw new PaymentApiException(503, "Stax is not configured: set Stax:ApiKey (user-secrets or the STAX_API_KEY environment variable).");

    // ---- plumbing -------------------------------------------------------------------------------------------------

    private static async Task<T> Run<T>(Func<Task<T>> call)
    {
        try { return await call().ConfigureAwait(false); }
        catch (ApiException ex) { throw Translate(ex); }
        catch (SdkConnectionException ex) { throw new StaxException(0, ex.Message); }
        catch (HttpRequestException ex) { throw new StaxException(0, ex.Message); }
    }

    private static async Task Run(Func<Task> call) => await Run(async () => { await call().ConfigureAwait(false); return 0; }).ConfigureAwait(false);

    private static async Task<JsonElement> RunCapturing(Func<Task> call)
    {
        var sink = new StringBuilder();
        _captured.Value = sink;
        try { await Run(call).ConfigureAwait(false); }
        finally { _captured.Value = null; }
        var text = sink.ToString();
        if (text.Length == 0) throw new StaxException(0, "Stax returned no body");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static StaxException Translate(ApiException ex)
    {
        var status = (int)ex.StatusCode;
        var message = $"Stax answered HTTP {status}";
        if (ex is ApiException<RawError> raw)
        {
            message = ExtractMessage(raw.Error.ReadAsString()) ?? message;
        }
        return new StaxException(status, message);
    }

    private static string? ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var parts = new List<string>();
            Collect(doc.RootElement, null, parts);
            return parts.Count == 0 ? null : string.Join("; ", parts.Distinct());
        }
        catch (JsonException) { return body.Length > 300 ? body[..300] : body; }
    }

    private static void Collect(JsonElement e, string? name, List<string> parts)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                var plain = string.IsNullOrEmpty(name) || int.TryParse(name, out _) || name == "message" || name == "error";
                parts.Add(plain ? e.GetString()! : $"{name}: {e.GetString()}");
                break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    // {"error":{"0":"...","message":"..."}} repeats the same text; keep only "message" in that case.
                    if (p.Name == "error" && p.Value.ValueKind == JsonValueKind.Object &&
                        p.Value.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(m.GetString()!);
                        continue;
                    }
                    Collect(p.Value, p.Name, parts);
                }
                break;
            case JsonValueKind.Array:
                foreach (var i in e.EnumerateArray()) Collect(i, name, parts);
                break;
        }
    }

    private static JsonElement ToElement(object o) => o is JsonElement je ? je : JsonSerializer.SerializeToElement(o);

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())
            : null;

    private static decimal Dec(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return 0m;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal();
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        return 0m;
    }

    private static bool Bool(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return false;
        return v.ValueKind == JsonValueKind.True
            || (v.ValueKind == JsonValueKind.Number && v.GetDouble() != 0)
            || (v.ValueKind == JsonValueKind.String && (v.GetString() == "1" || v.GetString() == "true"));
    }

    private static DateTimeOffset? Time(JsonElement e, string name)
    {
        var s = Str(e, name);
        if (s != null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
            return new DateTimeOffset(d, TimeSpan.Zero);
        return null;
    }

    // ---- customers ------------------------------------------------------------------------------------------------

    public Task<StaxCustomer> FindOrCreateCustomer(string email, string firstName, string? lastName, string? company, CancellationToken ct = default) =>
        Run(async () =>
        {
            var body = new Dictionary<string, object?> { ["email"] = email, ["firstname"] = firstName };
            if (!string.IsNullOrWhiteSpace(lastName)) body["lastname"] = lastName;
            if (!string.IsNullOrWhiteSpace(company)) body["company"] = company;
            var r = ToElement(await Client.Customer.PostFindOrCreateCustomer(new CustomerRequests.PostFindOrCreateCustomerRequest { Body = body }, null, ct));
            return new StaxCustomer(Str(r, "id")!, Str(r, "email"), Str(r, "company"));
        });

    /// <summary>Looks a customer up by email without creating one.</summary>
    public Task<StaxCustomer?> FindCustomerByEmail(string email, CancellationToken ct = default) =>
        Run(async () =>
        {
            var r = ToElement(await Client.Customer.GetCustomers(new CustomerRequests.GetCustomersRequest { Keywords = email, PerPage = 100 }, null, ct));
            if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("data", out var data))
            {
                foreach (var c in data.EnumerateArray())
                {
                    if (string.Equals(Str(c, "email"), email, StringComparison.OrdinalIgnoreCase) && Str(c, "deleted_at") == null)
                        return new StaxCustomer(Str(c, "id")!, Str(c, "email"), Str(c, "company"));
                }
            }
            return null;
        });

    public Task SetCustomerCompany(string customerId, string company, CancellationToken ct = default) =>
        Run(() => Client.Customer.PutCustomer(new CustomerRequests.PutCustomerRequest
        {
            Id = Guid.Parse(customerId),
            Body = new Dictionary<string, object?> { ["company"] = company },
        }, null, ct));

    /// <summary>Blanks the personal data held on a customer, then deletes it.</summary>
    public Task EraseCustomer(string customerId, CancellationToken ct = default) =>
        Run(async () =>
        {
            var id = Guid.Parse(customerId);
            await Client.Customer.PutCustomer(new CustomerRequests.PutCustomerRequest
            {
                Id = id,
                Body = new Dictionary<string, object?>
                {
                    ["firstname"] = "Erased", ["lastname"] = "Erased", ["email"] = "", ["company"] = "", ["phone"] = "",
                    ["address_1"] = "", ["address_2"] = "", ["address_city"] = "", ["address_state"] = "", ["address_zip"] = "",
                    ["address_country"] = "", ["notes"] = "", ["reference"] = "",
                },
            }, null, ct).ConfigureAwait(false);
            await Client.Customer.DeleteCustomer(new CustomerRequests.DeleteCustomerRequest { Id = id }, null, ct).ConfigureAwait(false);
        });

    // ---- payment methods ------------------------------------------------------------------------------------------

    private static StaxPaymentMethod ParsePm(JsonElement e)
    {
        var meta = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("meta", out var m) ? m : default;
        return new StaxPaymentMethod(
            Str(e, "id")!, Str(e, "customer_id") ?? "", Str(e, "method") ?? "", Str(e, "card_type"), Str(e, "card_last_four"),
            Str(e, "card_exp"), Str(e, "bank_name"), Str(e, "bank_type"), Str(e, "bank_holder_type"), Str(e, "person_name"),
            Str(e, "bin_type"), meta.ValueKind == JsonValueKind.Object && Bool(meta, "prepaid"),
            Time(e, "created_at"), Time(e, "deleted_at"));
    }

    public Task<StaxPaymentMethod> CreatePaymentMethod(IDictionary<string, object?> body, CancellationToken ct = default) =>
        Run(async () => ParsePm(ToElement(await Client.PaymentMethod.PostPaymentMethod(new PaymentMethodRequests.PostPaymentMethodRequest { Body = body }, null, ct))));

    /// <summary>Fetches a payment method by id; removed ones are returned too (with <c>DeletedAt</c> set). Null when Stax does not know the id.</summary>
    public async Task<StaxPaymentMethod?> GetPaymentMethod(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var guid)) return null;
        try
        {
            return await Run(async () => ParsePm(ToElement(await Client.PaymentMethod.GetPaymentMethod(new PaymentMethodRequests.GetPaymentMethodRequest { Id = guid }, null, ct)))).ConfigureAwait(false);
        }
        catch (StaxException ex) when (ex.UpstreamStatus is 404 or 422) { return null; }
    }

    /// <summary>The customer's active (not removed) payment methods.</summary>
    public Task<List<StaxPaymentMethod>> ListPaymentMethods(string customerId, CancellationToken ct = default) =>
        Run(async () =>
        {
            var r = await Client.Customer.GetCustomerPaymentMethods(new CustomerRequests.GetCustomerPaymentMethodsRequest { Id = Guid.Parse(customerId) }, null, ct);
            return r.Select(x => ParsePm(ToElement(x))).Where(p => p.DeletedAt == null).ToList();
        });

    public Task ScrubPaymentMethod(string id, CancellationToken ct = default) =>
        Run(() => Client.PaymentMethod.PutPaymentMethod(new PaymentMethodRequests.PutPaymentMethodRequest
        {
            Id = Guid.Parse(id),
            Body = new Dictionary<string, object?> { ["person_name"] = "Erased", ["address_1"] = "", ["address_2"] = "", ["address_city"] = "", ["address_state"] = "", ["address_zip"] = "" },
        }, null, ct));

    public Task DeletePaymentMethod(string id, CancellationToken ct = default) =>
        Run(() => Client.PaymentMethod.DeletePaymentMethod(new PaymentMethodRequests.DeletePaymentMethodRequest { Id = Guid.Parse(id) }, null, ct));

    // ---- transactions ---------------------------------------------------------------------------------------------

    private static StaxTransaction ParseTx(JsonElement e)
    {
        var success = Bool(e, "success") && !string.Equals(Str(e, "status"), "FAILED", StringComparison.OrdinalIgnoreCase);
        return new StaxTransaction(Str(e, "id")!, success, Str(e, "status") ?? "", Dec(e, "total"), Dec(e, "total_refunded"),
            Bool(e, "is_voided"), Time(e, "settled_at"), Str(e, "message") ?? Str(e, "error_description"), Str(e, "method") ?? "");
    }

    public Task<StaxTransaction> Charge(string paymentMethodId, decimal total, IDictionary<string, object?> meta, string idempotencyId, CancellationToken ct = default) =>
        Run(async () => ParseTx(ToElement(await Client.Charge.PostCharge(new StaxFattMerchantApi.Requests.Charge.PostChargeRequest
        {
            Body = new Dictionary<string, object?>
            {
                ["payment_method_id"] = paymentMethodId,
                ["total"] = total,
                ["meta"] = meta,
                ["idempotency_id"] = idempotencyId,
            },
        }, null, ct))));

    public Task<StaxTransaction> GetTransaction(string id, CancellationToken ct = default) =>
        Run(async () => ParseTx(ToElement(await Client.Transaction.GetTransaction(new TransactionRequests.GetTransactionRequest { Id = Guid.Parse(id) }, null, ct))));

    /// <summary>Refunds the whole amount of a transaction.</summary>
    public Task RefundInFull(string id, CancellationToken ct = default) =>
        Run(async () => await Client.Transaction.PostRefund(new TransactionRequests.PostRefundRequest { Id = Guid.Parse(id), Body = new PostRefundRequestBodyJson() }, null, ct));

    /// <summary>Payment-method ids that Stax holds transactions for under this customer (finds methods removed outside the shop).</summary>
    public Task<HashSet<string>> PaymentMethodIdsFromTransactions(string customerId, string email, CancellationToken ct = default) =>
        Run(async () =>
        {
            var ids = new HashSet<string>();
            for (var page = 1; page <= 5; page++)
            {
                var r = ToElement(await Client.Transaction.GetTransactions(new TransactionRequests.GetTransactionsRequest { Keywords = email, Page = page, PerPage = 100 }, null, ct));
                if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("data", out var data)) break;
                foreach (var t in data.EnumerateArray())
                {
                    if (Str(t, "customer_id") == customerId && Str(t, "payment_method_id") is { Length: > 0 } pm) ids.Add(pm);
                }
                var last = r.TryGetProperty("last_page", out var lp) && lp.ValueKind == JsonValueKind.Number ? lp.GetInt32() : page;
                if (page >= last) break;
            }
            return ids;
        });

    // ---- merchant -------------------------------------------------------------------------------------------------

    /// <summary>The surcharge rate (percent) Stax reports for the shop's merchant, when surcharging is enabled there.</summary>
    public Task<decimal?> GetCardSurchargePercent(CancellationToken ct = default) =>
        Run(async () =>
        {
            var r = ToElement(await Client.Self.GetSelf(null, ct));
            if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("merchant", out var m) && Bool(m, "is_surcharge_enabled"))
            {
                var rate = Dec(m, "cnp_credit_surcharge_rate");
                if (rate > 0) return (decimal?)(rate <= 1 ? rate * 100 : rate);
            }
            return (decimal?)null;
        });

    // ---- invoice schedules ----------------------------------------------------------------------------------------

    public Task<StaxSchedule> CreateInvoiceSchedule(string customerId, decimal total, string url, string rule, CancellationToken ct = default) =>
        Run(async () =>
        {
            var e = await RunCapturing(() => Client.InvoiceSchedule.PostInvoiceSchedule(new InvoiceScheduleRequests.PostInvoiceScheduleRequest
            {
                Body = new Dictionary<string, object?> { ["customer_id"] = customerId, ["total"] = total, ["url"] = url, ["rule"] = rule },
            }, null, ct)).ConfigureAwait(false);
            return new StaxSchedule(Str(e, "id") ?? throw new StaxException(0, "Stax did not return a schedule id"), Bool(e, "active"), Str(e, "next_run_at"));
        });

    public Task<StaxSchedule> SetInvoiceScheduleActive(string id, bool active, CancellationToken ct = default) =>
        Run(async () =>
        {
            var e = await RunCapturing(() => Client.InvoiceSchedule.PutInvoiceSchedule(new InvoiceScheduleRequests.PutInvoiceScheduleRequest
            {
                Id = Guid.Parse(id),
                Body = new Dictionary<string, object?> { ["active"] = active },
            }, null, ct)).ConfigureAwait(false);
            return new StaxSchedule(Str(e, "id") ?? id, Bool(e, "active"), Str(e, "next_run_at"));
        });

    public Task DeleteInvoiceSchedule(string id, CancellationToken ct = default) =>
        Run(() => Client.InvoiceSchedule.DeleteInvoiceSchedule(new InvoiceScheduleRequests.DeleteInvoiceScheduleRequest { Id = Guid.Parse(id) }, null, ct));

    // ---- marketplace ----------------------------------------------------------------------------------------------

    public Task<(string Id, string? Status)> CreateMerchant(IDictionary<string, object?> body, CancellationToken ct = default)
    {
        var client = _platformClient ?? throw new PaymentApiException(409,
            "Seller onboarding is not available: the shop has no Stax platform key (Stax:PlatformApiKey / STAX_PLATFORM_API_KEY).");
        return Run(async () =>
        {
            var r = ToElement(await client.Team.PostMerchant(new TeamRequests.PostMerchantRequest { Body = body }, null, ct));
            return (Str(r, "id") ?? throw new StaxException(0, "Stax did not return a merchant id"), Str(r, "status"));
        });
    }
}
