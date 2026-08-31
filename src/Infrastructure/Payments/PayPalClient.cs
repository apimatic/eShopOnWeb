using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _options.Validate();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public string Currency => _options.Currency.ToUpperInvariant();

    public async Task<PayPalSavedCard> SaveCardAsync(
        CardDetails card,
        string? customerId,
        string requestId,
        CancellationToken cancellationToken)
    {
        var setupPayload = new Dictionary<string, object?>
        {
            ["payment_source"] = new
            {
                card = CardPayload(card)
            }
        };
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            setupPayload["customer"] = new { id = customerId };
        }

        using var setup = await SendJsonAsync(
            HttpMethod.Post,
            "/v3/vault/setup-tokens",
            setupPayload,
            requestId,
            cancellationToken);

        ThrowIfPayerActionRequired(setup.RootElement);
        var setupId = RequiredString(setup.RootElement, "id");
        var setupStatus = OptionalString(setup.RootElement, "status");
        if (!string.Equals(setupStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalPayerActionRequiredException();
        }

        using var token = await SendJsonAsync(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            new
            {
                payment_source = new
                {
                    token = new { id = setupId, type = "SETUP_TOKEN" }
                }
            },
            DeriveRequestId(requestId, "token"),
            cancellationToken);

        var root = token.RootElement;
        var cardResult = root.GetProperty("payment_source").GetProperty("card");
        return new PayPalSavedCard(
            RequiredString(root, "id"),
            OptionalNestedString(root, "customer", "id"),
            RequiredString(cardResult, "brand"),
            RequiredString(cardResult, "last_digits"),
            RequiredString(cardResult, "expiry"),
            OptionalString(cardResult, "name"));
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}",
            null,
            null,
            cancellationToken);
    }

    public async Task<PayPalAuthorization> AuthorizeAsync(
        int orderId,
        Guid paymentReference,
        decimal amount,
        CardDetails? card,
        string? vaultId,
        string requestId,
        CancellationToken cancellationToken)
    {
        if ((card == null) == string.IsNullOrWhiteSpace(vaultId))
        {
            throw new ArgumentException("Supply exactly one card payment source.");
        }

        object paymentSource = card != null
            ? new { card = CardPayload(card) }
            : new { card = new { vault_id = vaultId } };
        var reference = $"eshop-{paymentReference:N}";

        using var document = await SendJsonAsync(
            HttpMethod.Post,
            "/v2/checkout/orders",
            new
            {
                intent = "AUTHORIZE",
                purchase_units = new[]
                {
                    new
                    {
                        reference_id = orderId.ToString(CultureInfo.InvariantCulture),
                        custom_id = paymentReference.ToString("N"),
                        invoice_id = reference,
                        amount = Money(amount)
                    }
                },
                payment_source = paymentSource
            },
            requestId,
            cancellationToken);

        var root = document.RootElement;
        ThrowIfPayerActionRequired(root);
        var authorization = root.GetProperty("purchase_units")[0]
            .GetProperty("payments").GetProperty("authorizations")[0];
        return ParseAuthorization(authorization, RequiredString(root, "id"));
    }

    public async Task<PayPalAuthorization> ReauthorizeAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { },
            requestId,
            cancellationToken);
        return ParseAuthorization(document.RootElement, string.Empty);
    }

    public async Task<PayPalCapture> CaptureAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new { amount = Money(amount), final_capture = true },
            requestId,
            cancellationToken);

        var root = document.RootElement;
        var breakdown = root.GetProperty("seller_receivable_breakdown");
        var returnedAmount = ParseMoney(root.GetProperty("amount"));
        var fee = ParseMoney(breakdown.GetProperty("paypal_fee"));
        var net = ParseMoney(breakdown.GetProperty("net_amount"));
        return new PayPalCapture(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            returnedAmount.Value,
            returnedAmount.Currency,
            fee.Value,
            net.Value,
            OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            JsonContent.Create(new { }, options: JsonOptions),
            requestId,
            cancellationToken);
    }

    public async Task<PayPalRefund> RefundAsync(
        string captureId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(
            HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new { amount = Money(amount), custom_id = requestId },
            requestId,
            cancellationToken);
        var root = document.RootElement;
        var returnedAmount = ParseMoney(root.GetProperty("amount"));
        return new PayPalRefund(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            returnedAmount.Value,
            returnedAmount.Currency,
            OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to)
        {
            throw new ArgumentException("The reconciliation start must be before its end.");
        }

        var results = new List<PayPalTransaction>();
        var cursor = from.ToUniversalTime();
        var end = to.ToUniversalTime();

        while (cursor < end)
        {
            var windowEnd = cursor.AddDays(31) < end ? cursor.AddDays(31) : end;
            var page = 1;
            var totalPages = 1;
            do
            {
                var path = "/v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(FormatDate(cursor))}" +
                    $"&end_date={Uri.EscapeDataString(FormatDate(windowEnd))}" +
                    "&fields=transaction_info&balance_affecting_records_only=N&page_size=500" +
                    $"&page={page}";
                using var document = await SendJsonAsync(HttpMethod.Get, path, null, null, cancellationToken);
                var root = document.RootElement;
                if (root.TryGetProperty("transaction_details", out var details))
                {
                    results.AddRange(details.EnumerateArray().Select(ParseTransaction));
                }

                totalPages = OptionalInt(root, "total_pages") ?? 1;
                page++;
            }
            while (page <= totalPages);

            cursor = windowEnd;
        }

        return results
            .GroupBy(x => new { x.TransactionId, x.EventCode, x.UpdatedAt })
            .Select(x => x.First())
            .ToList();
    }

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken)
    {
        HttpContent? content = body == null ? null : JsonContent.Create(body, options: JsonOptions);
        using var response = await SendAsync(method, path, content, requestId, cancellationToken);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        string? requestId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, BuildUri(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrWhiteSpace(requestId))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }

            if (content != null)
            {
                var bytes = await content.ReadAsByteArrayAsync(cancellationToken);
                request.Content = new ByteArrayContent(bytes);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
            {
                response.Dispose();
                _accessToken = null;
                continue;
            }

            if ((response.StatusCode == HttpStatusCode.RequestTimeout ||
                 response.StatusCode == HttpStatusCode.TooManyRequests ||
                 (int)response.StatusCode >= 500) && attempt < 3)
            {
                response.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var exception = await CreateExceptionAsync(response, cancellationToken);
                response.Dispose();
                throw exception;
            }

            return response;
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken != null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken != null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateExceptionAsync(response, cancellationToken);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            _accessToken = RequiredString(document.RootElement, "access_token");
            var expiresIn = OptionalInt(document.RootElement, "expires_in") ?? 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<PayPalApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string name = "UPSTREAM_ERROR";
        string? issue = null;
        string? debugId = null;
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            name = OptionalString(document.RootElement, "name") ?? OptionalString(document.RootElement, "error") ?? name;
            debugId = OptionalString(document.RootElement, "debug_id");
            if (document.RootElement.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                issue = details.EnumerateArray().Select(x => OptionalString(x, "issue")).FirstOrDefault(x => x != null);
            }
        }
        catch (JsonException)
        {
            // Intentionally do not expose or log an unstructured upstream body.
        }

        return new PayPalApiException(response.StatusCode, name, issue, debugId);
    }

    private Uri BuildUri(string path) => new($"{_options.ResolveBaseUrl()}/{path.TrimStart('/')}", UriKind.Absolute);

    private object Money(decimal amount) => new
    {
        currency_code = Currency,
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static object CardPayload(CardDetails card) => new
    {
        number = card.Number,
        expiry = card.Expiry,
        security_code = card.SecurityCode,
        name = card.Name,
        billing_address = new
        {
            address_line_1 = card.AddressLine1,
            address_line_2 = card.AddressLine2,
            admin_area_2 = card.City,
            admin_area_1 = card.State,
            postal_code = card.PostalCode,
            country_code = card.CountryCode.ToUpperInvariant()
        }
    };

    private static PayPalAuthorization ParseAuthorization(JsonElement element, string payPalOrderId)
    {
        var money = ParseMoney(element.GetProperty("amount"));
        return new PayPalAuthorization(
            payPalOrderId,
            RequiredString(element, "id"),
            RequiredString(element, "status"),
            money.Value,
            money.Currency,
            OptionalDate(element, "create_time") ?? DateTimeOffset.UtcNow,
            OptionalDate(element, "expiration_time"));
    }

    private static PayPalTransaction ParseTransaction(JsonElement element)
    {
        var info = element.GetProperty("transaction_info");
        var amount = ParseMoney(info.GetProperty("transaction_amount"));
        decimal? fee = null;
        if (info.TryGetProperty("fee_amount", out var feeElement))
        {
            fee = ParseMoney(feeElement).Value;
        }

        return new PayPalTransaction(
            RequiredString(info, "transaction_id"),
            OptionalString(info, "paypal_reference_id"),
            OptionalString(info, "paypal_reference_id_type"),
            OptionalString(info, "transaction_event_code") ?? string.Empty,
            OptionalString(info, "transaction_status") ?? string.Empty,
            OptionalDate(info, "transaction_initiation_date"),
            OptionalDate(info, "transaction_updated_date"),
            amount.Value,
            amount.Currency,
            fee,
            OptionalString(info, "invoice_id"),
            OptionalString(info, "custom_field"));
    }

    private static (decimal Value, string Currency) ParseMoney(JsonElement money) =>
        (decimal.Parse(RequiredString(money, "value"), NumberStyles.Number, CultureInfo.InvariantCulture),
         RequiredString(money, "currency_code"));

    private static void ThrowIfPayerActionRequired(JsonElement root)
    {
        if (string.Equals(OptionalString(root, "status"), "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            (root.TryGetProperty("links", out var links) && links.EnumerateArray().Any(link =>
                string.Equals(OptionalString(link, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(OptionalString(link, "rel"), "approve", StringComparison.OrdinalIgnoreCase))))
        {
            throw new PayPalPayerActionRequiredException();
        }
    }

    private static string RequiredString(JsonElement element, string property) =>
        element.GetProperty(property).GetString() ?? throw new JsonException($"PayPal response omitted {property}.");

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? OptionalNestedString(JsonElement element, string parent, string property) =>
        element.TryGetProperty(parent, out var nested) ? OptionalString(nested, property) : null;

    private static int? OptionalInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : null;

    private static DateTimeOffset? OptionalDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetDateTimeOffset(out var result) ? result : null;

    private static string FormatDate(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string DeriveRequestId(string requestId, string suffix)
    {
        var value = $"{requestId}-{suffix}";
        return value.Length <= 108 ? value : value[..108];
    }
}
