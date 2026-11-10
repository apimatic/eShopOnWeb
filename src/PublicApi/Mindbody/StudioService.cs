using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Mindbody;

public record NewClientDetails
{
    public int? HomeLocationId { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? StreetAddress { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public string? MobileNumber { get; init; }
    public string? ReferralSource { get; init; }
}

public record StudioLocation(int Id, string? Name);

public record StudioClass(
    int Id, string? Name, DateTimeOffset StartTime, DateTimeOffset EndTime,
    StudioLocation? Location, string? Instructor, int AvailableSpots);

public record StudioSlot(DateTimeOffset StartTime, DateTimeOffset EndTime);

public record StudioBooking(
    string BookingId, string Kind, DateTimeOffset StartTime, DateTimeOffset EndTime,
    int? ClassId, int? SessionTypeId, int? LocationId, long? StaffId, string? Name);

public record BookingResult(StudioBooking Booking, bool Created);

public record StudioTrainer(
    long Id, string? FirstName, string? LastName, string? DisplayName, string? Email,
    string? MobilePhone, string? HomePhone, string? WorkPhone, string? Address, string? City,
    string? State, string? PostalCode, string? Country, string? Bio, string? ImageUrl, bool Active,
    bool TeachesClasses, bool OffersAppointments);

/// <summary>Studio business logic on top of the Mindbody HTTP API.</summary>
public class StudioService
{
    private const int DEFAULT_RANGE_DAYS = 7;
    private const int MAX_RANGE_DAYS = 366;
    private const int STAFF_CHUNK = 20;
    private static readonly TimeSpan SlowCacheTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan StaffCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly SemaphoreSlim[] ClientLocks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly MindbodyClient _mb;
    private readonly IMemoryCache _cache;
    private readonly ILogger<StudioService> _logger;

    public StudioService(MindbodyClient mb, IMemoryCache cache, ILogger<StudioService> logger)
    {
        _mb = mb;
        _cache = cache;
        _logger = logger;
    }

    // ---------------------------------------------------------------- time zone

    private async Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue("mb:tz", out TimeZoneInfo? cached) && cached is not null) return cached;
        try
        {
            using var doc = await _mb.GetJsonAsync("site/sites", "", ct);
            var id = doc.RootElement.GetProperty("Sites")[0].GetProperty("TimeZone").GetString();
            var tz = TimeZoneInfo.FindSystemTimeZoneById(id!);
            _cache.Set("mb:tz", tz, SlowCacheTtl);
            _cache.Set("mb:tz:stale", tz);
            return tz;
        }
        catch (Exception ex)
        {
            if (_cache.TryGetValue("mb:tz:stale", out TimeZoneInfo? stale) && stale is not null) return stale;
            _logger.LogError(ex, "Could not determine the studio time zone");
            throw new StudioException(HttpStatusCode.BadGateway, "The studio's time zone could not be determined.");
        }
    }

