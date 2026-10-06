using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Serializes work for a given key. Used to stop a double-clicked subscribe from racing itself.
/// </summary>
public interface IKeyedLock
{
    Task<T> RunAsync<T>(string key, Func<Task<T>> action, CancellationToken cancellationToken = default);
}
