using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using ThePlaidApi;
using ThePlaidApi.Core;
using ThePlaidApi.Core.Hooks;

namespace Microsoft.eShopWeb.PublicApi.Plaid.Services;

/// <summary>Everything the pay-by-bank services share when they talk to Plaid.</summary>
public sealed class PlaidAccess
{
    private readonly IDataProtector _protector;
    private readonly ILogger<PlaidAccess> _logger;
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    public PlaidAccess(ThePlaidApiClient client, IDataProtectionProvider dataProtection, ILogger<PlaidAccess> logger)
    {
        Client = client;
        _protector = dataProtection.CreateProtector("Plaid.AccessToken.v1");
        _logger = logger;
    }

    public ThePlaidApiClient Client { get; }

    public string Protect(string accessToken) => _protector.Protect(accessToken);

    public string Unprotect(string protectedToken) => _protector.Unprotect(protectedToken);

    /// <summary>An opaque, stable id for Plaid. Plaid refuses identifiers that look like an email address.</summary>
    public static string ClientUserId(string userName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userName.Trim().ToLowerInvariant()));
        return "u_" + Convert.ToHexString(hash).Substring(0, 32).ToLowerInvariant();
    }

    /// <summary>Serialises work on one connection / payment within this process.</summary>
    public static async Task<IDisposable> LockAsync(Guid id, CancellationToken ct)
    {
        var gate = Locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }

    /// <summary>
    /// Plaid prepares a product the first time it is asked for (for example transactions on an item that was linked
    /// for something else) and answers PRODUCT_NOT_READY meanwhile. Wait for it, within a bound.
    /// </summary>
    public async Task<T> WhenProductReady<T>(Func<Task<T>> call, CancellationToken ct, int maxAttempts = 40)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await call();
            }
            catch (Exception e) when (attempt < maxAttempts && PlaidFailure.HasCode(e, "PRODUCT_NOT_READY"))
            {
                _logger.LogInformation("Plaid product not ready yet (attempt {Attempt}); waiting.", attempt);
                await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
            }
        }
    }

    /// <summary>
    /// The SDK writes the start and end dates of /transactions/get as full timestamps, which Plaid refuses; it
    /// wants YYYY-MM-DD. This per-call hook rewrites the two fields on the way out.
    /// </summary>
    public static readonly RequestOptions DateOnlyBody = new()
    {
        Hooks = new List<SdkHook>
        {
            SdkHook.OnRequest(async (HttpRequestMessage request, HookContext _, CancellationToken ct) =>
            {
                if (request.Content is null) return;
                var body = await request.Content.ReadAsStringAsync();
                if (JsonNode.Parse(body) is not JsonObject json) return;
                foreach (var key in new[] { "start_date", "end_date" })
                {
                    if (json[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 10)
                        json[key] = s.Substring(0, 10);
                }
                request.Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json");
            })
        }
    };

    public static string Money(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
}
