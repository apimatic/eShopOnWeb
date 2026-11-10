using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Banking;

/// <summary>Settings of the shop's own pay-by-bank rules (the <c>BankPayment:</c> section).</summary>
public class BankPaymentSettings
{
    public const string CONFIG_NAME = "BankPayment";

    /// <summary>What the shopper pays on top of the order total for express (same-day) delivery of the money.</summary>
    public decimal ExpressFee { get; set; } = 2.50m;

    /// <summary>The most a same-day ACH transfer may move, fee included. That is the ACH network's own limit.</summary>
    public decimal SameDayLimit { get; set; } = 1_000_000m;
}

public static class BankingServiceCollectionExtensions
{
    public static IServiceCollection AddBanking(this IServiceCollection services)
    {
        services.AddSingleton<KeyedLocks>();
        services.AddSingleton<AccessTokenProtector>();
        services.AddScoped<BankPickerService>();
        services.AddScoped<BankConnectionService>();
        services.AddScoped<BankPaymentService>();
        services.AddScoped<BankActivityService>();
        services.AddScoped<PortfolioService>();
        return services;
    }
}

/// <summary>Plaid access tokens are credentials to a shopper's bank; they are stored encrypted.</summary>
public class AccessTokenProtector
{
    private readonly IDataProtector _protector;

    public AccessTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("eShopOnWeb.PublicApi.Banking.AccessToken");
    }

    public string Protect(string accessToken) => _protector.Protect(accessToken);

    public string Unprotect(BankConnection connection)
    {
        try
        {
            return _protector.Unprotect(connection.ProtectedAccessToken);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new RequestFailedException(409, "The saved bank connection can no longer be read; please connect the bank again.");
        }
    }
}

/// <summary>One-at-a-time execution per key, so two requests cannot, say, confirm the same checkout twice.</summary>
public class KeyedLocks
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string key)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}

internal static class Money
{
    /// <summary>Plaid wants amounts as strings with two decimals.</summary>
    public static string ToPlaid(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static decimal FromPlaid(double amount) => Math.Round((decimal)amount, 2, MidpointRounding.AwayFromZero);

    public static bool HasCents(decimal amount) => decimal.Round(amount, 2) != amount;
}

internal static class BankDates
{
    private static readonly TimeZoneInfo UsEastern = FindEastern();

    private static TimeZoneInfo FindEastern()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }

    /// <summary>Today on the US banking calendar.</summary>
    public static DateTime Today() => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, UsEastern).Date;

    public static DateTime OnOrAfterBusinessDay(DateTime date)
    {
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            date = date.AddDays(1);
        }

        return date;
    }

    public static DateTime AddBusinessDays(DateTime date, int days)
    {
        date = OnOrAfterBusinessDay(date);
        while (days > 0)
        {
            date = OnOrAfterBusinessDay(date.AddDays(1));
            days--;
        }

        return date;
    }

    /// <summary>A date as Plaid reports it, as a plain calendar date whatever the offset it was parsed with.</summary>
    public static DateTime CalendarDate(DateTimeOffset value) => new(value.Year, value.Month, value.Day);
}
