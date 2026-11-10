using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using MindbodyPublicApi.Core.Exceptions;
using MindbodyPublicApi.Requests.AppointmentApi;
using MindbodyPublicApi.Requests.ClassApi;
using MindbodyPublicApi.Requests.ClientApi;
using MindbodyPublicApi.Requests.SiteApi;
using MindbodyPublicApi.Requests.StaffApi;
using Mb = MindbodyPublicApi.Models;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// The studio booking rules of eShopOnWeb, on top of Mindbody.
/// </summary>
/// <remarks>
/// Design notes:
/// <list type="bullet">
/// <item>The mobile app re-sends a request when it gets no answer in time and treats anything but a success as a
/// failure. Mindbody happily books the same class twice and rejects a repeated appointment, so every booking first
/// looks for the booking it would create and, when it exists, answers with it. Cancelling an already-cancelled
/// booking of the caller succeeds for the same reason.</item>
/// <item>All times are converted from the studio's wall-clock times to date-times with the studio's offset.</item>
/// <item>Nothing here keeps state that outlives a few minutes, apart from short-lived markers of cancelled bookings.</item>
/// </list>
/// </remarks>
public sealed class StudioService
{
    public const int DefaultClassWindowDays = 7;
    public const int MaxRangeDays = 62;
    private const int PageSize = 200;
    private static readonly TimeSpan ReferenceDataLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CancellationMarkerLifetime = TimeSpan.FromDays(14);
    private static readonly Regex HasOffset = new(@"(Z|[+-]\d{2}(:?\d{2})?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly MindbodyGateway _gateway;
    private readonly StudioClock _clock;
    private readonly IMemoryCache _cache;
    private readonly KeyedAsyncLock _locks = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _cancelled = new();

    public StudioService(MindbodyGateway gateway, StudioClock clock, IMemoryCache cache)
    {
        _gateway = gateway;
        _clock = clock;
        _cache = cache;
    }

    // ------------------------------------------------------------------------------------------------
    // Classes
    // ------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<StudioClassDto>> ListClassesAsync(string? from, string? to, CancellationToken ct)
    {
        var zone = await _clock.GetZoneAsync(ct);
        var today = await _clock.TodayAsync(ct);

        var fromDay = ParseDate(from, "from");
        var toDay = ParseDate(to, "to");
        var first = fromDay ?? today;
        var last = toDay ?? (fromDay is null ? today : fromDay.Value).AddDays(DefaultClassWindowDays);
        ValidateRange(first, last);

        var windowStart = StudioClock.StartOfDay(first, zone);
        var windowEnd = StudioClock.StartOfDay(last.AddDays(1), zone);
        var now = _clock.UtcNow;

        var classes = await PageAsync(async (offset, limit) =>
        {
            var response = await _gateway.CallAsync((client, token) => client.ClassApi.GetClasses(new GetClassesOperationRequest
            {
                // Mindbody only looks at the studio calendar day of these two instants, so use midday to stay clear of any offset edge.
                RequestStartDateTime = StudioClock.StartOfDay(first, zone).AddHours(12),
                RequestEndDateTime = StudioClock.StartOfDay(last, zone).AddHours(12),
                RequestLimit = limit,
                RequestOffset = offset,
                Authorization = token,
                SiteId = _gateway.SiteId,
            }, cancellationToken: ct), ct);
            return (response.Classes, response.PaginationResponse?.TotalResults);
        }, ct);

        var result = new List<StudioClassDto>();
        foreach (var c in classes)
        {
            if (c.Id is null || c.StartDateTime is null || c.EndDateTime is null)
            {
                continue;
            }

            if (c.IsCanceled == true || c.Active == false)
            {
                continue;
            }

            var start = StudioClock.ToInstant(c.StartDateTime.Value.DateTime, zone);
            var end = StudioClock.ToInstant(c.EndDateTime.Value.DateTime, zone);
            // Only classes that start inside the requested days, and that are not over yet.
            if (start < windowStart || start >= windowEnd || end <= now)
            {
                continue;
            }

            result.Add(ToClassDto(c, start, end, zone));
        }

        return result
            .GroupBy(c => c.Id)
            .Select(g => g.First())
            .OrderBy(c => c.StartTime.UtcDateTime)
            .ThenBy(c => c.Id)
            .ToList();
    }

    private static StudioClassDto ToClassDto(Mb.Class c, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone) => new()
    {
        Id = c.Id!.Value,
        Name = c.ClassDescription?.Name,
        Description = c.ClassDescription?.Description,
        StartTime = StudioClock.ToStudioTime(start, zone),
        EndTime = StudioClock.ToStudioTime(end, zone),
        LocationId = c.Location?.Id,
        LocationName = c.Location?.Name,
        Trainer = c.Staff?.DisplayName ?? c.Staff?.Name,
        Capacity = c.MaxCapacity,
        Booked = c.TotalBooked,
    };

    // ------------------------------------------------------------------------------------------------
    // Session times
    // ------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<StudioSlotDto>> ListSlotsAsync(int? sessionTypeId, string? from, string? to, CancellationToken ct)
    {
        if (sessionTypeId is null or <= 0)
        {
            throw StudioException.BadRequest("sessionTypeId is required and must be a positive number.");
        }

        var fromDay = ParseDate(from, "from") ?? throw StudioException.BadRequest("from is required (a date such as 2026-11-12).");
        var toDay = ParseDate(to, "to") ?? throw StudioException.BadRequest("to is required (a date such as 2026-11-14).");
        ValidateRange(fromDay, toDay);

        var type = await GetAppointmentSessionTypeAsync(sessionTypeId.Value, ct);
        var zone = await _clock.GetZoneAsync(ct);
        var times = await GetSessionStartTimesAsync(sessionTypeId.Value, ct);
        if (times.Count == 0)
        {
            return Array.Empty<StudioSlotDto>();
        }

        var length = type.DefaultTimeLength
            ?? throw new StudioException(422, $"Session type {sessionTypeId} has no default length, so its end times are unknown.");
        var now = _clock.UtcNow;
        var slots = new List<StudioSlotDto>();
        for (var day = fromDay; day <= toDay; day = day.AddDays(1))
        {
            foreach (var time in times)
            {
                var wall = day.ToDateTime(time);
                if (zone.IsInvalidTime(wall))
                {
                    continue; // this clock time does not exist on a spring-forward day
                }

                var start = StudioClock.ToInstant(wall, zone);
                if (start <= now)
                {
                    continue;
                }

                slots.Add(new StudioSlotDto
                {
                    StartTime = StudioClock.ToStudioTime(start, zone),
                    EndTime = StudioClock.ToStudioTime(start.AddMinutes(length), zone),
                });
            }
        }

        return slots.OrderBy(s => s.StartTime.UtcDateTime).ToList();
    }

    private async Task<Mb.SessionType> GetAppointmentSessionTypeAsync(int sessionTypeId, CancellationToken ct)
    {
        var types = await CachedAsync("session-types", async () =>
        {
            var all = await PageAsync(async (offset, limit) =>
            {
                var response = await _gateway.CallAsync((client, token) => client.SiteApi.GetSessionTypes(new GetSessionTypesOperationRequest
                {
                    RequestLimit = limit,
                    RequestOffset = offset,
                    Authorization = token,
                    SiteId = _gateway.SiteId,
                }, cancellationToken: ct), ct);
                return (response.SessionTypes, response.PaginationResponse?.TotalResults);
            }, ct);
            return all.Where(t => t.Id is not null).ToDictionary(t => t.Id!.Value);
        });

        if (!types.TryGetValue(sessionTypeId, out var type))
        {
            throw StudioException.NotFound($"Session type {sessionTypeId} does not exist.");
        }

        if (type.Type != MindbodyPublicApi.Models.Enums.Type1.Appointment)
        {
            throw StudioException.BadRequest($"Session type {sessionTypeId} is not a one-to-one session.");
        }

        return type;
    }

    private async Task<IReadOnlyList<TimeOnly>> GetSessionStartTimesAsync(int sessionTypeId, CancellationToken ct)
    {
        var response = await _gateway.CallAsync((client, token) => client.AppointmentApi.GetActiveSessionTimes(new GetActiveSessionTimesOperationRequest
        {
            RequestSessionTypeIds = [sessionTypeId],
            RequestLimit = PageSize,
            Authorization = token,
            SiteId = _gateway.SiteId,
        }, cancellationToken: ct), ct);

        // The per-type list is the full set (the top-level list is paginated and merges types).
        var raw = response.SessionTypeActiveSessionTimes?
            .FirstOrDefault(g => g.SessionTypeId == sessionTypeId)?.ActiveSessionTimes
            ?? Array.Empty<string>();

        var times = new SortedSet<TimeOnly>();
        foreach (var text in raw)
        {
            if (TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                times.Add(time);
            }
        }

        return times.ToList();
    }

    // ------------------------------------------------------------------------------------------------
    // Booking a class
    // ------------------------------------------------------------------------------------------------

    public async Task<StudioBookingDto> BookClassAsync(StudioCaller caller, int? classId, StudioClientProfile profile, CancellationToken ct)
    {
        var id = RequireId(classId, "classId");
        var zone = await _clock.GetZoneAsync(ct);
        var cls = await FindClassAsync(id, zone, ct);
        EnsureClassCanBeBooked(cls, zone, forShopper: true);

        var clientId = await GetOrCreateClientAsync(caller, profile, ct);
        return await BookClassForClientAsync(clientId, cls, zone, forShopper: true, ct);
    }

    public async Task<StudioBookingDto> BookClassForShopperAsync(string? shopper, int? classId, CancellationToken ct)
    {
        var id = RequireId(classId, "classId");
        if (string.IsNullOrWhiteSpace(shopper))
        {
            throw StudioException.BadRequest("shopper is required (the shopper's e-mail address).");
        }

        var zone = await _clock.GetZoneAsync(ct);
        var cls = await FindClassAsync(id, zone, ct);
        EnsureClassCanBeBooked(cls, zone, forShopper: false);

        var clientId = await FindClientIdAsync(shopper.Trim(), ct)
            ?? throw StudioException.NotFound($"'{shopper}' is not a client of the studio yet; the shopper has to make a first booking in the app.");
        return await BookClassForClientAsync(clientId, cls, zone, forShopper: false, ct);
    }

    // Calls that create something upstream are deliberately not tied to the caller's connection (the SDK's own
    // timeout bounds them): when the app gives up waiting and asks again, the first request must have finished,
    // so the repeat finds its result instead of racing it.
    private async Task<StudioBookingDto> BookClassForClientAsync(string clientId, Mb.Class cls, TimeZoneInfo zone, bool forShopper, CancellationToken ct)
    {
        using var _ = await _locks.LockAsync("client:" + clientId, ct);

        var classId = cls.Id!.Value;
        var start = StudioClock.ToInstant(cls.StartDateTime!.Value.DateTime, zone);
        var end = StudioClock.ToInstant(cls.EndDateTime!.Value.DateTime, zone);

        // A repeated request must not book the place twice: hand back the booking that already exists.
        var existing = (await GetClientVisitsAsync(clientId, start.AddDays(-1), end.AddDays(1), ct))
            .FirstOrDefault(v => v.ClassId == classId && v.Id is not null && IsActive(v));
        if (existing is not null)
        {
            return ToClassBooking(existing.Id!.Value, clientId, cls, start, end, zone);
        }

        EnsureHasRoom(cls, forShopper);

        var response = await _gateway.CallAsync((client, token) => client.ClassApi.AddClientToClass(new AddClientToClassOperationRequest
        {
            SiteId = _gateway.SiteId,
            Authorization = token,
            Body = new Mb.AddClientToClassRequest { ClientId = clientId, ClassId = classId },
        }, cancellationToken: CancellationToken.None), CancellationToken.None);

        var visitId = response.Visit?.Id;
        if (visitId is null or <= 0)
        {
            throw new StudioException(502, "The studio system did not confirm the booking. Please try again.");
        }

        return ToClassBooking(visitId.Value, clientId, cls, start, end, zone);
    }

    private static StudioBookingDto ToClassBooking(long bookingId, string clientId, Mb.Class cls, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone) => new()
    {
        BookingId = bookingId,
        Type = "class",
        ClientId = clientId,
        ClassId = cls.Id,
        LocationId = cls.Location?.Id,
        Name = cls.ClassDescription?.Name,
        TrainerId = cls.Staff?.Id,
        StartTime = StudioClock.ToStudioTime(start, zone),
        EndTime = StudioClock.ToStudioTime(end, zone),
    };

    private async Task<Mb.Class> FindClassAsync(int classId, TimeZoneInfo zone, CancellationToken ct)
    {
        var today = await _clock.TodayAsync(ct);
        // Mindbody only finds a class by id inside a date window, so look from yesterday to far ahead.
        var from = StudioClock.StartOfDay(today.AddDays(-1), zone);
        var until = StudioClock.StartOfDay(today.AddYears(4), zone);

        var response = await _gateway.CallAsync((client, token) => client.ClassApi.GetClasses(new GetClassesOperationRequest
        {
            RequestClassIds = [classId],
            RequestStartDateTime = from,
            RequestEndDateTime = until,
            Authorization = token,
            SiteId = _gateway.SiteId,
        }, cancellationToken: ct), ct);

        var cls = response.Classes?.FirstOrDefault(c => c.Id == classId);
        if (cls is null || cls.StartDateTime is null || cls.EndDateTime is null)
        {
            throw StudioException.NotFound($"Class {classId} is not in the studio's timetable.");
        }

        return cls;
    }

    private void EnsureClassCanBeBooked(Mb.Class cls, TimeZoneInfo zone, bool forShopper)
    {
        if (cls.IsCanceled == true)
        {
            throw StudioException.Conflict("This class has been cancelled.");
        }

        var now = _clock.UtcNow;
        var start = StudioClock.ToInstant(cls.StartDateTime!.Value.DateTime, zone);
        var end = StudioClock.ToInstant(cls.EndDateTime!.Value.DateTime, zone);
        if (forShopper)
        {
            if (cls.Active == false)
            {
                throw StudioException.Conflict("This class is not open for booking.");
            }

            if (start <= now)
            {
                throw StudioException.Conflict("This class has already started.");
            }

            if (cls.IsAvailable == false)
            {
                throw StudioException.Conflict("This class is not open for booking.");
            }

            var window = cls.BookingWindow?.StartDateTime;
            if (window is not null && StudioClock.ToInstant(window.Value.DateTime, zone) > now)
            {
                throw StudioException.Conflict("Booking for this class has not opened yet.");
            }
        }
        else if (end <= now)
        {
            throw StudioException.Conflict("This class is already over.");
        }
    }

    private static void EnsureHasRoom(Mb.Class cls, bool forShopper)
    {
        if (cls.MaxCapacity is { } max && cls.TotalBooked is { } booked && booked >= max)
        {
            throw StudioException.Conflict("This class is full.");
        }

        if (forShopper && cls.WebCapacity is { } webMax && cls.WebBooked is { } webBooked && webBooked >= webMax)
        {
            throw StudioException.Conflict("This class is full.");
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Booking a one-to-one session
    // ------------------------------------------------------------------------------------------------

    public async Task<StudioBookingDto> BookAppointmentAsync(StudioCaller caller, int? sessionTypeId, string? startTime, long? staffId,
        StudioClientProfile profile, CancellationToken ct)
    {
        var typeId = RequireId(sessionTypeId, "sessionTypeId");
        if (string.IsNullOrWhiteSpace(startTime))
        {
            throw StudioException.BadRequest("startTime is required (an ISO-8601 date-time such as 2026-11-12T10:30:00-08:00).");
        }

        var type = await GetAppointmentSessionTypeAsync(typeId, ct);
        var zone = await _clock.GetZoneAsync(ct);
        var start = ParseStart(startTime, zone);
        if (start <= _clock.UtcNow)
        {
            throw StudioException.Conflict("That time has already passed.");
        }

        var wall = TimeZoneInfo.ConvertTime(start, zone).DateTime;
        var times = await GetSessionStartTimesAsync(typeId, ct);
        if (!times.Contains(TimeOnly.FromDateTime(wall)))
        {
            throw StudioException.BadRequest("A session of this type cannot start at that time.");
        }

        var length = type.DefaultTimeLength
            ?? throw new StudioException(422, $"Session type {typeId} has no default length.");
        var end = start.AddMinutes(length);

        var clientId = await GetOrCreateClientAsync(caller, profile, ct);

        using var _ = await _locks.LockAsync("client:" + clientId, ct);

        // A repeated request must not fail because its first copy already booked the slot.
        var existing = (await GetClientAppointmentsAsync(clientId, start.AddDays(-1), end.AddDays(1), ct))
            .FirstOrDefault(a => a.Id is not null && a.SessionTypeId == typeId && IsActive(a) &&
                                 a.StartDateTime is { } s && StudioClock.ToInstant(s.DateTime, zone) == start &&
                                 (staffId is null || a.StaffId == staffId));
        if (existing is not null)
        {
            return ToAppointmentBooking(existing.Id!.Value, clientId, typeId, existing.StaffId, existing.LocationId, type.Name, start, end, zone);
        }

        var availability = await FindFreeTrainerAsync(typeId, start, end, staffId, ct);

        var response = await _gateway.CallAsync((client, token) => client.AppointmentApi.AddAppointment(new AddAppointmentOperationRequest
        {
            SiteId = _gateway.SiteId,
            Authorization = token,
            Body = AppointmentBody(clientId, typeId, availability, start, staffId is not null),
        }, cancellationToken: CancellationToken.None), CancellationToken.None);

        var appointmentId = response.Appointment?.Id;
        if (appointmentId is null or <= 0)
        {
            throw new StudioException(502, "The studio system did not confirm the booking. Please try again.");
        }

        return ToAppointmentBooking(appointmentId.Value, clientId, typeId, availability.Staff.Id, availability.Location.Id, type.Name, start, end, zone);
    }

    /// <summary>
    /// Mindbody reads a start time in a request body correctly only as an explicit UTC time ("...Z") or as studio
    /// wall-clock time. The SDK writes a DateTimeOffset property as "...+00:00", which Mindbody misreads (it then
    /// looks for a trainer at the wrong hour), so the start time travels as an additional property instead.
    /// The end time is left to Mindbody, which derives it from the length of the session type.
    /// </summary>
    private static Mb.AddAppointmentRequest AppointmentBody(string clientId, int typeId, Mb.Availability availability, DateTimeOffset start, bool staffRequested)
    {
        var extra = new MindbodyPublicApi.Core.Models.AdditionalProperties();
        extra.Set("StartDateTime", start.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
        return new Mb.AddAppointmentRequest
        {
            ClientId = clientId,
            LocationId = availability.Location!.Id!.Value,
            SessionTypeId = typeId,
            StaffId = availability.Staff!.Id!.Value,
            StaffRequested = staffRequested,
            AdditionalProperties = extra,
        };
    }

    private async Task<Mb.Availability> FindFreeTrainerAsync(int typeId, DateTimeOffset start, DateTimeOffset end,
        long? staffId, CancellationToken ct)
    {
        var availabilities = await PageAsync(async (offset, limit) =>
        {
            var response = await _gateway.CallAsync((client, token) => client.AppointmentApi.GetBookableItems(new GetBookableItemsOperationRequest
            {
                RequestSessionTypeIds = [typeId],
                RequestStartDate = start,
                RequestEndDate = start,
                RequestStaffIds = staffId is null ? null : [staffId.Value],
                RequestLimit = limit,
                RequestOffset = offset,
                Authorization = token,
                SiteId = _gateway.SiteId,
            }, cancellationToken: ct), ct);
            return (response.Availabilities, response.PaginationResponse?.TotalResults);
        }, ct);

        var free = availabilities
            .Where(a => a.Staff?.Id is not null && a.Location?.Id is not null && a.StartDateTime is not null && a.EndDateTime is not null)
            .Where(a => staffId is null || a.Staff!.Id == staffId)
            // Unlike the other schedule calls, Mindbody answers GetBookableItems with real UTC offsets when it is
            // asked with an offset-qualified date (the SDK always does), so these are already exact instants.
            .Where(a => a.StartDateTime!.Value <= start && end <= a.EndDateTime!.Value)
            .OrderBy(a => a.Staff!.Id)
            .FirstOrDefault();

        if (free is null)
        {
            throw StudioException.Conflict(staffId is null
                ? "No trainer is free at that time."
                : $"Trainer {staffId} is not free at that time.");
        }

        return free;
    }

    private static StudioBookingDto ToAppointmentBooking(long bookingId, string clientId, int typeId, long? staffId, int? locationId, string? name,
        DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone) => new()
    {
        BookingId = bookingId,
        Type = "appointment",
        ClientId = clientId,
        SessionTypeId = typeId,
        TrainerId = staffId,
        LocationId = locationId,
        Name = name,
        StartTime = StudioClock.ToStudioTime(start, zone),
        EndTime = StudioClock.ToStudioTime(end, zone),
    };

    // ------------------------------------------------------------------------------------------------
    // Cancelling
    // ------------------------------------------------------------------------------------------------

    public async Task CancelClassBookingAsync(StudioCaller caller, long bookingId, CancellationToken ct)
    {
        var clientId = await FindClientIdAsync(caller.Email, ct)
            ?? throw StudioException.NotFound("There is no such booking.");
        var zone = await _clock.GetZoneAsync(ct);

        using var _ = await _locks.LockAsync("client:" + clientId, ct);

        var today = await _clock.TodayAsync(ct);
        var visits = await GetClientVisitsAsync(clientId, StudioClock.StartOfDay(today.AddDays(-1), zone), StudioClock.StartOfDay(today.AddYears(4), zone), ct);
        var visit = visits.FirstOrDefault(v => v.Id == bookingId && v.ClassId is > 0 && IsActive(v));
        if (visit is null)
        {
            ThrowUnlessAlreadyCancelled("class", clientId, bookingId);
            return;
        }

        if (visit.StartDateTime is { } s && StudioClock.ToInstant(s.DateTime, zone) <= _clock.UtcNow)
        {
            throw StudioException.Conflict("This class has already started and can no longer be cancelled.");
        }

        await CancelUpstreamAsync("class", clientId, bookingId,
            cancel: () => _gateway.CallAsync<object?>(async (client, token) =>
            {
                try
                {
                    await client.ClassApi.RemoveClientFromClass(new RemoveClientFromClassOperationRequest
                    {
                        SiteId = _gateway.SiteId,
                        Authorization = token,
                        Body = new Mb.RemoveClientFromClassRequest
                        {
                            ClientId = clientId,
                            ClassId = visit.ClassId!.Value,
                            VisitId = (int)bookingId,
                        },
                    }, cancellationToken: CancellationToken.None);
                    return null;
                }
                catch (ResponseDeserializationException ex) when (IsSuccessStatus(ex))
                {
                    // Mindbody did cancel; only its echo of the class could not be read, and nothing here needs it.
                    return null;
                }
            }, CancellationToken.None),
            stillBooked: async () => (await GetClientVisitsAsync(clientId, StudioClock.StartOfDay(today.AddDays(-1), zone),
                StudioClock.StartOfDay(today.AddYears(4), zone), CancellationToken.None)).Any(v => v.Id == bookingId && IsActive(v)));
    }
    public async Task CancelAppointmentBookingAsync(StudioCaller caller, long bookingId, CancellationToken ct)
    {
        var clientId = await FindClientIdAsync(caller.Email, ct)
            ?? throw StudioException.NotFound("There is no such booking.");
        var zone = await _clock.GetZoneAsync(ct);

        using var _ = await _locks.LockAsync("client:" + clientId, ct);

        var today = await _clock.TodayAsync(ct);
        var appointments = await GetClientAppointmentsAsync(clientId, StudioClock.StartOfDay(today.AddDays(-1), zone), StudioClock.StartOfDay(today.AddYears(4), zone), ct);
        var appointment = appointments.FirstOrDefault(a => a.Id == bookingId && IsActive(a));
        if (appointment is null)
        {
            ThrowUnlessAlreadyCancelled("appointment", clientId, bookingId);
            return;
        }

        if (appointment.StartDateTime is { } s && StudioClock.ToInstant(s.DateTime, zone) <= _clock.UtcNow)
        {
            throw StudioException.Conflict("This session has already started and can no longer be cancelled.");
        }

        await CancelUpstreamAsync("appointment", clientId, bookingId,
            cancel: () => _gateway.CallAsync<object?>(async (client, token) =>
            {
                try
                {
                    await client.AppointmentApi.UpdateAppointment(new UpdateAppointmentOperationRequest
                    {
                        SiteId = _gateway.SiteId,
                        Authorization = token,
                        Body = new Mb.UpdateAppointmentRequest { AppointmentId = bookingId, Execute = "cancel" },
                    }, cancellationToken: CancellationToken.None);
                    return null;
                }
                catch (ResponseDeserializationException ex) when (IsSuccessStatus(ex))
                {
                    return null;
                }
            }, CancellationToken.None),
            stillBooked: async () => (await GetClientAppointmentsAsync(clientId, StudioClock.StartOfDay(today.AddDays(-1), zone),
                StudioClock.StartOfDay(today.AddYears(4), zone), CancellationToken.None)).Any(a => a.Id == bookingId && IsActive(a)));
    }

    /// <summary>
    /// Cancels upstream without tying the outcome to the caller's connection: if the app gave up waiting and asked
    /// again, the cancellation must still be recorded. When Mindbody's answer was lost, look again whether it happened.
    /// </summary>
    private async Task CancelUpstreamAsync(string kind, string clientId, long bookingId, Func<Task<object?>> cancel, Func<Task<bool>> stillBooked)
    {
        try
        {
            await cancel();
        }
        catch (StudioException ex) when (ex.StatusCode is 502 or 504)
        {
            bool still;
            try
            {
                still = await stillBooked();
            }
            catch (StudioException)
            {
                throw ex;
            }

            if (still)
            {
                throw;
            }
        }

        MarkCancelled(kind, clientId, bookingId);
    }

    private static bool IsSuccessStatus(ApiException ex) => (int)ex.StatusCode is >= 200 and < 300;

    private void ThrowUnlessAlreadyCancelled(string kind, string clientId, long bookingId)
    {
        // A repeated cancellation (the first one succeeded but its answer got lost) is a success.
        if (_cancelled.TryGetValue(MarkerKey(kind, clientId, bookingId), out var until) && until > _clock.UtcNow)
        {
            return;
        }

        throw StudioException.NotFound("There is no such booking.");
    }

    private void MarkCancelled(string kind, string clientId, long bookingId)
    {
        var now = _clock.UtcNow;
        foreach (var expired in _cancelled.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
        {
            _cancelled.TryRemove(expired, out _);
        }

        _cancelled[MarkerKey(kind, clientId, bookingId)] = now + CancellationMarkerLifetime;
    }

    private static string MarkerKey(string kind, string clientId, long bookingId) => $"{kind}:{clientId}:{bookingId}";

    // ------------------------------------------------------------------------------------------------
    // Trainers
    // ------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<StudioTrainerDto>> ListTrainersAsync(CancellationToken ct)
    {
        var staff = await PageAsync(async (offset, limit) =>
        {
            var response = await _gateway.CallAsync((client, token) => client.StaffApi.GetStaff(new GetStaffOperationRequest
            {
                RequestLimit = limit,
                RequestOffset = offset,
                Authorization = token,
                SiteId = _gateway.SiteId,
            }, cancellationToken: ct), ct);
            return (response.StaffMembers, response.PaginationResponse?.TotalResults);
        }, ct);

        return staff
            .Where(s => s.Id is not null)
            .GroupBy(s => s.Id!.Value)
            .Select(g => g.First())
            .OrderBy(s => s.Id)
            .Select(s => new StudioTrainerDto
            {
                Id = s.Id!.Value,
                FirstName = s.FirstName,
                LastName = s.LastName,
                DisplayName = s.DisplayName ?? s.Name,
                Email = s.Email,
                HomePhone = s.HomePhone,
                MobilePhone = s.MobilePhone,
                WorkPhone = s.WorkPhone,
                Address = s.Address,
                City = s.City,
                State = s.State,
                PostalCode = s.PostalCode,
                Country = s.Country,
            })
            .ToList();
    }

    // ------------------------------------------------------------------------------------------------
    // Clients
    // ------------------------------------------------------------------------------------------------

    /// <summary>The Mindbody client of the caller; created once, from the eShop identity, on the first booking.</summary>
    private async Task<string> GetOrCreateClientAsync(StudioCaller caller, StudioClientProfile profile, CancellationToken ct)
    {
        using var _ = await _locks.LockAsync("identity:" + caller.Email.ToLowerInvariant(), ct);

        var existing = await FindClientIdAsync(caller.Email, ct);
        if (existing is not null)
        {
            return existing;
        }

        var details = await ValidateProfileAsync(profile, ct);
        var (first, last) = NamesFrom(caller);

        try
        {
            var response = await _gateway.CallAsync((client, token) => client.ClientApi.AddClient(new AddClientOperationRequest
            {
                SiteId = _gateway.SiteId,
                Authorization = token,
                Body = new Mb.AddClientRequest
                {
                    FirstName = first,
                    LastName = last,
                    Email = caller.Email,
                    AddressLine1 = details.Address,
                    City = details.City,
                    State = details.State,
                    PostalCode = details.PostalCode,
                    BirthDate = new DateTimeOffset(details.BirthDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                    MobilePhone = details.Mobile,
                    ReferredBy = details.ReferredBy,
                    HomeLocation = new Mb.Location { Id = details.HomeLocationId },
                },
            }, cancellationToken: CancellationToken.None), CancellationToken.None);

            var id = response.Client?.Id;
            if (string.IsNullOrEmpty(id))
            {
                throw new StudioException(502, "The studio system did not confirm the new client. Please try again.");
            }

            return id;
        }
        catch (StudioException ex) when (ex.StatusCode == 409)
        {
            // Mindbody refuses duplicates; if the client appeared meanwhile, use it.
            return await FindClientIdAsync(caller.Email, ct) ?? throw ex;
        }
    }

    private async Task<string?> FindClientIdAsync(string emailOrId, CancellationToken ct)
    {
        var wanted = emailOrId.Trim();
        var matches = await PageAsync(async (offset, limit) =>
        {
            var response = await _gateway.CallAsync((client, token) => client.ClientApi.GetClients(new GetClientsOperationRequest
            {
                RequestSearchText = wanted,
                RequestLimit = limit,
                RequestOffset = offset,
                Authorization = token,
                SiteId = _gateway.SiteId,
            }, cancellationToken: ct), ct);
            return (response.Clients, response.PaginationResponse?.TotalResults);
        }, ct);

        var byEmail = matches
            .Where(c => c.Id is not null && c.Active != false && string.Equals(c.Email?.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => long.TryParse(c.Id, out var n) ? n : long.MaxValue)
            .FirstOrDefault();
        if (byEmail is not null)
        {
            return byEmail.Id;
        }

        // Staff may also name a shopper by the studio's own client id.
        if (wanted.All(char.IsDigit))
        {
            var response = await _gateway.CallAsync((client, token) => client.ClientApi.GetClients(new GetClientsOperationRequest
            {
                RequestClientIDs = [wanted],
                Authorization = token,
                SiteId = _gateway.SiteId,
            }, cancellationToken: ct), ct);
            return response.Clients?.FirstOrDefault(c => c.Id == wanted && c.Active != false)?.Id;
        }

        return null;
    }

    private static (string First, string Last) NamesFrom(StudioCaller caller)
    {
        var local = caller.Email.Split('@')[0];
        var parts = Regex.Split(local, @"[._\-+\s]+").Where(p => p.Length > 0).Select(Capitalise).ToArray();
        if (parts.Length == 0)
        {
            parts = [Capitalise(caller.UserName)];
        }

        return parts.Length == 1 ? (parts[0], parts[0]) : (parts[0], string.Join(' ', parts.Skip(1)));
    }

    private static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];

    private sealed record ValidProfile(int HomeLocationId, string Address, string City, string State, string PostalCode,
        DateOnly BirthDate, string Mobile, string ReferredBy);

    private async Task<ValidProfile> ValidateProfileAsync(StudioClientProfile? profile, CancellationToken ct)
    {
        profile ??= new StudioClientProfile();
        var missing = new List<string>();
        if (profile.HomeLocationId is null or <= 0) missing.Add("homeLocationId");
        if (string.IsNullOrWhiteSpace(profile.AddressLine1)) missing.Add("streetAddress");
        if (string.IsNullOrWhiteSpace(profile.City)) missing.Add("city");
        if (string.IsNullOrWhiteSpace(profile.State)) missing.Add("state");
        if (string.IsNullOrWhiteSpace(profile.PostalCode)) missing.Add("postalCode");
        if (string.IsNullOrWhiteSpace(profile.BirthDate)) missing.Add("dateOfBirth");
        if (string.IsNullOrWhiteSpace(profile.MobilePhone)) missing.Add("mobilePhone");
        if (string.IsNullOrWhiteSpace(profile.ReferredBy)) missing.Add("referredBy");
        if (missing.Count > 0)
        {
            throw StudioException.BadRequest(
                "This is your first booking with the studio, so these details are needed: " + string.Join(", ", missing) + ".");
        }

        if (!DateOnly.TryParse(profile.BirthDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth) ||
            birth.Year < 1900 || birth > await _clock.TodayAsync(ct))
        {
            throw StudioException.BadRequest("dateOfBirth must be a valid date in the past, such as 1990-05-06.");
        }

        var mobile = profile.MobilePhone!.Trim();
        if (mobile.Count(char.IsDigit) < 7 || mobile.Any(ch => !(char.IsDigit(ch) || "+-(). ".Contains(ch))))
        {
            throw StudioException.BadRequest("mobilePhone must be a phone number.");
        }

        var locations = await CachedAsync("locations", async () =>
            await PageAsync(async (offset, limit) =>
            {
                var response = await _gateway.CallAsync((client, token) => client.SiteApi.GetLocations(new GetLocationsOperationRequest
                {
                    RequestLimit = limit,
                    RequestOffset = offset,
                    Authorization = token,
                    SiteId = _gateway.SiteId,
                }, cancellationToken: ct), ct);
                return (response.Locations, response.PaginationResponse?.TotalResults);
            }, ct));
        if (!locations.Any(l => l.Id == profile.HomeLocationId))
        {
            throw StudioException.BadRequest(
                "homeLocationId is not one of the studio's locations: " +
                string.Join(", ", locations.Where(l => l.Id is not null).Select(l => $"{l.Id} ({l.Name})")) + ".");
        }

        var referrals = await CachedAsync("referral-types", async () =>
        {
            var response = await _gateway.CallAsync((client, token) => client.ClientApi.GetClientReferralTypes(new GetClientReferralTypesOperationRequest
            {
                Authorization = token,
                SiteId = _gateway.SiteId,
            }, cancellationToken: ct), ct);
            return (IReadOnlyList<string>)(response.ReferralTypes?.ToList() ?? new List<string>());
        });
        var referredBy = referrals.FirstOrDefault(r => string.Equals(r, profile.ReferredBy!.Trim(), StringComparison.OrdinalIgnoreCase));
        if (referredBy is null)
        {
            throw StudioException.BadRequest("referredBy must be one of: " + string.Join(", ", referrals) + ".");
        }

        return new ValidProfile(profile.HomeLocationId!.Value, profile.AddressLine1!.Trim(), profile.City!.Trim(), profile.State!.Trim(),
            profile.PostalCode!.Trim(), birth, mobile, referredBy);
    }

    // ------------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<Mb.Visit>> GetClientVisitsAsync(string clientId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        await PageAsync(async (offset, limit) =>
        {
            var response = await _gateway.CallAsync((client, token) => client.ClientApi.GetClientVisits(new GetClientVisitsOperationRequest
            {
                RequestClientId = clientId,
                RequestStartDate = from,
                RequestEndDate = to,
                RequestLimit = limit,
                RequestOffset = offset,
                Authorization = token,
                SiteId = _gateway.SiteId,
            }, cancellationToken: ct), ct);
            return (response.Visits, response.PaginationResponse?.TotalResults);
        }, ct);

    private async Task<IReadOnlyList<Mb.Appointment>> GetClientAppointmentsAsync(string clientId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        await PageAsync(async (offset, limit) =>
        {
            var response = await _gateway.CallAsync((client, token) => client.AppointmentApi.GetStaffAppointments(new GetStaffAppointmentsOperationRequest
            {
                RequestClientId = clientId,
                RequestStartDate = from,
                RequestEndDate = to,
                RequestLimit = limit,
                RequestOffset = offset,
                Authorization = token,
                SiteId = _gateway.SiteId,
            }, cancellationToken: ct), ct);
            return (response.Appointments, response.PaginationResponse?.TotalResults);
        }, ct);

    private static bool IsActive(Mb.Visit visit) =>
        visit.LateCancelled != true &&
        visit.AppointmentStatus != MindbodyPublicApi.Models.Enums.AppointmentStatus.Cancelled &&
        visit.AppointmentStatus != MindbodyPublicApi.Models.Enums.AppointmentStatus.LateCancelled &&
        visit.AppointmentStatus != MindbodyPublicApi.Models.Enums.AppointmentStatus.NoShow;

    private static bool IsActive(Mb.Appointment appointment) =>
        appointment.Status != MindbodyPublicApi.Models.Enums.Status.Cancelled &&
        appointment.Status != MindbodyPublicApi.Models.Enums.Status.LateCancelled &&
        appointment.Status != MindbodyPublicApi.Models.Enums.Status.NoShow;

    /// <summary>Reads every page of a Mindbody list, so nothing past the first page is silently dropped.</summary>
    private static async Task<IReadOnlyList<T>> PageAsync<T>(
        Func<int, int, Task<(IReadOnlyList<T>? Items, int? Total)>> fetch, CancellationToken ct)
    {
        var all = new List<T>();
        var offset = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var (items, total) = await fetch(offset, PageSize);
            var count = items?.Count ?? 0;
            if (count > 0)
            {
                all.AddRange(items!);
            }

            offset += count;
            if (count == 0 || (total is { } t && offset >= t) || (total is null && count < PageSize))
            {
                return all;
            }
        }
    }

    private async Task<T> CachedAsync<T>(string key, Func<Task<T>> factory)
    {
        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        var value = await factory();
        _cache.Set(key, value, ReferenceDataLifetime);
        return value;
    }

    private static int RequireId(int? value, string name) =>
        value is > 0 ? value.Value : throw StudioException.BadRequest($"{name} is required and must be a positive number.");

    private static DateOnly? ParseDate(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        // Accept a plain date or a date-time; only the calendar day matters.
        if (DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        if (trimmed.Length > 10 && trimmed[10] is 'T' or ' ' &&
            DateOnly.TryParseExact(trimmed[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return date;
        }

        throw StudioException.BadRequest($"{name} must be a date such as 2026-11-12.");
    }

    private static void ValidateRange(DateOnly first, DateOnly last)
    {
        if (last < first)
        {
            throw StudioException.BadRequest("'to' must not be before 'from'.");
        }

        if (last.DayNumber - first.DayNumber + 1 > MaxRangeDays)
        {
            throw StudioException.BadRequest($"At most {MaxRangeDays} days can be listed at once.");
        }
    }

    private static DateTimeOffset ParseStart(string text, TimeZoneInfo zone)
    {
        var trimmed = text.Trim();
        const DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces;
        if (HasOffset.IsMatch(trimmed))
        {
            if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, styles, out var withOffset))
            {
                return withOffset.ToUniversalTime();
            }
        }
        else if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, styles, out var wall) && trimmed.Contains('T'))
        {
            // No offset given: read it as the studio's own clock.
            return StudioClock.ToInstant(wall, zone);
        }

        throw StudioException.BadRequest("startTime must be an ISO-8601 date-time with a UTC offset, such as 2026-11-12T10:30:00-08:00.");
    }
}