    private static DateTimeOffset ToOffset(DateTime local, TimeZoneInfo tz)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(local)) local = local.AddHours(1);
        var offset = tz.IsAmbiguousTime(local) ? tz.GetAmbiguousTimeOffsets(local).Max() : tz.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static DateTime ToLocal(DateTimeOffset instant, TimeZoneInfo tz)
        => TimeZoneInfo.ConvertTime(instant, tz).DateTime;

    private static DateTime NowLocal(TimeZoneInfo tz) => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime;

    private static (DateOnly From, DateOnly To) ResolveRange(DateOnly? from, DateOnly? to, DateOnly today, bool defaultWindow)
    {
        var start = from ?? (defaultWindow ? today : throw new StudioException(HttpStatusCode.BadRequest, "'from' is required."));
        var end = to ?? (defaultWindow ? (from is null ? today.AddDays(DEFAULT_RANGE_DAYS) : start.AddDays(DEFAULT_RANGE_DAYS)) : throw new StudioException(HttpStatusCode.BadRequest, "'to' is required."));
        if (end < start) throw new StudioException(HttpStatusCode.BadRequest, "'to' must not be before 'from'.");
        if (end.DayNumber - start.DayNumber > MAX_RANGE_DAYS)
            throw new StudioException(HttpStatusCode.BadRequest, $"The date range may not exceed {MAX_RANGE_DAYS} days.");
        return (start, end);
    }

    // ---------------------------------------------------------------- classes

    private static bool CanShopperAttend(MbClass c, DateTime nowLocal)
        => !c.IsCanceled && c.Active && c.IsAvailable
           && c.WebCapacity > 0 && c.WebBooked < c.WebCapacity
           && (c.MaxCapacity <= 0 || c.TotalBooked < c.MaxCapacity)
           && c.EndDateTime > nowLocal;

    private static StudioClass ToStudioClass(MbClass c, TimeZoneInfo tz) => new(
        c.Id, c.ClassDescription?.Name, ToOffset(c.StartDateTime, tz), ToOffset(c.EndDateTime, tz),
        c.Location is null ? null : new StudioLocation(c.Location.Id, c.Location.Name),
        c.Staff?.DisplayName ?? c.Staff?.Name,
        Math.Max(0, Math.Min(c.WebCapacity - c.WebBooked, c.MaxCapacity > 0 ? c.MaxCapacity - c.TotalBooked : int.MaxValue)));

    public async Task<List<StudioClass>> ListClassesAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var tz = await GetTimeZoneAsync(ct);
        var now = NowLocal(tz);
        var (start, end) = ResolveRange(from, to, DateOnly.FromDateTime(now), defaultWindow: true);
        var classes = await _mb.GetAllAsync<MbClass>("class/classes", "Classes", MindbodyClient.FormatQuery(
            ("StartDateTime", start.ToDateTime(TimeOnly.MinValue)),
            ("EndDateTime", end.ToDateTime(new TimeOnly(23, 59, 59)))), ct);
        return classes
            .Where(c => CanShopperAttend(c, now))
            .OrderBy(c => c.StartDateTime).ThenBy(c => c.Id)
            .Select(c => ToStudioClass(c, tz))
            .ToList();
    }

    private async Task<MbClass> GetClassAsync(int classId, TimeZoneInfo tz, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(NowLocal(tz));
        var classes = await _mb.GetAllAsync<MbClass>("class/classes", "Classes", MindbodyClient.FormatQuery(
            ("ClassIds", classId),
            ("StartDateTime", today.AddDays(-2).ToDateTime(TimeOnly.MinValue)),
            ("EndDateTime", today.AddDays(800).ToDateTime(TimeOnly.MinValue))), ct);
        return classes.FirstOrDefault(c => c.Id == classId)
               ?? throw new StudioException(HttpStatusCode.NotFound, $"Class {classId} was not found in the studio timetable.");
    }

    // ---------------------------------------------------------------- session times

    private async Task<MbSessionType> GetSessionTypeAsync(int sessionTypeId, CancellationToken ct)
    {
        if (!_cache.TryGetValue("mb:sessiontypes", out List<MbSessionType>? types) || types is null)
        {
            types = await _mb.GetAllAsync<MbSessionType>("site/sessiontypes", "SessionTypes", "", ct);
            _cache.Set("mb:sessiontypes", types, SlowCacheTtl);
        }
        var type = types.FirstOrDefault(t => t.Id == sessionTypeId)
                   ?? throw new StudioException(HttpStatusCode.NotFound, $"Session type {sessionTypeId} does not exist.");
        if (!string.Equals(type.Type, "Appointment", StringComparison.OrdinalIgnoreCase) || type.DefaultTimeLength is not > 0)
            throw new StudioException(HttpStatusCode.UnprocessableEntity, $"Session type {sessionTypeId} cannot be booked as a one-to-one session.");
        return type;
    }

    private async Task<List<long>> GetAppointmentInstructorIdsAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue("mb:instructors", out List<long>? ids) && ids is not null) return ids;
        var staff = await _mb.GetAllAsync<MbStaff>("staff/staff", "StaffMembers", "", ct);
        ids = staff.Where(s => s.AppointmentInstructor).Select(s => s.Id).ToList();
        _cache.Set("mb:instructors", ids, StaffCacheTtl);
        return ids;
    }

    public async Task<List<StudioSlot>> ListSlotsAsync(int sessionTypeId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (sessionTypeId <= 0) throw new StudioException(HttpStatusCode.BadRequest, "'sessionTypeId' is required.");
        var tz = await GetTimeZoneAsync(ct);
        var now = NowLocal(tz);
        var (start, end) = ResolveRange(from, to, DateOnly.FromDateTime(now), defaultWindow: false);
        var type = await GetSessionTypeAsync(sessionTypeId, ct);
        var length = TimeSpan.FromMinutes(type.DefaultTimeLength!.Value);
        var programId = await GetProgramIdAsync(sessionTypeId, ct);

        var windows = new List<MbAvailability>();
        var instructorIds = await GetAppointmentInstructorIdsAsync(ct);
        foreach (var chunk in instructorIds.Chunk(STAFF_CHUNK))
        {
            var query = MindbodyClient.FormatQuery(
                ("StartDate", start.ToString("yyyy-MM-dd")),
                ("EndDate", end.ToString("yyyy-MM-dd")))
                + string.Concat(chunk.Select(id => $"&StaffIds={id}"));
            using var doc = await _mb.GetJsonAsync("appointment/scheduleitems", query, ct);
            CollectAvailabilities(doc, programId, windows);
        }

        var firstDay = start.ToDateTime(TimeOnly.MinValue);
        var lastDay = end.ToDateTime(TimeOnly.MaxValue);
        var starts = new SortedSet<DateTime>();
        foreach (var w in windows)
        {
            for (var t = w.StartDateTime; t + length <= w.EndDateTime; t += length)
            {
                if (t >= firstDay && t <= lastDay && t > now) starts.Add(t);
            }
        }
        return starts.Select(t => new StudioSlot(ToOffset(t, tz), ToOffset(t + length, tz))).ToList();
    }

    private static void CollectAvailabilities(JsonDocument doc, int? programId, List<MbAvailability> windows)
    {
        if (doc.RootElement.TryGetProperty("StaffMembers", out var staff))
        {
            foreach (var member in staff.EnumerateArray())
            {
                if (!member.TryGetProperty("Availabilities", out var availabilities)) continue;
                foreach (var a in availabilities.EnumerateArray())
                {
                    if (programId is not null && !HasProgram(a, programId.Value)) continue;
                    var parsed = MindbodyClient.Read<MbAvailability>(a);
                    if (parsed is not null) windows.Add(parsed);
                }
            }
        }
    }

    private static bool HasProgram(JsonElement availability, int programId)
    {
        if (!availability.TryGetProperty("Programs", out var programs) || programs.ValueKind != JsonValueKind.Array) return true;
        foreach (var p in programs.EnumerateArray())
        {
            if (p.TryGetProperty("Id", out var id) && id.TryGetInt32(out var value) && value == programId) return true;
        }
        return programs.GetArrayLength() == 0;
    }

    private async Task<int?> GetProgramIdAsync(int sessionTypeId, CancellationToken ct)
    {
        using var doc = await _mb.GetJsonAsync("site/sessiontypes", MindbodyClient.FormatQuery(("SessionTypeIds", sessionTypeId)), ct);
        foreach (var t in doc.RootElement.GetProperty("SessionTypes").EnumerateArray())
        {
            if (t.TryGetProperty("Id", out var id) && id.GetInt32() == sessionTypeId
                && t.TryGetProperty("ProgramId", out var program) && program.TryGetInt32(out var programId))
                return programId;
        }
        return null;
    }

    // ---------------------------------------------------------------- clients

    private static SemaphoreSlim LockFor(string email)
        => ClientLocks[(uint)StringComparer.OrdinalIgnoreCase.GetHashCode(email) % (uint)ClientLocks.Length];

    private async Task<MbClient?> FindClientAsync(string email, CancellationToken ct)
    {
        var clients = await _mb.GetAllAsync<MbClient>("client/clients", "Clients", MindbodyClient.FormatQuery(("SearchText", email)), ct);
        return clients.FirstOrDefault(c => string.Equals(c.Email?.Trim(), email, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<HashSet<int>> GetLocationIdsAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue("mb:locations", out HashSet<int>? ids) && ids is not null) return ids;
        var locations = await _mb.GetAllAsync<MbLocation>("site/locations", "Locations", "", ct);
        ids = locations.Select(l => l.Id).ToHashSet();
        _cache.Set("mb:locations", ids, SlowCacheTtl);
        return ids;
    }

    private async Task<MbClient> FindOrCreateClientAsync(string email, NewClientDetails? details, CancellationToken ct)
    {
        var existing = await FindClientAsync(email, ct);
        if (existing is not null) return existing;

        var missing = new List<string>();
        if (details?.HomeLocationId is null) missing.Add("homeLocationId");
        if (string.IsNullOrWhiteSpace(details?.StreetAddress)) missing.Add("streetAddress");
        if (string.IsNullOrWhiteSpace(details?.City)) missing.Add("city");
        if (string.IsNullOrWhiteSpace(details?.State)) missing.Add("state");
        if (string.IsNullOrWhiteSpace(details?.PostalCode)) missing.Add("postalCode");
        if (details?.DateOfBirth is null) missing.Add("dateOfBirth");
        if (string.IsNullOrWhiteSpace(details?.MobileNumber)) missing.Add("mobileNumber");
        if (string.IsNullOrWhiteSpace(details?.ReferralSource)) missing.Add("referralSource");
        if (missing.Count > 0)
            throw new StudioException(HttpStatusCode.BadRequest,
                "This is the first studio booking for this shopper, so these details are required: " + string.Join(", ", missing) + ".");

        if (details!.DateOfBirth!.Value > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new StudioException(HttpStatusCode.BadRequest, "dateOfBirth must be in the past.");
        if (!(await GetLocationIdsAsync(ct)).Contains(details.HomeLocationId!.Value))
            throw new StudioException(HttpStatusCode.BadRequest, $"homeLocationId {details.HomeLocationId} is not a studio location.");

        var local = email.Split('@')[0];
        using var doc = await _mb.PostAsync("client/addclient", new
        {
            FirstName = string.IsNullOrWhiteSpace(details.FirstName) ? local : details.FirstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(details.LastName) ? "Shopper" : details.LastName.Trim(),
            Email = email,
            AddressLine1 = details.StreetAddress!.Trim(),
            City = details.City!.Trim(),
            State = details.State!.Trim(),
            PostalCode = details.PostalCode!.Trim(),
            BirthDate = details.DateOfBirth.Value.ToString("yyyy-MM-dd"),
            MobilePhone = details.MobileNumber!.Trim(),
            ReferredBy = details.ReferralSource!.Trim(),
            HomeLocation = new { Id = details.HomeLocationId.Value }
        }, ct);
        var client = doc.RootElement.TryGetProperty("Client", out var c) ? MindbodyClient.Read<MbClient>(c) : null;
        if (client?.Id is null) throw new StudioException(HttpStatusCode.BadGateway, "The studio could not create the client record.");
        return client;
    }

    private async Task<List<MbVisit>> GetVisitsAsync(string clientId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var visits = await _mb.GetAllAsync<MbVisit>("client/clientvisits", "Visits", MindbodyClient.FormatQuery(
            ("ClientId", clientId),
            ("StartDate", from.ToString("yyyy-MM-dd")),
            ("EndDate", to.ToString("yyyy-MM-dd"))), ct);
        return visits.Where(v => !v.LateCancelled
            && !string.Equals(v.AppointmentStatus, "Cancelled", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(v.AppointmentStatus, "LateCancelled", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    // ---------------------------------------------------------------- booking

    public Task<BookingResult> BookClassAsync(string shopperEmail, int classId, NewClientDetails? details, bool staffOverride, CancellationToken ct)
        => WithClientLockAsync(shopperEmail, async () =>
        {
            if (classId <= 0) throw new StudioException(HttpStatusCode.BadRequest, "'classId' is required.");
            var tz = await GetTimeZoneAsync(ct);
            var now = NowLocal(tz);
            var cls = await GetClassAsync(classId, tz, ct);
            if (staffOverride)
            {
                if (cls.IsCanceled) throw new StudioException(HttpStatusCode.Conflict, "This class has been cancelled.");
                if (cls.EndDateTime <= now) throw new StudioException(HttpStatusCode.Conflict, "This class has already finished.");
            }

            var client = await FindClientAsync(shopperEmail, ct);
            if (client is not null)
            {
                var day = DateOnly.FromDateTime(cls.StartDateTime);
                var already = (await GetVisitsAsync(client.Id!, day, day, ct)).FirstOrDefault(v => v.ClassId == classId);
                if (already is not null) return new BookingResult(ToClassBooking(already, cls, tz), false);
            }

            if (!staffOverride && !CanShopperAttend(cls, now))
                throw new StudioException(HttpStatusCode.Conflict, "This class is not open for booking (cancelled, full, finished or not bookable online).");

            client ??= await FindOrCreateClientAsync(shopperEmail, details, ct);
            using var doc = await _mb.PostAsync("class/addclienttoclass",
                new { ClientId = client.Id, ClassId = classId, SendEmail = false }, ct);
            var visit = doc.RootElement.TryGetProperty("Visit", out var v) ? MindbodyClient.Read<MbVisit>(v) : null;
            if (visit is null || visit.Id == 0) throw new StudioException(HttpStatusCode.BadGateway, "The studio did not confirm the booking.");
            return new BookingResult(ToClassBooking(visit, cls, tz), true);
        }, ct);

    private static StudioBooking ToClassBooking(MbVisit visit, MbClass cls, TimeZoneInfo tz) => new(
        visit.Id.ToString(), "class", ToOffset(cls.StartDateTime, tz), ToOffset(cls.EndDateTime, tz),
        cls.Id, null, cls.Location?.Id, cls.Staff?.Id, cls.ClassDescription?.Name);

    public Task<BookingResult> BookAppointmentAsync(string shopperEmail, int sessionTypeId, DateTimeOffset startTime,
        int? locationId, long? staffId, NewClientDetails? details, CancellationToken ct)
        => WithClientLockAsync(shopperEmail, async () =>
        {
            if (sessionTypeId <= 0) throw new StudioException(HttpStatusCode.BadRequest, "'sessionTypeId' is required.");
            var tz = await GetTimeZoneAsync(ct);
            var type = await GetSessionTypeAsync(sessionTypeId, ct);
            var length = TimeSpan.FromMinutes(type.DefaultTimeLength!.Value);
            var localStart = ToLocal(startTime, tz);
            if (startTime <= DateTimeOffset.UtcNow) throw new StudioException(HttpStatusCode.UnprocessableEntity, "The session start time is in the past.");
            var day = DateOnly.FromDateTime(localStart);

            var client = await FindClientAsync(shopperEmail, ct);
            if (client is not null)
            {
                var already = (await GetVisitsAsync(client.Id!, day, day, ct))
                    .FirstOrDefault(v => v.AppointmentId > 0 && v.StartDateTime == localStart && (v.VisitType == 0 || v.VisitType == sessionTypeId));
                if (already is not null)
                    return new BookingResult(new StudioBooking(already.AppointmentId.ToString(), "appointment",
                        ToOffset(already.StartDateTime, tz), ToOffset(already.EndDateTime, tz), null, sessionTypeId,
                        already.LocationId, already.StaffId, type.Name), false);
            }

            var bookable = await _mb.GetAllAsync<MbAvailability>("appointment/bookableitems", "Availabilities", MindbodyClient.FormatQuery(
                ("SessionTypeIds", sessionTypeId),
                ("StartDate", day.ToString("yyyy-MM-dd")),
                ("EndDate", day.ToString("yyyy-MM-dd"))), ct);
            var match = bookable.FirstOrDefault(a => a.Staff is not null && a.Location is not null
                && a.StartDateTime <= localStart && localStart + length <= a.EndDateTime
                && (staffId is null || a.Staff.Id == staffId)
                && (locationId is null || a.Location.Id == locationId));
            if (match is null)
                throw new StudioException(HttpStatusCode.Conflict, "No trainer is free for that session at the requested time.");

            client ??= await FindOrCreateClientAsync(shopperEmail, details, ct);
            using var doc = await _mb.PostAsync("appointment/addappointment", new
            {
                ClientId = client.Id,
                LocationId = match.Location!.Id,
                SessionTypeId = sessionTypeId,
                StaffId = match.Staff!.Id,
                StartDateTime = localStart.ToString("yyyy-MM-dd'T'HH:mm:ss"),
                SendEmail = false
            }, ct);
            var appointment = doc.RootElement.TryGetProperty("Appointment", out var a) ? MindbodyClient.Read<MbAppointment>(a) : null;
            if (appointment is null || appointment.Id == 0) throw new StudioException(HttpStatusCode.BadGateway, "The studio did not confirm the booking.");
            return new BookingResult(new StudioBooking(appointment.Id.ToString(), "appointment",
                ToOffset(appointment.StartDateTime, tz), ToOffset(appointment.EndDateTime, tz), null, sessionTypeId,
                appointment.LocationId, appointment.StaffId, type.Name), true);
        }, ct);

    // ---------------------------------------------------------------- cancel

    /// <summary>Cancels the shopper's own class booking. Cancelling a booking that is already gone succeeds.</summary>
    public Task CancelClassBookingAsync(string shopperEmail, long bookingId, CancellationToken ct)
        => WithClientLockAsync(shopperEmail, async () =>
        {
            var client = await FindClientAsync(shopperEmail, ct)
                         ?? throw new StudioException(HttpStatusCode.NotFound, "You have no studio bookings.");
            var visit = (await GetOwnVisitsAsync(client, ct)).FirstOrDefault(v => v.Id == bookingId && v.ClassId > 0);
            if (visit is null) return true; // already cancelled (e.g. a retried request)
            using var doc = await _mb.PostAsync("class/removeclientfromclass",
                new { ClientId = client.Id, ClassId = visit.ClassId, VisitIds = new[] { visit.Id }, SendEmail = false, LateCancel = false }, ct);
            return true;
        }, ct);

    /// <summary>Cancels the shopper's own appointment. Cancelling an appointment that is already gone succeeds.</summary>
    public Task CancelAppointmentBookingAsync(string shopperEmail, long bookingId, CancellationToken ct)
        => WithClientLockAsync(shopperEmail, async () =>
        {
            var client = await FindClientAsync(shopperEmail, ct)
                         ?? throw new StudioException(HttpStatusCode.NotFound, "You have no studio bookings.");
            var visit = (await GetOwnVisitsAsync(client, ct)).FirstOrDefault(v => v.AppointmentId == bookingId);
            if (visit is null)
            {
                // Not one of the caller's active appointments: either already cancelled, or someone else's.
                var today = DateOnly.FromDateTime(NowLocal(await GetTimeZoneAsync(ct)));
                var others = await _mb.GetAllAsync<MbAppointment>("appointment/staffappointments", "Appointments",
                    MindbodyClient.FormatQuery(
                        ("AppointmentIds", bookingId),
                        ("StartDate", today.AddDays(-1).ToString("yyyy-MM-dd")),
                        ("EndDate", today.AddDays(800).ToString("yyyy-MM-dd"))), ct);
                if (others.Any(a => a.Id == bookingId && !string.Equals(a.ClientId, client.Id, StringComparison.Ordinal)))
                    throw new StudioException(HttpStatusCode.NotFound, "Booking not found.");
                return true;
            }
            using var doc = await _mb.PostAsync("appointment/updateappointment",
                new { AppointmentId = bookingId, Execute = "Cancel", SendEmail = false }, ct);
            return true;
        }, ct);

    private async Task<List<MbVisit>> GetOwnVisitsAsync(MbClient client, CancellationToken ct)
    {
        var tz = await GetTimeZoneAsync(ct);
        var today = DateOnly.FromDateTime(NowLocal(tz));
        return await GetVisitsAsync(client.Id!, today.AddDays(-1), today.AddDays(800), ct);
    }

    // ---------------------------------------------------------------- trainers

    public async Task<List<StudioTrainer>> ListTrainersAsync(CancellationToken ct)
    {
        var staff = await _mb.GetAllAsync<MbStaff>("staff/staff", "StaffMembers", "", ct);
        return staff
            .Where(s => s.AppointmentInstructor || s.ClassTeacher)
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ThenBy(s => s.Id)
            .Select(s => new StudioTrainer(s.Id, s.FirstName, s.LastName, s.DisplayName ?? s.Name, s.Email,
                s.MobilePhone, s.HomePhone, s.WorkPhone, s.Address, s.City, s.State, s.PostalCode, s.Country,
                s.Bio, s.ImageUrl, s.Active, s.ClassTeacher, s.AppointmentInstructor))
            .ToList();
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<T> WithClientLockAsync<T>(string email, Func<Task<T>> action, CancellationToken ct)
    {
        var gate = LockFor(email);
        await gate.WaitAsync(ct);
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }
}
