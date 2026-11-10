using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MindbodyPublicApi.Models;
using MindbodyPublicApi.Models.Enums;
using MindbodyPublicApi.Requests.AppointmentApi;
using MindbodyPublicApi.Requests.ClassApi;
using MindbodyPublicApi.Requests.ClientApi;
using MindbodyPublicApi.Requests.SiteApi;
using MindbodyPublicApi.Requests.StaffApi;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Process-wide state shared by the per-request <see cref="StudioService"/> instances.
/// </summary>
public sealed class StudioState
{
    private static readonly TimeSpan LedgerLifetime = TimeSpan.FromDays(30);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _cancelled = new();
    private long _writes;

    public KeyedLock Locks { get; } = new();

    /// <summary>
    /// Remembers cancellations carried out for a client so that the app repeating a DELETE it got no answer to
    /// is told it succeeded (Mindbody no longer lists a cancelled booking, so it cannot tell us itself).
    /// </summary>
    public void RecordCancellation(string clientId, string kind, long bookingId, DateTimeOffset now)
    {
        _cancelled[$"{clientId}|{kind}|{bookingId}"] = now;
        if (Interlocked.Increment(ref _writes) % 256 == 0)
        {
            foreach (var entry in _cancelled)
            {
                if (now - entry.Value > LedgerLifetime) _cancelled.TryRemove(entry.Key, out _);
            }
        }
    }

    public bool WasCancelled(string clientId, string kind, long bookingId, DateTimeOffset now) =>
        _cancelled.TryGetValue($"{clientId}|{kind}|{bookingId}", out var at) && now - at <= LedgerLifetime;
}

public class StudioService
{
    private const int MaxClassDays = 366;
    private const int MaxSlotDays = 731;
    private const int LookAheadDays = 400;
    private const int DefaultSessionMinutes = 60;

    private static readonly Regex PhonePattern = new(@"^\+?[0-9 ().\-]{7,20}$", RegexOptions.Compiled);

    private readonly MindbodyGateway _mb;
    private readonly UserManager<ApplicationUser> _users;
    private readonly StudioState _state;

    public StudioService(MindbodyGateway mb, UserManager<ApplicationUser> users, StudioState state)
    {
        _mb = mb;
        _users = users;
        _state = state;
    }

    // =============================================================== classes

    public async Task<List<ClassListingDto>> ListClassesAsync(string? fromText, string? toText, CancellationToken ct)
    {
        var zone = await _mb.GetSiteZoneAsync(ct);
        var today = _mb.SiteToday(zone);
        var from = ParseDate(fromText, "from");
        var to = ParseDate(toText, "to");

        if (from is null && to is null)
        {
            from = today;
            to = today.AddDays(7);
        }
        else if (from is null)
        {
            from = today;
        }
        else if (to is null)
        {
            to = from.Value.AddDays(7);
        }

        if (to < from) throw StudioException.BadRequest("'to' must not be earlier than 'from'.");
        if (to.Value.DayNumber - from.Value.DayNumber + 1 > MaxClassDays)
            throw StudioException.BadRequest($"The range may cover at most {MaxClassDays} days.");

        var classes = await FetchClassesAsync(zone, from.Value, to.Value, null, ct);
        var now = _mb.UtcNow;

        return classes
            .Select(c => (Class: c, Start: StartOf(c, zone), End: EndOf(c, zone)))
            .Where(x => x.Start is not null && x.End is not null)
            .Where(x => IsOpenToShoppers(x.Class, x.Start!.Value, now))
            .Where(x => DateOnly.FromDateTime(x.Start!.Value.DateTime) >= from && DateOnly.FromDateTime(x.Start!.Value.DateTime) <= to)
            .OrderBy(x => x.Start).ThenBy(x => x.Class.Id)
            .Select(x => ToListing(x.Class, x.Start!.Value, x.End!.Value))
            .ToList();
    }

    private static bool IsOpenToShoppers(Class c, DateTimeOffset start, DateTimeOffset now)
    {
        if (c.Id is null) return false;
        if (c.IsCanceled == true) return false;
        if (c.Active == false) return false;
        if (start <= now) return false; // already started: it can no longer be attended
        if (c.MaxCapacity is int max && c.TotalBooked is int booked && booked >= max && c.IsWaitlistAvailable != true) return false;
        return true;
    }

    private static DateTimeOffset? StartOf(Class c, TimeZoneInfo zone) =>
        c.StartDateTime is { } s ? MindbodyGateway.FromSiteWall(s, zone) : null;

