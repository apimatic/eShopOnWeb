using System;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Serializes work for a given idempotency key across the whole process, so a check-then-send
/// under one key cannot race a concurrent repeat of the same key into a second message.
/// Registered as a singleton (the domain repositories are per-request, this outlives them).
/// </summary>
public interface IResendIdempotencyGuard
{
    Task<T> RunExclusivelyAsync<T>(string key, Func<Task<T>> action);
}
