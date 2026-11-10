using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>Studio features, built on the Mindbody API.</summary>
public class StudioService
{
    private const int MAX_RANGE_DAYS = 366;
    private const int LOCK_STRIPES = 64;
    private static readonly DateTime FarPast = new(2000, 1, 1);

    private readonly MindbodyApi _api;
    private readonly IMemoryCache _cache;
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, LOCK_STRIPES).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public StudioService(MindbodyApi api, IMemoryCache cache)
    {
        _api = api;
        _cache = cache;
    }

    // ------------------------------------------------------------------ time zone

    private async Task<TimeZoneInfo> SiteTimeZoneAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue("studio:tz", out TimeZoneInfo? cached) && cached is not null) return cached;
        var page = await _api.GetAsync<MbSitesPage>("site/sites", null, ct);
        var id = page.Sites?.FirstOrDefault()?.TimeZone;
        if (string.IsNullOrWhiteSpace(id))
            throw new StudioException(502, "The studio system did not report its time zone.");
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new StudioException(502, "The studio's time zone is not known to this server.");
        }
        _cache.Set("studio:tz", tz, TimeSpan.FromHours(6));
        return tz;
    }

    private static DateTimeOffset ToOffset(TimeZoneInfo tz, DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(local)) local = local.AddHours(1);
        var offset = tz.IsAmbiguousTime(local) ? tz.BaseUtcOffset : tz.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static DateTime ToLocal(TimeZoneInfo tz, DateTimeOffset when) => TimeZoneInfo.ConvertTime(when, tz).DateTime;

    private static DateTime Now(TimeZoneInfo tz) => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime;

    private static string Iso(DateTime d) => d.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static (DateOnly from, DateOnly to) Range(DateOnly? from, DateOnly? to, DateOnly today, bool required)
    {
        if (required && (from is null || to is null))
            throw StudioException.BadRequest("Both 'from' and 'to' dates are required (yyyy-MM-dd).");
        var f = from ?? (to is not null && to.Value < today ? to.Value : today);
        var t = to ?? f.AddDays(7);
        if (t < f) throw StudioException.BadRequest("'to' must not be before 'from'.");
        if (t.DayNumber - f.DayNumber >= MAX_RANGE_DAYS)
            throw StudioException.BadRequest($"The date range may not exceed {MAX_RANGE_DAYS} days.");
        return (f, t);
    }

    // ------------------------------------------------------------------ 1. classes

    private static bool Attendable(MbClass c, DateTime now)
        => !c.IsCanceled && c.Active && c.IsAvailable
           && !string.Equals(c.ClassDescription?.Program?.ScheduleType, "Enrollment", StringComparison.OrdinalIgnoreCase)
           && (c.WebCapacity ?? 0) > 0 && (c.WebBooked ?? 0) < c.WebCapacity
           && c.StartDateTime > now;

    public async Task<List<ClassDto>> ListClassesAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var tz = await SiteTimeZoneAsync(ct);
        var now = Now(tz);
        var (f, t) = Range(from, to, DateOnly.FromDateTime(now), false);
        var classes = await _api.GetAllAsync<MbClassesPage, MbClass>("class/classes", new()
        {
            new("StartDateTime", Day(f)),
            new("EndDateTime", Day(t))
        }, p => p.Classes, ct);

        return classes
            .Where(c => Attendable(c, now))
            .Where(c => DateOnly.FromDateTime(c.StartDateTime) >= f && DateOnly.FromDateTime(c.StartDateTime) <= t)
            .OrderBy(c => c.StartDateTime).ThenBy(c => c.Id)
            .Select(c => new ClassDto(c.Id, c.ClassDescription?.Name, ToOffset(tz, c.StartDateTime), ToOffset(tz, c.EndDateTime),
                c.Location?.Id, c.Location?.Name, c.Staff?.DisplayName ?? c.Staff?.Name,
                Math.Max(0, (c.WebCapacity ?? 0) - (c.WebBooked ?? 0))))
            .ToList();
    }

    // ------------------------------------------------------------------ 2. session times

    private async Task<MbSessionType> GetSessionTypeAsync(int id, CancellationToken ct)
    {
        if (!_cache.TryGetValue("studio:sessiontypes", out List<MbSessionType>? types) || types is null)
        {
            types = await _api.GetAllAsync<MbSessionTypesPage, MbSessionType>("site/sessiontypes", new(), p => p.SessionTypes, ct);
            _cache.Set("studio:sessiontypes", types, TimeSpan.FromMinutes(10));
        }
        var st = types.FirstOrDefault(x => x.Id == id) ?? throw StudioException.NotFound($"Session type {id} does not exist.");
        if (string.Equals(st.Type, "Class", StringComparison.OrdinalIgnoreCase) || string.Equals(st.Type, "Enrollment", StringComparison.OrdinalIgnoreCase))
            throw StudioException.BadRequest($"Session type {id} is not a one-to-one session type.");
        return st;
    }

    public async Task<List<SlotDto>> ListSlotsAsync(int? sessionTypeId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (sessionTypeId is null) throw StudioException.BadRequest("'sessionTypeId' is required.");
        var tz = await SiteTimeZoneAsync(ct);
        var now = Now(tz);
        var (f, t) = Range(from, to, DateOnly.FromDateTime(now), true);
        var st = await GetSessionTypeAsync(sessionTypeId.Value, ct);
        var length = st.DefaultTimeLength ?? st.StaffTimeLength;
        if (length is null or <= 0)
            throw StudioException.Unprocessable($"Session type {st.Id} has no session length configured at the studio.");

        // Every trainer's working windows, whether or not they already have a booking in them.
        var staff = await _api.GetAllAsync<MbStaffPage, MbStaff>("staff/staff", new(), p => p.StaffMembers, ct);
        var ids = staff.Where(s => s.Active && s.AppointmentInstructor).Select(s => s.Id).ToList();

        var starts = new SortedSet<DateTime>();
        var step = TimeSpan.FromMinutes(length.Value);
        for (var chunkStart = f; chunkStart <= t; chunkStart = chunkStart.AddDays(31))
        {
            var chunkEnd = chunkStart.AddDays(30) < t ? chunkStart.AddDays(30) : t;
            foreach (var batch in ids.Chunk(50))
            {
                var q = new List<KeyValuePair<string, string>> { new("StartDate", Day(chunkStart)), new("EndDate", Day(chunkEnd)) };
                q.AddRange(batch.Select(i => new KeyValuePair<string, string>("StaffIds", i.ToString(CultureInfo.InvariantCulture))));
                var members = await _api.GetAllAsync<MbStaffPage, MbStaff>("appointment/scheduleitems", q, p => p.StaffMembers, ct);
                foreach (var w in members.SelectMany(m => m.Availabilities ?? new()))
                {
                    if (w.Programs?.Any(p => p.Id == st.ProgramId) != true) continue;
                    for (var s = w.StartDateTime; s + step <= w.EndDateTime; s += step)
                    {
                        var d = DateOnly.FromDateTime(s);
                        if (d >= f && d <= t && s > now) starts.Add(s);
                    }
                }
            }
        }
        return starts.Select(s => new SlotDto(ToOffset(tz, s), ToOffset(tz, s + step))).ToList();
    }

    // ------------------------------------------------------------------ clients

    private SemaphoreSlim LockFor(string key) => _locks[(uint)StringComparer.OrdinalIgnoreCase.GetHashCode(key) % LOCK_STRIPES];

    private async Task<T> WithLockAsync<T>(string key, CancellationToken ct, Func<Task<T>> action)
    {
        var l = LockFor(key);
        await l.WaitAsync(ct);
        try { return await action(); }
        finally { l.Release(); }
    }

    private async Task<MbClient?> FindClientByEmailAsync(string email, CancellationToken ct)
    {
        var found = await _api.GetAllAsync<MbClientsPage, MbClient>("client/clients", new() { new("SearchText", email) }, p => p.Clients, ct);
        return found
            .Where(c => string.Equals(c.Email?.Trim(), email, StringComparison.OrdinalIgnoreCase) && c.Active != false)
            .OrderBy(c => long.TryParse(c.Id, out var n) ? n : long.MaxValue)
            .FirstOrDefault();
    }

    private async Task<MbClient?> FindClientAsync(string identity, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identity)) throw new StudioException(401, "The caller's identity is missing from the token.");
        return await FindClientByEmailAsync(identity.Trim(), ct);
    }

    private static List<string> MissingDetails(NewClientDetails d)
    {
        var m = new List<string>();
        if (d.HomeLocationId is null) m.Add("homeLocationId");
        if (string.IsNullOrWhiteSpace(d.AddressLine1)) m.Add("addressLine1");
        if (string.IsNullOrWhiteSpace(d.City)) m.Add("city");
        if (string.IsNullOrWhiteSpace(d.State)) m.Add("state");
        if (string.IsNullOrWhiteSpace(d.PostalCode)) m.Add("postalCode");
        if (d.BirthDate is null) m.Add("birthDate");
        if (string.IsNullOrWhiteSpace(d.MobilePhone)) m.Add("mobilePhone");
        if (string.IsNullOrWhiteSpace(d.ReferredBy)) m.Add("referredBy");
        return m;
    }

    private void RequireNewClientDetails(string identity, NewClientDetails d)
    {
        var missing = MissingDetails(d);
        if (missing.Count > 0)
            throw StudioException.BadRequest(
                "You are not yet a client of the studio. With your first booking please also provide: " + string.Join(", ", missing) + ".");
        if (!identity.Contains('@'))
            throw StudioException.BadRequest("Your account has no e-mail address, which the studio needs to create a client.");
        if (d.BirthDate!.Value > DateOnly.FromDateTime(DateTime.UtcNow) || d.BirthDate.Value.Year < 1900)
            throw StudioException.BadRequest("'birthDate' is not a valid date of birth.");
    }

    private async Task<MbClient> CreateClientAsync(string identity, NewClientDetails d, CancellationToken ct)
    {
        var locations = await _api.GetAllAsync<MbLocationsPage, MbLocation>("site/locations", new(), p => p.Locations, ct);
        if (locations.All(l => l.Id != d.HomeLocationId))
            throw StudioException.BadRequest($"Home location {d.HomeLocationId} does not exist; choose one of: " +
                string.Join(", ", locations.Select(l => $"{l.Id} ({l.Name})")) + ".");

        var referrals = (await _api.GetAsync<MbReferralTypesPage>("client/clientreferraltypes", null, ct)).ReferralTypes ?? new();
        var referral = referrals.FirstOrDefault(r => string.Equals(r, d.ReferredBy!.Trim(), StringComparison.OrdinalIgnoreCase));
        if (referral is null)
            throw StudioException.BadRequest("'referredBy' must be one of: " + string.Join(", ", referrals) + ".");

        var local = identity.Split('@')[0];
        var created = await _api.PostAsync<MbClientResponse>("client/addclient", new
        {
            FirstName = string.IsNullOrWhiteSpace(d.FirstName) ? local : d.FirstName!.Trim(),
            LastName = string.IsNullOrWhiteSpace(d.LastName) ? "eShopOnWeb" : d.LastName!.Trim(),
            Email = identity,
            AddressLine1 = d.AddressLine1!.Trim(),
            City = d.City!.Trim(),
            State = d.State!.Trim(),
            PostalCode = d.PostalCode!.Trim(),
            BirthDate = Day(d.BirthDate!.Value),
            MobilePhone = d.MobilePhone!.Trim(),
            ReferredBy = referral,
            HomeLocation = new { Id = d.HomeLocationId }
        }, ct);
        return created.Client ?? throw new StudioException(502, "The studio system did not return the new client.");
    }

    // ------------------------------------------------------------------ class lookups

    private async Task<MbClass> GetClassAsync(long classId, CancellationToken ct)
    {
        var tz = await SiteTimeZoneAsync(ct);
        var found = await _api.GetAllAsync<MbClassesPage, MbClass>("class/classes", new()
        {
            new("ClassIds", classId.ToString(CultureInfo.InvariantCulture)),
            new("StartDateTime", Iso(FarPast)),
            new("EndDateTime", Iso(Now(tz).AddYears(10)))
        }, p => p.Classes, ct);
        return found.FirstOrDefault(c => c.Id == classId) ?? throw StudioException.NotFound($"Class {classId} does not exist.");
    }

    private async Task<List<MbVisit>> VisitsAsync(string clientId, DateTime from, DateTime to, CancellationToken ct)
        => await _api.GetAllAsync<MbVisitsPage, MbVisit>("client/clientvisits", new()
        {
            new("ClientId", clientId), new("StartDate", Iso(from)), new("EndDate", Iso(to))
        }, p => p.Visits, ct);

    private async Task<ClassBookingDto> BookClassCoreAsync(string identity, long classId, NewClientDetails? details, bool admin, CancellationToken ct)
    {
        var tz = await SiteTimeZoneAsync(ct);
        var cls = await GetClassAsync(classId, ct);
        var client = await FindClientAsync(identity, ct);

        // A retried request must succeed, so an existing booking is answered before availability is re-checked.
        if (client is not null)
        {
            var existing = (await VisitsAsync(client.Id!, cls.StartDateTime.Date, cls.StartDateTime.Date.AddDays(1), ct))
                .FirstOrDefault(v => v.ClassId == classId && v.AppointmentId == 0 && !IsCancelledStatus(v.AppointmentStatus));
            if (existing is not null) return ClassBooking(tz, existing.Id, cls);
        }

        var now = Now(tz);
        if (cls.IsCanceled || !cls.Active) throw StudioException.Conflict("This class has been cancelled.");
        if (cls.StartDateTime <= now) throw StudioException.Conflict("This class has already started.");
        if (!admin && !Attendable(cls, now))
            throw StudioException.Conflict("This class cannot be booked online (it is full or not open to shoppers).");

        if (client is null)
        {
            if (admin) throw StudioException.NotFound($"'{identity}' is not a client of the studio yet; the shopper must make a first booking themselves.");
            RequireNewClientDetails(identity, details!);
            client = await CreateClientAsync(identity, details!, ct);
        }

        var resp = await _api.PostAsync<MbAddToClassResponse>("class/addclienttoclass",
            new { ClientId = client.Id, ClassId = classId, Test = false, SendEmail = false }, ct);
        return ClassBooking(tz, resp.Visit?.Id ?? 0, cls);
    }

    private static bool IsCancelledStatus(string? s)
        => s is not null && (s.Contains("Cancel", StringComparison.OrdinalIgnoreCase));

    private static ClassBookingDto ClassBooking(TimeZoneInfo tz, long bookingId, MbClass c)
        => new(bookingId, c.Id, c.ClassDescription?.Name, ToOffset(tz, c.StartDateTime), ToOffset(tz, c.EndDateTime));

    // ------------------------------------------------------------------ 3. book

    
    public Task<ClassBookingDto> BookClassAsync(string identity, ClassBookingRequest r, CancellationToken ct)
    {
        if (r.ClassId is null) throw StudioException.BadRequest("'classId' is required.");
        return WithLockAsync(identity, ct, () => BookClassCoreAsync(identity, r.ClassId.Value, r, false, ct));
    }

    public Task<AppointmentBookingDto> BookAppointmentAsync(string identity, AppointmentBookingRequest r, CancellationToken ct)
    {
        if (r.SessionTypeId is null) throw StudioException.BadRequest("'sessionTypeId' is required.");
        if (r.StartTime is null) throw StudioException.BadRequest("'startTime' is required (ISO-8601 with UTC offset).");
        return WithLockAsync(identity, ct, () => BookAppointmentCoreAsync(identity, r, ct));
    }

    private async Task<AppointmentBookingDto> BookAppointmentCoreAsync(string identity, AppointmentBookingRequest r, CancellationToken ct)
    {
        var tz = await SiteTimeZoneAsync(ct);
        var st = await GetSessionTypeAsync(r.SessionTypeId!.Value, ct);
        var length = st.DefaultTimeLength ?? st.StaffTimeLength;
        var start = ToLocal(tz, r.StartTime!.Value);
        var day = start.Date;
        var client = await FindClientAsync(identity, ct);

        if (client is not null)
        {
            var existing = (await AppointmentsAsync(client.Id!, day, day.AddDays(1), ct))
                .FirstOrDefault(a => a.SessionTypeId == st.Id && a.StartDateTime == start && !IsCancelledStatus(a.Status));
            if (existing is not null) return AppointmentBooking(tz, existing);
        }

        if (start <= Now(tz)) throw StudioException.Conflict("That time has already passed.");

        // A trainer must be free for the whole session at the requested time.
        var bookable = await _api.GetAllAsync<MbBookablePage, MbBookable>("appointment/bookableitems", new()
        {
            new("SessionTypeIds", st.Id.ToString(CultureInfo.InvariantCulture)),
            new("StartDate", Day(DateOnly.FromDateTime(day))),
            new("EndDate", Day(DateOnly.FromDateTime(day)))
        }, p => p.Availabilities, ct);
        var slot = bookable.FirstOrDefault(b =>
            b.Staff is not null && b.Location is not null
            && (r.StaffId is null || b.Staff.Id == r.StaffId)
            && (r.LocationId is null || b.Location.Id == r.LocationId)
            && b.StartDateTime <= start
            && (b.BookableEndDateTime is { } be && be.Year > 1
                ? start <= be
                : length is > 0 && start.AddMinutes(length.Value) <= b.EndDateTime));
        if (slot is null) throw StudioException.Conflict("No trainer is free to start this session at that time.");

        if (client is null)
        {
            RequireNewClientDetails(identity, r);
            client = await CreateClientAsync(identity, r, ct);
        }

        var resp = await _api.PostAsync<MbAppointmentResponse>("appointment/addappointment", new
        {
            ClientId = client.Id,
            SessionTypeId = st.Id,
            StaffId = slot.Staff!.Id,
            LocationId = slot.Location!.Id,
            StartDateTime = Iso(start),
            Test = false,
            SendEmail = false
        }, ct);
        var appt = resp.Appointment ?? throw new StudioException(502, "The studio system did not return the appointment.");
        return AppointmentBooking(tz, appt);
    }

    private async Task<List<MbAppointment>> AppointmentsAsync(string clientId, DateTime from, DateTime to, CancellationToken ct)
        => await _api.GetAllAsync<MbAppointmentsPage, MbAppointment>("appointment/staffappointments", new()
        {
            new("ClientIds", clientId), new("StartDate", Iso(from)), new("EndDate", Iso(to))
        }, p => p.Appointments, ct);

    private static AppointmentBookingDto AppointmentBooking(TimeZoneInfo tz, MbAppointment a)
        => new(a.Id, a.SessionTypeId, a.StaffId, a.LocationId, ToOffset(tz, a.StartDateTime), ToOffset(tz, a.EndDateTime));

    // ------------------------------------------------------------------ 4. cancel

    private bool CancelledBefore(string kind, string identity, long id) => _cache.TryGetValue($"studio:cancelled:{kind}:{identity.ToLowerInvariant()}:{id}", out _);

    private void RememberCancelled(string kind, string identity, long id)
        => _cache.Set($"studio:cancelled:{kind}:{identity.ToLowerInvariant()}:{id}", true, TimeSpan.FromDays(2));

    public Task<CancelledDto> CancelClassBookingAsync(string identity, long bookingId, CancellationToken ct)
        => WithLockAsync(identity, ct, async () =>
        {
            var tz = await SiteTimeZoneAsync(ct);
            var client = await FindClientAsync(identity, ct);
            var now = Now(tz);
            var mine = client is null ? null
                : (await VisitsAsync(client.Id!, now.AddYears(-2), now.AddYears(3), ct)).FirstOrDefault(v => v.Id == bookingId && v.ClassId != 0);
            if (mine is null)
            {
                // A retried cancellation (the booking is already gone) is still a success.
                if (CancelledBefore("class", identity, bookingId)) return new CancelledDto(bookingId, "cancelled");
                throw StudioException.NotFound($"You have no class booking {bookingId}.");
            }
            await _api.PostAsync<System.Text.Json.JsonElement>("class/removeclientfromclass",
                new { ClientId = client!.Id, ClassId = mine.ClassId, Test = false, SendEmail = false }, ct);
            RememberCancelled("class", identity, bookingId);
            return new CancelledDto(bookingId, "cancelled");
        });

    public Task<CancelledDto> CancelAppointmentBookingAsync(string identity, long bookingId, CancellationToken ct)
        => WithLockAsync(identity, ct, async () =>
        {
            var tz = await SiteTimeZoneAsync(ct);
            var client = await FindClientAsync(identity, ct);
            var now = Now(tz);
            var mine = client is null ? null
                : (await AppointmentsAsync(client.Id!, now.AddYears(-2), now.AddYears(3), ct))
                    .FirstOrDefault(a => a.Id == bookingId && !IsCancelledStatus(a.Status));
            if (mine is null)
            {
                if (CancelledBefore("appointment", identity, bookingId)) return new CancelledDto(bookingId, "cancelled");
                throw StudioException.NotFound($"You have no appointment booking {bookingId}.");
            }
            await _api.PostAsync<System.Text.Json.JsonElement>("appointment/updateappointment",
                new { AppointmentId = bookingId, Execute = "Cancel", Test = false, SendEmail = false }, ct);
            RememberCancelled("appointment", identity, bookingId);
            return new CancelledDto(bookingId, "cancelled");
        });

    // ------------------------------------------------------------------ 5. trainers

    public async Task<List<TrainerDto>> ListTrainersAsync(CancellationToken ct)
    {
        var staff = await _api.GetAllAsync<MbStaffPage, MbStaff>("staff/staff", new(), p => p.StaffMembers, ct);
        return staff
            .Where(s => s.AppointmentInstructor || s.ClassTeacher)
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ThenBy(s => s.Id)
            .Select(s => new TrainerDto(s.Id, s.Active, s.FirstName, s.LastName, s.DisplayName ?? s.Name,
                s.Email, s.HomePhone, s.MobilePhone, s.WorkPhone, s.Address, s.City, s.State, s.PostalCode, s.Country))
            .ToList();
    }

    // ------------------------------------------------------------------ 6. book for a shopper

    public async Task<ClassBookingDto> AdminBookClassAsync(AdminClassBookingRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Shopper)) throw StudioException.BadRequest("'shopper' is required.");
        if (r.ClassId is null) throw StudioException.BadRequest("'classId' is required.");
        var shopper = r.Shopper.Trim();
        if (!shopper.Contains('@'))
            throw StudioException.BadRequest("'shopper' must be the shopper's eShop identity (their e-mail address).");
        return await WithLockAsync(shopper, ct, () => BookClassCoreAsync(shopper, r.ClassId.Value, null, true, ct));
    }
}