    private static DateTimeOffset? EndOf(Class c, TimeZoneInfo zone) =>
        c.EndDateTime is { } e ? MindbodyGateway.FromSiteWall(e, zone) : null;

    private static ClassListingDto ToListing(Class c, DateTimeOffset start, DateTimeOffset end) => new()
    {
        Id = c.Id!.Value,
        Name = c.ClassDescription?.Name,
        Description = c.ClassDescription?.Description,
        StartTime = start,
        EndTime = end,
        LocationId = c.Location?.Id,
        LocationName = c.Location?.Name,
        TrainerId = c.Staff?.Id,
        TrainerName = c.Staff?.DisplayName ?? c.Staff?.Name,
        Capacity = c.MaxCapacity,
        Booked = c.TotalBooked,
        IsWaitlistAvailable = c.IsWaitlistAvailable == true,
    };

    private Task<List<Class>> FetchClassesAsync(TimeZoneInfo zone, DateOnly from, DateOnly to, IReadOnlyList<int>? ids, CancellationToken ct) =>
        MindbodyGateway.ReadAllAsync<Class>(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, token) => c.ClassApi.GetClasses(new GetClassesOperationRequest
            {
                SiteId = site,
                Authorization = token,
                RequestStartDateTime = MindbodyGateway.ToSiteWallRequest(from),
                RequestEndDateTime = MindbodyGateway.ToSiteWallRequestEnd(to),
                RequestClassIds = ids,
                RequestLimit = limit,
                RequestOffset = offset,
            }, null, ct), "reading the class timetable", ct);
            return (r?.Classes, r?.PaginationResponse?.TotalResults);
        });

    private async Task<(Class Class, DateTimeOffset Start, DateTimeOffset End)> GetClassAsync(int classId, TimeZoneInfo zone, CancellationToken ct)
    {
        var today = _mb.SiteToday(zone);
        var found = await FetchClassesAsync(zone, today.AddDays(-1), today.AddDays(LookAheadDays), new[] { classId }, ct);
        var cls = found.FirstOrDefault(c => c.Id == classId);
        if (cls is null)
            throw StudioException.NotFound($"There is no class {classId} in the studio's timetable.");
        var start = StartOf(cls, zone);
        var end = EndOf(cls, zone);
        if (start is null || end is null)
            throw new StudioException(HttpStatusCode.BadGateway, "The studio system returned a class without times.");
        return (cls, start.Value, end.Value);
    }

    // =============================================================== session times

    public async Task<List<SlotDto>> ListSlotsAsync(int? sessionTypeId, string? fromText, string? toText, CancellationToken ct)
    {
        if (sessionTypeId is null or <= 0) throw StudioException.BadRequest("'sessionTypeId' is required.");
        var zone = await _mb.GetSiteZoneAsync(ct);
        var today = _mb.SiteToday(zone);
        var from = ParseDate(fromText, "from") ?? today;
        var to = ParseDate(toText, "to") ?? from;
        if (to < from) throw StudioException.BadRequest("'to' must not be earlier than 'from'.");
        if (to.DayNumber - from.DayNumber + 1 > MaxSlotDays)
            throw StudioException.BadRequest($"The range may cover at most {MaxSlotDays} days.");

        var sessionType = await GetAppointmentSessionTypeAsync(sessionTypeId.Value, ct);
        var minutes = sessionType.DefaultTimeLength is > 0 ? sessionType.DefaultTimeLength.Value : DefaultSessionMinutes;
        var times = await GetStartTimesAsync(sessionTypeId.Value, ct);

        var slots = new List<SlotDto>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            foreach (var time in times)
            {
                var wall = day.ToDateTime(TimeOnly.FromTimeSpan(time));
                if (zone.IsInvalidTime(wall)) continue; // skipped by a daylight-saving jump
                var start = MindbodyGateway.FromSiteWall(wall, zone);
                var endInstant = start.AddMinutes(minutes);
                var end = TimeZoneInfo.ConvertTime(endInstant, zone);
                slots.Add(new SlotDto { SessionTypeId = sessionTypeId.Value, StartTime = start, EndTime = end });
            }
        }
        return slots;
    }

    private async Task<SessionType> GetAppointmentSessionTypeAsync(int sessionTypeId, CancellationToken ct)
    {
        var types = await MindbodyGateway.ReadAllAsync<SessionType>(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, token) => c.SiteApi.GetSessionTypes(new GetSessionTypesOperationRequest
            {
                SiteId = site,
                Authorization = token,
                RequestLimit = limit,
                RequestOffset = offset,
            }, null, ct), "reading the session types", ct);
            return (r?.SessionTypes, r?.PaginationResponse?.TotalResults);
        });

        var type = types.FirstOrDefault(t => t.Id == sessionTypeId);
        if (type is null)
            throw StudioException.NotFound($"There is no session type {sessionTypeId}.");
        if (type.Type != Type1.Appointment)
            throw StudioException.BadRequest($"Session type {sessionTypeId} is not a one-to-one session type.");
        return type;
    }

    private async Task<List<TimeSpan>> GetStartTimesAsync(int sessionTypeId, CancellationToken ct)
    {
        var raw = new List<string>();
        var grouped = false;
        var all = await MindbodyGateway.ReadAllAsync<string>(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, token) => c.AppointmentApi.GetActiveSessionTimes(new GetActiveSessionTimesOperationRequest
            {
                SiteId = site,
                Authorization = token,
                RequestSessionTypeIds = new[] { sessionTypeId },
                RequestLimit = limit,
                RequestOffset = offset,
            }, null, ct), "reading the session times", ct);

            var group = r?.SessionTypeActiveSessionTimes?.FirstOrDefault(g => g.SessionTypeId == sessionTypeId);
            if (group is not null)
            {
                // The per-type list is complete and not paginated.
                grouped = true;
                raw.AddRange(group.ActiveSessionTimes ?? Array.Empty<string>());
                return (null, 0);
            }
            return (r?.ActiveSessionTimes, r?.PaginationResponse?.TotalResults);
        });

        var source = grouped ? raw : all;
        return source
            .Select(s => TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var t) ? (TimeSpan?)t : null)
            .Where(t => t is { } v && v >= TimeSpan.Zero && v < TimeSpan.FromDays(1))
            .Select(t => t!.Value)
            .Distinct()
            .OrderBy(t => t)
            .ToList();
    }

    // =============================================================== booking

    public async Task<BookingDto> BookClassAsync(string? userName, int? classId, ClientDetailsRequest? details, CancellationToken ct)
    {
        if (classId is null or <= 0) throw StudioException.BadRequest("'classId' is required.");
        var zone = await _mb.GetSiteZoneAsync(ct);
        var user = await GetUserAsync(userName);
        var (cls, start, end) = await GetClassAsync(classId.Value, zone, ct);

        if (!IsOpenToShoppers(cls, start, _mb.UtcNow))
            throw StudioException.Conflict($"Class {classId} is not open for booking (it is cancelled, hidden, full or has already started).");

        var clientId = await EnsureClientAsync(user, details, ct);
        return await PlaceInClassAsync(clientId, cls, start, end, zone, ct);
    }

    public async Task<BookingDto> AdminBookClassAsync(string? shopper, int? classId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(shopper)) throw StudioException.BadRequest("'shopper' is required.");
        if (classId is null or <= 0) throw StudioException.BadRequest("'classId' is required.");
        var zone = await _mb.GetSiteZoneAsync(ct);

        var user = await _users.FindByNameAsync(shopper)
                   ?? await _users.FindByEmailAsync(shopper)
                   ?? await _users.FindByIdAsync(shopper);
        if (user is null) throw StudioException.NotFound($"There is no shopper '{shopper}'.");

        var (cls, start, end) = await GetClassAsync(classId.Value, zone, ct);
        var client = await FindClientAsync(user, ct);
        if (client?.Id is null)
            throw StudioException.Conflict($"Shopper '{shopper}' is not yet a client of the studio; they must make their first booking themselves to provide their details.");

        return await PlaceInClassAsync(client.Id, cls, start, end, zone, ct);
    }

    private async Task<BookingDto> PlaceInClassAsync(string clientId, Class cls, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, CancellationToken ct)
    {
        using var _ = await _state.Locks.AcquireAsync("client:" + clientId);

        var existing = (await GetScheduleAsync(clientId, DateOnly.FromDateTime(start.DateTime), DateOnly.FromDateTime(start.DateTime), ct))
            .FirstOrDefault(v => v.ClassId == cls.Id && v.Id is > 0 && !IsCancelled(v.AppointmentStatus));
        if (existing is not null)
            return ClassBooking(cls, existing.Id!.Value, start, end, already: true);

        // Writes are not tied to the caller's connection: if the app gives up and repeats the request, it must
        // find the booking this call made rather than race with it.
        var added = await _mb.CallAsync((c, site, token) => c.ClassApi.AddClientToClass(new AddClientToClassOperationRequest
        {
            SiteId = site,
            Authorization = token,
            Body = new AddClientToClassRequest { ClientId = clientId, ClassId = cls.Id!.Value },
        }), "booking the class", CancellationToken.None);

        var visitId = added?.Visit?.Id;
        if (visitId is null or <= 0)
            throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not confirm the class booking.");
        return ClassBooking(cls, visitId.Value, start, end, already: false);
    }

    private static BookingDto ClassBooking(Class cls, long visitId, DateTimeOffset start, DateTimeOffset end, bool already) => new()
    {
        BookingId = visitId,
        Kind = "class",
        Name = cls.ClassDescription?.Name,
        ClassId = cls.Id,
        TrainerId = cls.Staff?.Id,
        LocationId = cls.Location?.Id,
        StartTime = start,
        EndTime = end,
        AlreadyBooked = already,
    };

    public async Task<BookingDto> BookAppointmentAsync(string? userName, int? sessionTypeId, string? startTimeText,
        long? staffId, int? locationId, ClientDetailsRequest? details, CancellationToken ct)
    {
        if (sessionTypeId is null or <= 0) throw StudioException.BadRequest("'sessionTypeId' is required.");
        if (string.IsNullOrWhiteSpace(startTimeText) ||
            !DateTimeOffset.TryParse(startTimeText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var requested))
            throw StudioException.BadRequest("'startTime' is required and must be an ISO-8601 date-time with its UTC offset.");

        var zone = await _mb.GetSiteZoneAsync(ct);
        var user = await GetUserAsync(userName);
        var type = await GetAppointmentSessionTypeAsync(sessionTypeId.Value, ct);
        var minutes = type.DefaultTimeLength is > 0 ? type.DefaultTimeLength.Value : DefaultSessionMinutes;

        // The caller's instant, expressed as the studio's wall clock.
        var start = TimeZoneInfo.ConvertTime(requested, zone);
        if (start <= _mb.UtcNow) throw StudioException.BadRequest("The session must start in the future.");
        var wall = start.DateTime;

        var grid = await GetStartTimesAsync(sessionTypeId.Value, ct);
        if (!grid.Contains(wall.TimeOfDay))
            throw StudioException.BadRequest("That is not a time a session of this type can start; use the times listed by /api/studio/slots.");
        var end = TimeZoneInfo.ConvertTime(start.AddMinutes(minutes), zone);
        var endWall = TimeZoneInfo.ConvertTime(end, zone);

        var date = DateOnly.FromDateTime(wall);
        var windows = await MindbodyGateway.ReadAllAsync<Availability>(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, token) => c.AppointmentApi.GetBookableItems(new GetBookableItemsOperationRequest
            {
                SiteId = site,
                Authorization = token,
                RequestSessionTypeIds = new[] { sessionTypeId.Value },
                RequestStartDate = MindbodyGateway.ToSiteWallRequest(date),
                RequestEndDate = MindbodyGateway.ToSiteWallRequestEnd(date),
                RequestStaffIds = staffId is null ? null : new[] { staffId.Value },
                RequestLocationIds = locationId is null ? null : new[] { locationId.Value },
                RequestLimit = limit,
                RequestOffset = offset,
            }, null, ct), "checking trainer availability", ct);
            return (r?.Availabilities, r?.PaginationResponse?.TotalResults);
        });

        // Mindbody lists a trainer's whole working window together with the narrower windows left free around
        // the bookings already made; only the narrowest ones are really free.
        var usable = windows
            .Where(a => a.StartDateTime is not null && a.EndDateTime is not null && a.Staff?.Id is not null && a.Location?.Id is not null)
            .Where(a => a.SessionType?.Id is null || a.SessionType.Id == sessionTypeId)
            .Where(a => staffId is null || a.Staff!.Id == staffId)
            .Where(a => locationId is null || a.Location!.Id == locationId)
            .ToList();
        var candidates = usable
            .Where(a => !usable.Any(o => o != a && o.Staff!.Id == a.Staff!.Id && o.Location!.Id == a.Location!.Id &&
                                         o.StartDateTime!.Value.DateTime >= a.StartDateTime!.Value.DateTime &&
                                         o.EndDateTime!.Value.DateTime <= a.EndDateTime!.Value.DateTime &&
                                         (o.EndDateTime!.Value.DateTime - o.StartDateTime!.Value.DateTime) <
                                         (a.EndDateTime!.Value.DateTime - a.StartDateTime!.Value.DateTime)))
            .Where(a => a.StartDateTime!.Value.DateTime <= wall && a.EndDateTime!.Value.DateTime >= endWall.DateTime)
            .Select(a => (Staff: a.Staff!.Id!.Value, Location: a.Location!.Id!.Value))
            .Distinct()
            .OrderBy(a => a.Staff)
            .ToList();

        var clientId = await EnsureClientAsync(user, details, ct);

        using var _ = await _state.Locks.AcquireAsync("client:" + clientId);

        // Look a day either side of the studio date: Mindbody is loose about which day an appointment falls on.
        var mine = await GetAppointmentsAsync(clientId, date.AddDays(-1), date.AddDays(1), null, ct);
        var existing = mine.FirstOrDefault(a => a.SessionTypeId == sessionTypeId && a.StartDateTime is not null &&
                                                a.StartDateTime.Value.DateTime == wall && !IsCancelled(a.Status) &&
                                                (staffId is null || a.StaffId == staffId));
        if (existing?.Id is not null)
            return AppointmentBooking(existing.Id.Value, type.Name, sessionTypeId.Value, existing.StaffId, existing.LocationId, start, end, already: true);

        if (candidates.Count == 0)
            throw StudioException.Conflict("No trainer is free for a session of this type at that time.");

        // Mindbody lists some trainers as free that it then refuses to book (no pay rate for the service, say), so
        // try the free trainers in turn and report why the last was refused if none can take the session.
        StudioException? refusal = null;
        foreach (var (candidateStaff, candidateLocation) in candidates)
        {
            try
            {
                var added = await _mb.CallAsync((c, site, token) => c.AppointmentApi.AddAppointment(new AddAppointmentOperationRequest
                {
                    SiteId = site,
                    Authorization = token,
                    Body = new AddAppointmentRequest
                    {
                        ClientId = clientId,
                        SessionTypeId = sessionTypeId.Value,
                        StaffId = candidateStaff,
                        LocationId = candidateLocation,
                        StartDateTime = MindbodyGateway.ToSiteWallRequest(wall),
                        EndDateTime = MindbodyGateway.ToSiteWallRequest(endWall.DateTime),
                    },
                }), "booking the session", CancellationToken.None);

                var appointmentId = added?.Appointment?.Id;
                if (appointmentId is null or <= 0)
                    throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not confirm the session booking.");
                return AppointmentBooking(appointmentId.Value, type.Name, sessionTypeId.Value, candidateStaff, candidateLocation, start, end, already: false);
            }
            catch (StudioException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                refusal = ex;
            }
        }

        throw StudioException.Conflict("That session could not be booked at that time. " + refusal?.Message);
    }

    private static BookingDto AppointmentBooking(long id, string? name, int sessionTypeId, long? staffId, int? locationId,
        DateTimeOffset start, DateTimeOffset end, bool already) => new()
    {
        BookingId = id,
        Kind = "appointment",
        Name = name,
        SessionTypeId = sessionTypeId,
        TrainerId = staffId,
        LocationId = locationId,
        StartTime = start,
        EndTime = end,
        AlreadyBooked = already,
    };

    // =============================================================== cancelling

    public async Task<CancellationDto> CancelClassBookingAsync(string? userName, long bookingId, CancellationToken ct)
    {
        var zone = await _mb.GetSiteZoneAsync(ct);
        var user = await GetUserAsync(userName);
        var client = await FindClientAsync(user, ct)
                     ?? throw StudioException.NotFound("You have no studio bookings.");
        var clientId = client.Id!;

        using var _ = await _state.Locks.AcquireAsync("client:" + clientId);
        var today = _mb.SiteToday(zone);

        async Task<VisitWithWaitlistInfo?> FindAsync() =>
            (await GetScheduleAsync(clientId, today.AddDays(-1), today.AddDays(LookAheadDays), ct))
                .FirstOrDefault(v => v.Id == bookingId && v.ClassId is > 0 && !IsCancelled(v.AppointmentStatus));

        var visit = await FindAsync();
        if (visit is null)
        {
            if (_state.WasCancelled(clientId, "class", bookingId, _mb.UtcNow))
                return new CancellationDto { BookingId = bookingId, Kind = "class" };
            throw StudioException.NotFound($"You have no class booking {bookingId}.");
        }

        await _mb.CallAsync((c, site, token) => c.ClassApi.RemoveClientFromClass(new RemoveClientFromClassOperationRequest
        {
            SiteId = site,
            Authorization = token,
            Body = new RemoveClientFromClassRequest { ClientId = clientId, ClassId = visit.ClassId!.Value, VisitId = (int)bookingId },
        }), "cancelling the class booking", CancellationToken.None, tolerateUnreadableBody: true);

        // Mindbody answers this operation with a body the SDK cannot always read, so confirm the outcome.
        if (await FindAsync() is not null)
            throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not confirm the cancellation.");

        _state.RecordCancellation(clientId, "class", bookingId, _mb.UtcNow);
        return new CancellationDto { BookingId = bookingId, Kind = "class" };
    }

    public async Task<CancellationDto> CancelAppointmentBookingAsync(string? userName, long bookingId, CancellationToken ct)
    {
        var zone = await _mb.GetSiteZoneAsync(ct);
        var user = await GetUserAsync(userName);
        var client = await FindClientAsync(user, ct)
                     ?? throw StudioException.NotFound("You have no studio bookings.");
        var clientId = client.Id!;

        using var _ = await _state.Locks.AcquireAsync("client:" + clientId);
        var today = _mb.SiteToday(zone);

        async Task<Appointment?> FindAsync() =>
            (await GetAppointmentsAsync(clientId, today.AddDays(-1), today.AddDays(LookAheadDays), null, ct))
                .FirstOrDefault(a => a.Id == bookingId && !IsCancelled(a.Status));

        var appointment = await FindAsync();
        if (appointment is null)
        {
            if (_state.WasCancelled(clientId, "appointment", bookingId, _mb.UtcNow))
                return new CancellationDto { BookingId = bookingId, Kind = "appointment" };
            throw StudioException.NotFound($"You have no session booking {bookingId}.");
        }

        await _mb.CallAsync((c, site, token) => c.AppointmentApi.UpdateAppointment(new UpdateAppointmentOperationRequest
        {
            SiteId = site,
            Authorization = token,
            Body = new UpdateAppointmentRequest { AppointmentId = bookingId, Execute = "Cancel" },
        }), "cancelling the session booking", CancellationToken.None, tolerateUnreadableBody: true);

        if (await FindAsync() is not null)
            throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not confirm the cancellation.");

        _state.RecordCancellation(clientId, "appointment", bookingId, _mb.UtcNow);
        return new CancellationDto { BookingId = bookingId, Kind = "appointment" };
    }

    private static bool IsCancelled(AppointmentStatus? status) =>
        status == AppointmentStatus.Cancelled || status == AppointmentStatus.LateCancelled;

    private static bool IsCancelled(Status? status) =>
        status == Status.Cancelled || status == Status.LateCancelled;

    private Task<List<VisitWithWaitlistInfo>> GetScheduleAsync(string clientId, DateOnly from, DateOnly to, CancellationToken ct) =>
        MindbodyGateway.ReadAllAsync<VisitWithWaitlistInfo>(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, token) => c.ClientApi.GetClientSchedule(new GetClientScheduleOperationRequest
            {
                SiteId = site,
                Authorization = token,
                RequestClientId = clientId,
                RequestStartDate = MindbodyGateway.ToSiteWallRequest(from),
                RequestEndDate = MindbodyGateway.ToSiteWallRequestEnd(to),
                RequestLimit = limit,
                RequestOffset = offset,
            }, null, ct), "reading the shopper's bookings", ct);
            return (r?.Visits, r?.PaginationResponse?.TotalResults);
        });

    private Task<List<Appointment>> GetAppointmentsAsync(string clientId, DateOnly from, DateOnly to, IReadOnlyList<int>? ids, CancellationToken ct) =>
        MindbodyGateway.ReadAllAsync<Appointment>(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, token) => c.AppointmentApi.GetStaffAppointments(new GetStaffAppointmentsOperationRequest
            {
                SiteId = site,
                Authorization = token,
                RequestClientId = clientId,
                RequestAppointmentIds = ids,
                RequestStartDate = MindbodyGateway.ToSiteWallRequest(from),
                RequestEndDate = MindbodyGateway.ToSiteWallRequestEnd(to),
                RequestLimit = limit,
                RequestOffset = offset,
            }, null, ct), "reading the shopper's sessions", ct);
            return (r?.Appointments, r?.PaginationResponse?.TotalResults);
        });

    // =============================================================== trainers

    public async Task<List<TrainerDto>> ListTrainersAsync(CancellationToken ct)
    {
        var staff = await MindbodyGateway.ReadAllAsync<Staff>(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, token) => c.StaffApi.GetStaff(new GetStaffOperationRequest
            {
                SiteId = site,
                Authorization = token,
                RequestLimit = limit,
                RequestOffset = offset,
            }, null, ct), "reading the trainers", ct);
            return (r?.StaffMembers, r?.PaginationResponse?.TotalResults);
        });

        return staff
            .Where(s => s.Id is not null && (s.ClassTeacher == true || s.AppointmentInstructor == true))
            .GroupBy(s => s.Id)
            .Select(g => g.First())
            .OrderBy(s => s.LastName, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.FirstName, StringComparer.OrdinalIgnoreCase)
            .Select(s => new TrainerDto
            {
                Id = s.Id!.Value,
                FirstName = s.FirstName,
                LastName = s.LastName,
                DisplayName = s.DisplayName ?? s.Name,
                Email = s.Email,
                MobilePhone = s.MobilePhone,
                HomePhone = s.HomePhone,
                WorkPhone = s.WorkPhone,
                Address = s.Address,
                City = s.City,
                State = s.State,
                PostalCode = s.PostalCode,
                Country = s.Country,
                Bio = s.Bio,
                ImageUrl = s.ImageUrl,
                IsActive = s.Active == true,
                TeachesClasses = s.ClassTeacher == true,
                OffersAppointments = s.AppointmentInstructor == true,
            })
            .ToList();
    }

    // =============================================================== clients

    private async Task<ApplicationUser> GetUserAsync(string? userName)
    {
        var user = string.IsNullOrWhiteSpace(userName) ? null : await _users.FindByNameAsync(userName);
        return user ?? throw new StudioException(HttpStatusCode.Unauthorized, "The signed-in shopper is not known.");
    }

    private static string ClientIdSafe(ApplicationUser user) =>
        Guid.TryParse(user.Id, out var guid) ? "eshop" + guid.ToString("N") : "eshop" + Regex.Replace(user.Id, "[^A-Za-z0-9]", "");

    private async Task<Client?> FindClientAsync(ApplicationUser user, CancellationToken ct)
    {
        var id = ClientIdSafe(user);
        var byId = await _mb.CallAsync((c, site, token) => c.ClientApi.GetClients(new GetClientsOperationRequest
        {
            SiteId = site,
            Authorization = token,
            RequestClientIDs = new[] { id },
            RequestIncludeInactive = true,
        }, null, ct), "looking up the shopper's studio client", ct);
        var found = byId?.Clients?.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        if (found is not null) return ToClient(found);

        // A client the studio already held under this e-mail address (created by staff, say) is the same person.
        var email = user.Email;
        if (string.IsNullOrWhiteSpace(email)) return null;
        var byEmail = await _mb.CallAsync((c, site, token) => c.ClientApi.GetClients(new GetClientsOperationRequest
        {
            SiteId = site,
            Authorization = token,
            RequestSearchText = email,
            RequestLimit = 50,
        }, null, ct), "looking up the shopper's studio client", ct);
        var match = byEmail?.Clients?.Where(c => string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(c.Id))
            .OrderBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault();
        return match is null ? null : ToClient(match);
    }

    private static Client ToClient(ClientWithSuspensionInfo c) => new() { Id = c.Id, Email = c.Email, FirstName = c.FirstName, LastName = c.LastName };

    private async Task<string> EnsureClientAsync(ApplicationUser user, ClientDetailsRequest? details, CancellationToken ct)
    {
        var existing = await FindClientAsync(user, ct);
        if (existing?.Id is not null) return existing.Id;

        // Create each shopper's client exactly once, even when the app repeats the first booking.
        using var _ = await _state.Locks.AcquireAsync("user:" + user.Id);
        existing = await FindClientAsync(user, ct);
        if (existing?.Id is not null) return existing.Id;

        var request = await ValidateNewClientAsync(user, details, ct);
        StudioException failure;
        try
        {
            var created = await _mb.CallAsync((c, site, token) => c.ClientApi.AddClient(new AddClientOperationRequest
            {
                SiteId = site,
                Authorization = token,
                Body = request,
            }), "creating the studio client", CancellationToken.None);

            var id = created?.Client?.Id;
            if (string.IsNullOrEmpty(id))
                throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not confirm the new client.");
            return id;
        }
        catch (StudioException ex)
        {
            failure = ex;
        }

        // The client may have appeared meanwhile (an earlier attempt completed after its caller gave up).
        existing = await FindClientAsync(user, ct);
        if (existing?.Id is not null) return existing.Id;
        throw failure;
    }

    private async Task<AddClientRequest> ValidateNewClientAsync(ApplicationUser user, ClientDetailsRequest? d, CancellationToken ct)
    {
        d ??= new ClientDetailsRequest();
        var missing = new List<string>();
        if (d.HomeLocationId is null or <= 0) missing.Add("homeLocationId");
        if (string.IsNullOrWhiteSpace(d.StreetAddress)) missing.Add("streetAddress");
        if (string.IsNullOrWhiteSpace(d.City)) missing.Add("city");
        if (string.IsNullOrWhiteSpace(d.State)) missing.Add("state");
        if (string.IsNullOrWhiteSpace(d.PostalCode)) missing.Add("postalCode");
        if (string.IsNullOrWhiteSpace(d.DateOfBirth)) missing.Add("dateOfBirth");
        if (string.IsNullOrWhiteSpace(d.MobilePhone)) missing.Add("mobilePhone");
        if (string.IsNullOrWhiteSpace(d.ReferralSource)) missing.Add("referralSource");
        if (missing.Count > 0)
            throw StudioException.BadRequest(
                "This is your first booking with the studio, so these details are required: " + string.Join(", ", missing) + ".");

        var dob = ParseDate(d.DateOfBirth, "dateOfBirth")!.Value;
        var today = DateOnly.FromDateTime(_mb.UtcNow.UtcDateTime);
        if (dob >= today || dob.Year < today.Year - 120)
            throw StudioException.BadRequest("'dateOfBirth' is not a plausible date of birth.");
        if (!PhonePattern.IsMatch(d.MobilePhone!.Trim()))
            throw StudioException.BadRequest("'mobilePhone' is not a valid phone number.");
        if (string.IsNullOrWhiteSpace(user.Email))
            throw StudioException.Conflict("Your eShop identity has no e-mail address, which the studio requires.");

        var locations = await _mb.CallAsync((c, site, token) => c.SiteApi.GetLocations(new GetLocationsOperationRequest
        {
            SiteId = site,
            Authorization = token,
        }, null, ct), "reading the studio locations", ct);
        var location = locations?.Locations?.FirstOrDefault(l => l.Id == d.HomeLocationId);
        if (location is null)
            throw StudioException.BadRequest($"There is no studio location {d.HomeLocationId}.");

        var referrals = await _mb.CallAsync((c, site, token) => c.ClientApi.GetClientReferralTypes(new GetClientReferralTypesOperationRequest
        {
            SiteId = site,
            Authorization = token,
        }, null, ct), "reading the referral types", ct);
        var allowed = referrals?.ReferralTypes ?? Array.Empty<string>();
        var referral = allowed.FirstOrDefault(r => string.Equals(r, d.ReferralSource!.Trim(), StringComparison.OrdinalIgnoreCase));
        if (referral is null)
            throw StudioException.BadRequest("'referralSource' must be one of: " + string.Join(", ", allowed) + ".");

        var local = user.Email!.Split('@')[0];
        return new AddClientRequest
        {
            NewId = ClientIdSafe(user),
            FirstName = string.IsNullOrWhiteSpace(d.FirstName) ? local : d.FirstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(d.LastName) ? "Shopper" : d.LastName.Trim(),
            Email = user.Email,
            AddressLine1 = d.StreetAddress!.Trim(),
            City = d.City!.Trim(),
            State = d.State!.Trim(),
            PostalCode = d.PostalCode!.Trim(),
            BirthDate = MindbodyGateway.ToSiteWallRequest(dob),
            MobilePhone = d.MobilePhone.Trim(),
            ReferredBy = referral,
            HomeLocation = new Location { Id = location.Id },
        };
    }

    // =============================================================== helpers

    /// <summary>Accepts <c>yyyy-MM-dd</c> or a full ISO-8601 date-time (its written date is used).</summary>
    private static DateOnly? ParseDate(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        if (text.Contains('T') && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            return DateOnly.FromDateTime(dto.DateTime);
        throw StudioException.BadRequest($"'{name}' must be a date like 2026-11-30.");
    }
}
