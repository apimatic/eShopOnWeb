using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PayByBank;

/// <summary>Identity helpers: who the caller is comes from the JWT, never from the request.</summary>
public static class Caller
{
    public static string Id(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name
                   ?? user.FindFirstValue(ClaimTypes.Name)
                   ?? user.FindFirstValue("unique_name")
                   ?? user.FindFirstValue("name");
        if (string.IsNullOrWhiteSpace(name))
            throw new PayByBankException(HttpStatusCode.Unauthorized, "The token does not identify a user.");
        return Normalize(name);
    }

    public static string Normalize(string userId) => userId.Trim().ToLowerInvariant();
}

/// <summary>In-process mutual exclusion per key (a connection, a checkout, an order).</summary>
public static class KeyedLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    public static async Task<IDisposable> EnterAsync(string key)
    {
        var sem = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync().ConfigureAwait(false);
        return new Releaser(sem);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _sem;
        public Releaser(SemaphoreSlim sem) => _sem = sem;
        public void Dispose() => Interlocked.Exchange(ref _sem, null)?.Release();
    }
}

public static class Money
{
    /// <summary>Plaid amounts: decimal string with two digits of precision.</summary>
    public static string ToPlaid(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static decimal FromDouble(double amount) => Round((decimal)amount);

    public static decimal ParsePlaid(string? amount) =>
        decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0m;
}

public static class Fail
{
    public static PayByBankException BadRequest(string message) => new(HttpStatusCode.BadRequest, message);
    public static PayByBankException NotFound(string message) => new(HttpStatusCode.NotFound, message);
    public static PayByBankException Conflict(string message) => new(HttpStatusCode.Conflict, message);
}

public static class PlaidRetry
{
    private const int NotReadyAttempts = 8;
    private static readonly TimeSpan NotReadyDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// A product that was not requested at link time (or was only just requested) is initialised by Plaid
    /// on first use and answers PRODUCT_NOT_READY meanwhile; wait for it instead of failing.
    /// </summary>
    public static async Task<T> WhenReadyAsync<T>(Func<Task<T>> call)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await call();
            }
            catch (PlaidApiException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY" && attempt < NotReadyAttempts)
            {
                await Task.Delay(NotReadyDelay);
            }
        }
    }
}
