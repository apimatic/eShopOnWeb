using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>Holds a value that is reloaded after a while; a failed reload keeps serving the last good value.</summary>
internal sealed class RefreshingValue<T> where T : class
{
    private readonly TimeSpan _ttl;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private T? _value;
    private DateTimeOffset _loadedAt;

    public RefreshingValue(TimeSpan ttl) => _ttl = ttl;

    public async Task<T> GetAsync(Func<Task<T>> load)
    {
        if (_value is { } v && DateTimeOffset.UtcNow - _loadedAt < _ttl) return v;

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_value is { } v2 && DateTimeOffset.UtcNow - _loadedAt < _ttl) return v2;
            try
            {
                _value = await load().ConfigureAwait(false);
                _loadedAt = DateTimeOffset.UtcNow;
            }
            catch when (_value is not null)
            {
                // keep the stale copy, but retry soon rather than hammering the service
                _loadedAt = DateTimeOffset.UtcNow - _ttl + TimeSpan.FromSeconds(30);
            }
            return _value!;
        }
        finally
        {
            _lock.Release();
        }
    }
}

/// <summary>Serialises work per key (e.g. per studio client) so a retried request cannot race its twin.</summary>
internal sealed class KeyedLock
{
    private readonly SemaphoreSlim[] _stripes;

    public KeyedLock(int stripes = 128)
    {
        _stripes = new SemaphoreSlim[stripes];
        for (var i = 0; i < stripes; i++) _stripes[i] = new SemaphoreSlim(1, 1);
    }

    public async Task<IDisposable> AcquireAsync(string key)
    {
        var stripe = _stripes[(uint)StringComparer.OrdinalIgnoreCase.GetHashCode(key) % (uint)_stripes.Length];
        await stripe.WaitAsync().ConfigureAwait(false);
        return new Releaser(stripe);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;
        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}

/// <summary>Details the studio asks of every new client, given with a shopper's first booking.</summary>
public class NewClientDetails
{
    public int? HomeLocationId { get; set; }
    public string? StreetAddress { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? DateOfBirth { get; set; }
    public string? MobilePhone { get; set; }
    public string? ReferredBy { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public class ClassDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int? LocationId { get; set; }
    public string? LocationName { get; set; }
    public string? Trainer { get; set; }
}

public class SlotDto
{
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
}

public class TrainerDto
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public bool Active { get; set; }
    public string? Email { get; set; }
    public string? MobilePhone { get; set; }
    public string? HomePhone { get; set; }
    public string? WorkPhone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public bool TeachesClasses { get; set; }
    public bool TakesAppointments { get; set; }
}

public class BookingDto
{
    public long BookingId { get; set; }
    public string Kind { get; set; } = "";
    public string? Name { get; set; }
    public int? ClassId { get; set; }
    public int? SessionTypeId { get; set; }
    public long? TrainerId { get; set; }
    public int? LocationId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
}

public class CancelledBookingDto
{
    public long BookingId { get; set; }
    public string Status { get; set; } = "cancelled";
}
