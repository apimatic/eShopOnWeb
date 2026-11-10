using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using ThePlaidApi.Core.ErrorResponse;
using ThePlaidApi.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Banking;

/// <summary>A Plaid call that failed, with Plaid's own error code.</summary>
public class PlaidCallException : BankingException
{
    public string? ErrorCode { get; }

    public PlaidCallException(int statusCode, string reason, string? errorCode, Exception? inner = null)
        : base(statusCode, reason, inner)
    {
        ErrorCode = errorCode;
    }
}

public static class PlaidCall
{
    private sealed record PlaidError(
        [property: System.Text.Json.Serialization.JsonPropertyName("error_type")] string? ErrorType,
        [property: System.Text.Json.Serialization.JsonPropertyName("error_code")] string? ErrorCode,
        [property: System.Text.Json.Serialization.JsonPropertyName("error_message")] string? ErrorMessage,
        [property: System.Text.Json.Serialization.JsonPropertyName("display_message")] string? DisplayMessage);

    /// <summary>
    /// Runs a Plaid call and turns its failures into <see cref="PlaidCallException"/>s the API can answer with.
    /// </summary>
    public static async Task<T> Run<T>(Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (ApiException<RawError> ex)
        {
            PlaidError? error = null;
            try { error = ex.Error.ReadAsJson<PlaidError>(); } catch (System.Text.Json.JsonException) { }

            var code = error?.ErrorCode;
            var message = error?.DisplayMessage ?? error?.ErrorMessage ?? ex.Message;
            var reason = code is null ? $"Plaid: {message}" : $"Plaid: {message} ({code})";
            throw new PlaidCallException(MapStatus((int)ex.StatusCode, error?.ErrorType, code), reason, code, ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw new PlaidCallException(504, "Plaid did not answer in time.", null, ex);
        }
        catch (SdkConnectionException ex)
        {
            throw new PlaidCallException(502, "Plaid could not be reached.", null, ex);
        }
        catch (SdkException ex) when (ex is not ApiException)
        {
            throw new PlaidCallException(502, $"Plaid call failed: {ex.Message}", null, ex);
        }
    }

    public static async Task Run(Func<Task> call) => await Run(async () => { await call().ConfigureAwait(false); return 0; }).ConfigureAwait(false);

    private static int MapStatus(int httpStatus, string? type, string? code)
    {
        if (httpStatus == 429 || code == "RATE_LIMIT_EXCEEDED") return 429;
        if (code is "PRODUCT_NOT_READY") return 503;
        if (code is "ITEM_LOGIN_REQUIRED" or "INVALID_ACCESS_TOKEN" or "ITEM_NOT_FOUND" or "ACCESS_NOT_GRANTED") return 409;
        if (type is "INVALID_REQUEST" or "INVALID_INPUT") return 400;
        if (type == "TRANSFER_ERROR") return 422;
        if (type == "ITEM_ERROR") return 422;
        if (httpStatus >= 500) return 502;
        if (httpStatus >= 400) return 422;
        return 502;
    }
}

/// <summary>Protects Plaid access tokens at rest.</summary>
public class AccessTokenProtector
{
    private readonly IDataProtector _protector;

    public AccessTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("eShopOnWeb.Plaid.AccessToken.v1");
    }

    public string Protect(string accessToken) => _protector.Protect(accessToken);

    public string Unprotect(BankConnection connection)
    {
        try
        {
            return _protector.Unprotect(connection.ProtectedAccessToken);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw new BankingException(409, "This bank connection can't be used any more; reconnect the bank.", ex);
        }
    }
}

/// <summary>Per-key async locks (one operation at a time per connection / checkout).</summary>
public class ConnectionLocks
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;
        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}

public static class Money
{
    public static string ToPlaid(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>Plaid reports amounts as JSON doubles; convert without picking up binary noise.</summary>
    public static decimal FromDouble(double value) => Math.Round((decimal)value, 4, MidpointRounding.AwayFromZero);
}
