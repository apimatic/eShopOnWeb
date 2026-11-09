using System;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Serialises work on one key (an order, a shopper) inside this process.</summary>
public class KeyedLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string key)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}

/// <summary>Resolves the calling shopper from the JWT and makes sure the account still exists.</summary>
public class ShopperResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public ShopperResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>The shopper's canonical user name (their e-mail), used as the buyer id everywhere.</summary>
    public async Task<ApplicationUser> RequireAsync(ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ApiProblemException(401, "The token does not identify a shopper.");
        }

        var user = await _userManager.FindByNameAsync(name);
        if (user is null)
        {
            throw new ApiProblemException(401, "This shop account no longer exists.");
        }

        return user;
    }
}
