using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A process-wide mutual-exclusion lock keyed by an arbitrary string. Used to serialise concurrent
/// payment operations on the same order so a double-click cannot authorise or capture twice.
/// </summary>
public interface IKeyedLock
{
    Task<IDisposable> AcquireAsync(string key, CancellationToken ct = default);
}
