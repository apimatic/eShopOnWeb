using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>Subscription billing on top of Maxio (system of record). Every failure the app cannot recover from is a <see cref="BillingException"/>.</summary>
public partial class BillingService
{
    private const int PER_PAGE = 200;
    private const int MAX_PAGES = 50;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<BillingService> _logger;

    public BillingService(MaxioAdvancedBillingClient client, CatalogContext db, UserManager<ApplicationUser> users, ILogger<BillingService> logger)
    {
        _client = client;
        _db = db;
        _users = users;
        _logger = logger;
    }

    // ---------------------------------------------------------------- error translation

    private static BillingException Map(HttpStatusCode status, string? detail)
    {
        var code = (int)status;
        var reason = string.IsNullOrWhiteSpace(detail) ? "The billing provider refused the request." : detail;
        return code switch
        {
            404 => new BillingException(404, "Not found."),
            401 or 403 => new BillingException(502, "The billing provider rejected the application's credentials."),
            429 => new BillingException(503, "The billing provider is busy; try again later."),
            >= 400 and < 500 => new BillingException(code == 400 ? 422 : code, reason),
            _ => new BillingException(502, "The billing provider failed to process the request."),
        };
    }

    private static string Flatten(IReadOnlyDictionary<string, object> errors) =>
        string.Join("; ", errors.Select(kv => kv.Key + ": " + string.Join(", ",
            (kv.Value is JsonElement { ValueKind: JsonValueKind.Array } arr
                ? arr.EnumerateArray().Select(x => x.ToString())
                : new[] { kv.Value?.ToString() ?? string.Empty }).Distinct())));

    private static string? Join(IEnumerable<string>? errors) => errors == null ? null : string.Join("; ", errors);

    private static string? Describe(ApiException ex)
    {
        static string? L(ErrorListResponse1? e) => Join(e?.Errors);
        string? d = null;
        switch (ex)
        {
            case ApiException<CreateProductError> e: d = e.Error.TryGetErrorListResponse1(out var a) ? L(a) : null; break;
            case ApiException<CreateProductFamilyError> e: d = e.Error.TryGetErrorListResponse1(out var b) ? L(b) : null; break;
            case ApiException<CreateSubscriptionError> e: d = e.Error.TryGetErrorListResponse1(out var c) ? L(c) : null; break;
            case ApiException<UpdateSubscriptionError> e: d = e.Error.TryGetErrorListResponse1(out var f) ? L(f) : null; break;
            case ApiException<PauseSubscriptionError> e: d = e.Error.TryGetErrorListResponse1(out var g) ? L(g) : null; break;
            case ApiException<ResumeSubscriptionError> e: d = e.Error.TryGetErrorListResponse1(out var h) ? L(h) : null; break;
            case ApiException<ReactivateSubscriptionError> e: d = e.Error.TryGetErrorListResponse1(out var i) ? L(i) : null; break;
            case ApiException<MigrateSubscriptionProductError> e: d = e.Error.TryGetErrorListResponse1(out var j) ? L(j) : null; break;
            case ApiException<CreateCustomerError> e:
                d = e.Error.TryGetCustomerErrorResponse1(out var k) && k.Errors != null ? JsonSerializer.Serialize(k.Errors) : null; break;
            case ApiException<CreateProductCurrencyPricesError> e:
                d = e.Error.TryGetErrorArrayMapResponse1(out var m) && m.Errors != null ? Flatten(m.Errors) : null; break;
            case ApiException<UpdateProductCurrencyPricesError> e:
                d = e.Error.TryGetErrorArrayMapResponse1(out var n) && n.Errors != null ? Flatten(n.Errors) : null; break;
        }
        return Trim(d);
    }

    private async Task<T> Call<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ApiException<RawError> ex)
        {
            string? body = null;
            try { body = ex.Error.ReadAsString(); } catch (Exception) { /* body unreadable */ }
            throw Map(ex.StatusCode, ex.StatusCode == HttpStatusCode.NotFound ? null : Trim(body));
        }
        catch (ResponseDeserializationException ex)
        {
            if ((int)ex.StatusCode is >= 200 and < 300)
            {
                throw new BillingException(502, "The billing provider returned a response that could not be processed.", ex);
            }
            throw Map(ex.StatusCode, null);
        }
        catch (ApiException ex)
        {
            throw Map(ex.StatusCode, Describe(ex));
        }
        catch (SdkTimeoutException ex)
        {
            throw new BillingException(504, "The billing provider did not answer in time.", ex);
        }
        catch (SdkConnectionException ex)
        {
            throw new BillingException(502, "The billing provider is unreachable.", ex);
        }
        catch (AuthSchemeException ex)
        {
            throw new BillingException(502, "The billing provider credentials are missing or rejected.", ex);
        }
    }

    private static string? Trim(string? s) => s is { Length: > 300 } ? s[..300] : s;

    private static CancellationTokenSource Budgeted(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Budget);
        return cts;
    }
}
