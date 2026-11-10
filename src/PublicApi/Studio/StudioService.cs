using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>Studio features on top of Mindbody. Mutating calls are safe to repeat.</summary>
public class StudioService
{
    private const int MaxRangeDays = 366;
    private const int SlotChunkDays = 30;
    private static readonly TimeSpan SiteInfoTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan CancelMemoryTtl = TimeSpan.FromDays(30);

    // Serialises work per shopper so a repeated request cannot create a second client or booking.
    private static readonly SemaphoreSlim[] Locks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly MindbodyClient _mb;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IMemoryCache _cache;

    public StudioService(MindbodyClient mb, UserManager<ApplicationUser> users, IMemoryCache cache)
    {
        _mb = mb;
        _users = users;
        _cache = cache;
    }

    // ---------------------------------------------------------------- site info & time

    private Task<T> Cached<T>(string key, Func<Task<T>> factory)
        => _cache.GetOrCreateAsync(key, e =>
        {
            e.AbsoluteExpirationRelativeToNow = SiteInfoTtl;
            return factory();
        })!;

    private Task<TimeZoneInfo> GetSiteTimeZoneAsync(CancellationToken ct) => Cached("studio:tz", async () =>
    {
        var root = await _mb.GetAsync("site/sites", null, ct);
        var id = root.Items("Sites").Select(s => s.Str("TimeZone")).FirstOrDefault(s => !string.IsNullOrEmpty(s));
        if (id == null) throw new StudioException(502, "The studio system did not report its time zone.");
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new StudioException(502, $"The studio's time zone '{id}' is not known to this server.", ex);
        }
    });

    private Task<List<JsonElement>> GetLocationsAsync(CancellationToken ct)
        => Cached("studio:locations", () => _mb.GetAllAsync("site/locations", "Locations", null, ct));

    private Task<List<JsonElement>> GetSessionTypesAsync(CancellationToken ct)
        => Cached("studio:sessiontypes", () => _mb.GetAllAsync("site/sessiontypes", "SessionTypes", null, ct));

    private Task<HashSet<string>> GetReferralTypesAsync(CancellationToken ct) => Cached("studio:referrals", async () =>
    {
        var root = await _mb.GetAsync("client/clientreferraltypes", null, ct);
        return root.Items("ReferralTypes").Select(r => r.GetString() ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
    });

    /// <summary>Converts a site-local wall-clock time to an instant with the offset in force at that moment.</summary>
    private static DateTimeOffset ToOffset(DateTime local, TimeZoneInfo tz)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(local)) local = local.AddHours(1);
        var offset = tz.IsAmbiguousTime(local) ? tz.GetAmbiguousTimeOffsets(local).Max() : tz.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static DateTime ToLocal(DateTimeOffset instant, TimeZoneInfo tz)
        => DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(instant, tz).DateTime, DateTimeKind.Unspecified);

    private static string Wire(DateTime local) => local.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    private static DateOnly Today(TimeZoneInfo tz) => DateOnly.FromDateTime(ToLocal(DateTimeOffset.UtcNow, tz));

    // ---------------------------------------------------------------- classes

    public async Task<List<StudioClassDto>> ListClassesAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var tz = await GetSiteTimeZoneAsync(ct);
        var today = Today(tz);
        var start = from ?? today;
        var end = to ?? (from.HasValue ? start.AddDays(7) : today.AddDays(7));
        ValidateRange(start, end);

        var rows = await FetchClassesAsync(new[]
        {
            new KeyValuePair<string, string>("StartDateTime", Wire(start.ToDateTime(TimeOnly.MinValue))),
            new KeyValuePair<string, string>("EndDateTime", Wire(end.AddDays(1).ToDateTime(TimeOnly.MinValue)))
        }, ct);

        var now = DateTimeOffset.UtcNow;
        var result = new List<(DateTimeOffset Start, StudioClassDto Dto)>();
        foreach (var c in rows)
        {
            if (!IsBookableByShopper(c)) continue;
            var dto = ToClassDto(c, tz);
            if (dto == null || dto.EndTime <= now) continue;
            var startLocal = DateOnly.FromDateTime(ToLocal(dto.StartTime, tz));
            if (startLocal < start || startLocal > end) continue;
            result.Add((dto.StartTime, dto));
        }
        return result.OrderBy(r => r.Start).ThenBy(r => r.Dto.Id).Select(r => r.Dto).ToList();
    }

    private Task<List<JsonElement>> FetchClassesAsync(IEnumerable<KeyValuePair<string, string>> query, CancellationToken ct)
        => _mb.GetAllAsync("class/classes", "Classes", query, ct);

    private static bool IsBookableByShopper(JsonElement c)
    {
        if (c.Bool("IsCanceled") || !c.Bool("Active", true) || !c.Bool("IsAvailable", true)) return false;
        // Full for online booking.
        if (c.Int("WebCapacity") is { } webCapacity && (c.Int("WebBooked") ?? 0) >= webCapacity) return false;
        // Enrolment courses are joined through a different Mindbody flow than a single-class booking.
        var scheduleType = c.Obj("ClassDescription").Obj("Program").Str("ScheduleType");
        return !string.Equals(scheduleType, "Enrollment", StringComparison.OrdinalIgnoreCase);
    }

    private static StudioClassDto? ToClassDto(JsonElement c, TimeZoneInfo tz)
    {
        var id = c.Long("Id");
        var s = c.LocalDateTime("StartDateTime");
        var e = c.LocalDateTime("EndDateTime");
        if (id == null || s == null || e == null) return null;
        var location = c.Obj("Location");
        var staff = c.Obj("Staff");
        int? spots = c.Int("WebCapacity") is { } cap ? Math.Max(0, cap - (c.Int("WebBooked") ?? 0)) : null;
        return new StudioClassDto(
            id.Value,
            c.Obj("ClassDescription").Str("Name"),
            c.Obj("ClassDescription").Str("Description"),
            ToOffset(s.Value, tz),
            ToOffset(e.Value, tz),
            location.Int("Id"),
            location.Str("Name"),
            staff.Str("DisplayName") ?? staff.Str("Name"),
            spots);
    }

    private static void ValidateRange(DateOnly from, DateOnly to)
    {
        if (to < from) throw StudioException.BadRequest("'to' must not be before 'from'.");
        if (to.DayNumber - from.DayNumber >= MaxRangeDays)
            throw StudioException.BadRequest($"The range may cover at most {MaxRangeDays} days.");
    }

    private async Task<JsonElement?> FindClassAsync(long classId, CancellationToken ct)
    {
        // Without a date range Mindbody only looks at today, so search the whole bookable horizon.
        var tz = await GetSiteTimeZoneAsync(ct);
        var from = Today(tz).AddDays(-1);
        var rows = await FetchClassesAsync(new[]
        {
            new KeyValuePair<string, string>("ClassIds", classId.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("StartDateTime", Wire(from.ToDateTime(TimeOnly.MinValue))),
            new KeyValuePair<string, string>("EndDateTime", Wire(from.AddDays(MaxRangeDays).ToDateTime(TimeOnly.MinValue)))
        }, ct);
        foreach (var r in rows)
        {
            if (r.Long("Id") == classId) return r;
        }
        return null;
    }

    // ---------------------------------------------------------------- session times

    public async Task<List<SlotDto>> ListSlotsAsync(int? sessionTypeId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (sessionTypeId == null) throw StudioException.BadRequest("'sessionTypeId' is required.");
        if (from == null || to == null) throw StudioException.BadRequest("'from' and 'to' are required.");
        ValidateRange(from.Value, to.Value);

        var sessionType = await FindSessionTypeAsync(sessionTypeId.Value, ct);
        var tz = await GetSiteTimeZoneAsync(ct);
        var length = SessionLength(sessionType);
        var now = DateTimeOffset.UtcNow;
        var starts = new SortedSet<DateTime>();

        for (var day = from.Value; day <= to.Value; day = day.AddDays(SlotChunkDays))
        {
            var chunkEnd = day.AddDays(SlotChunkDays - 1) < to.Value ? day.AddDays(SlotChunkDays - 1) : to.Value;
            var windows = await FetchWindowsAsync(sessionTypeId.Value, day, chunkEnd, null, null, ct);
            foreach (var w in windows)
            {
                foreach (var s in StartsWithin(w, length ?? w.Length))
                {
                    var d = DateOnly.FromDateTime(s);
                    if (d < from.Value || d > to.Value) continue;
                    starts.Add(s);
                }
            }
        }

        var len = length ?? TimeSpan.FromMinutes(60);
        var result = new List<SlotDto>();
        foreach (var s in starts)
        {
            var startOffset = ToOffset(s, tz);
            if (startOffset <= now) continue;
            result.Add(new SlotDto(startOffset, ToOffset(s + len, tz)));
        }
        return result;
    }

    private async Task<JsonElement> FindSessionTypeAsync(int id, CancellationToken ct)
    {
        var all = await GetSessionTypesAsync(ct);
        foreach (var s in all)
        {
            if (s.Int("Id") == id) return s;
        }
        throw StudioException.NotFound($"Session type {id} does not exist.");
    }

    private static TimeSpan? SessionLength(JsonElement sessionType)
    {
        var minutes = sessionType.Int("DefaultTimeLength") ?? sessionType.Int("StaffTimeLength");
        return minutes is > 0 ? TimeSpan.FromMinutes(minutes.Value) : null;
    }

    private sealed record Window(DateTime Start, DateTime End, int? StaffId, int? LocationId, TimeSpan? SessionLength)
    {
        public TimeSpan Length => TimeSpan.FromMinutes(60);
    }

    private async Task<List<Window>> FetchWindowsAsync(int sessionTypeId, DateOnly from, DateOnly to, int? staffId, int? locationId, CancellationToken ct)
    {
        var q = new List<KeyValuePair<string, string>>
        {
            new("SessionTypeIds", sessionTypeId.ToString(CultureInfo.InvariantCulture)),
            new("StartDate", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("EndDate", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        };
        if (staffId != null) q.Add(new("StaffIds", staffId.Value.ToString(CultureInfo.InvariantCulture)));
        if (locationId != null) q.Add(new("LocationIds", locationId.Value.ToString(CultureInfo.InvariantCulture)));

        var items = await _mb.GetAllAsync("appointment/bookableitems", "Availabilities", q, ct);
        var windows = new List<Window>();
        foreach (var a in items)
        {
            var s = a.LocalDateTime("StartDateTime");
            var e = a.LocalDateTime("EndDateTime");
            if (s == null || e == null) continue;
            var minutes = a.Obj("SessionType").Int("DefaultTimeLength");
            windows.Add(new Window(s.Value, e.Value, a.Obj("Staff").Int("Id"), a.Obj("Location").Int("Id"),
                minutes is > 0 ? TimeSpan.FromMinutes(minutes.Value) : null));
        }
        return windows;
    }

    private static IEnumerable<DateTime> StartsWithin(Window w, TimeSpan length)
    {
        var len = w.SessionLength ?? length;
        for (var s = w.Start; s + len <= w.End; s += len) yield return s;
    }

    // ---------------------------------------------------------------- identity & clients

    private async Task<string> ResolveEmailAsync(ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name)) throw new StudioException(401, "The token does not identify a user.");
        var user = await _users.FindByNameAsync(name);
        return (user?.Email ?? name).Trim();
    }

    private static SemaphoreSlim LockFor(string key)
        => Locks[(uint)StringComparer.OrdinalIgnoreCase.GetHashCode(key) % (uint)Locks.Length];

    private async Task<T> WithLockAsync<T>(string key, Func<Task<T>> action, CancellationToken ct)
    {
        var gate = LockFor(key);
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

    private async Task<string?> FindClientIdAsync(string email, CancellationToken ct)
    {
        var rows = await _mb.GetAllAsync("client/clients", "Clients", new[] { new KeyValuePair<string, string>("SearchText", email) }, ct);
        string? fallback = null;
        foreach (var c in rows)
        {
            if (!string.Equals(c.Str("Email")?.Trim(), email, StringComparison.OrdinalIgnoreCase)) continue;
            if (c.Bool("Active", true)) return c.Str("Id");
            fallback ??= c.Str("Id");
        }
        return fallback;
    }

    private async Task<string> EnsureClientAsync(string email, ClientRegistration reg, CancellationToken ct)
    {
        var existing = await FindClientIdAsync(email, ct);
        if (existing != null) return existing;

        var missing = new List<string>();
        if (reg.HomeLocationId == null) missing.Add("homeLocationId");
        if (string.IsNullOrWhiteSpace(reg.StreetAddress)) missing.Add("streetAddress");
        if (string.IsNullOrWhiteSpace(reg.City)) missing.Add("city");
        if (string.IsNullOrWhiteSpace(reg.State)) missing.Add("state");
        if (string.IsNullOrWhiteSpace(reg.PostalCode)) missing.Add("postalCode");
        if (reg.DateOfBirth == null) missing.Add("dateOfBirth");
        if (string.IsNullOrWhiteSpace(reg.MobilePhone)) missing.Add("mobilePhone");
        if (string.IsNullOrWhiteSpace(reg.ReferredBy)) missing.Add("referredBy");
        if (missing.Count > 0)
        {
            throw StudioException.BadRequest(
                "A first booking also needs the new-client details. Missing: " + string.Join(", ", missing) + ".");
        }

        if (reg.DateOfBirth!.Value > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            throw StudioException.BadRequest("'dateOfBirth' cannot be in the future.");

        var location = (await GetLocationsAsync(ct)).FirstOrDefault(l => l.Int("Id") == reg.HomeLocationId);
        if (location.ValueKind != JsonValueKind.Object)
            throw StudioException.BadRequest($"Location {reg.HomeLocationId} does not exist.");

        var referrals = await GetReferralTypesAsync(ct);
        var referredBy = reg.ReferredBy!.Trim();
        if (referrals.Count > 0)
        {
            if (!referrals.Contains(referredBy))
                throw StudioException.BadRequest("'referredBy' must be one of: " + string.Join(", ", referrals.OrderBy(r => r)) + ".");
            referredBy = referrals.First(r => string.Equals(r, referredBy, StringComparison.OrdinalIgnoreCase));
        }

        var local = email.Split('@')[0];
        var body = new
        {
            FirstName = string.IsNullOrWhiteSpace(reg.FirstName) ? local : reg.FirstName!.Trim(),
            LastName = string.IsNullOrWhiteSpace(reg.LastName) ? "Shopper" : reg.LastName!.Trim(),
            Email = email,
            AddressLine1 = reg.StreetAddress!.Trim(),
            City = reg.City!.Trim(),
            State = reg.State!.Trim(),
            PostalCode = reg.PostalCode!.Trim(),
            BirthDate = reg.DateOfBirth.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            MobilePhone = reg.MobilePhone!.Trim(),
            ReferredBy = referredBy,
            HomeLocation = new { Id = reg.HomeLocationId }
        };

        try
        {
            var root = await _mb.PostAsync("client/addclient", body, ct);
            var id = root.Obj("Client").Str("Id");
            if (id == null) throw new StudioException(502, "The studio system did not return the new client.");
            return id;
        }
        catch (StudioException ex) when (ex.StatusCode == 400 && ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
        {
            // The client already exists (e.g. an earlier request whose answer was lost); use it.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var again = await FindClientIdAsync(email, ct);
                if (again != null) return again;
                await Task.Delay(500, ct);
            }
            throw;
        }
    }

    // ---------------------------------------------------------------- visits

    private sealed record Visit(long BookingId, long ClassId, long AppointmentId, string Status, DateTime Start, DateTime End,
        int? SessionTypeId, int? StaffId, int? LocationId, string? Name);

    private async Task<List<Visit>> GetVisitsAsync(string clientId, DateTime from, DateTime to, CancellationToken ct)
    {
        var rows = await _mb.GetAllAsync("client/clientvisits", "Visits", new[]
        {
            new KeyValuePair<string, string>("ClientId", clientId),
            new KeyValuePair<string, string>("StartDate", Wire(from)),
            new KeyValuePair<string, string>("EndDate", Wire(to))
        }, ct);

        var list = new List<Visit>();
        foreach (var v in rows)
        {
            var status = v.Str("AppointmentStatus") ?? "";
            if (status.Contains("cancel", StringComparison.OrdinalIgnoreCase) || v.Bool("LateCancelled")) continue;
            var s = v.LocalDateTime("StartDateTime");
            var e = v.LocalDateTime("EndDateTime");
            if (s == null || e == null) continue;
            var classId = v.Long("ClassId") ?? 0;
            var appointmentId = v.Long("AppointmentId") ?? 0;
            list.Add(new Visit(classId > 0 ? v.Long("Id") ?? 0 : appointmentId, classId, appointmentId, status, s.Value, e.Value,
                v.Int("VisitType"), v.Int("StaffId"), v.Int("LocationId"), v.Str("Name")));
        }
        return list;
    }

    // ---------------------------------------------------------------- book a class

    public Task<(ClassBookingDto Booking, bool Created)> BookClassAsync(ClaimsPrincipal principal, BookClassRequest request, CancellationToken ct)
    {
        if (request.ClassId == null) throw StudioException.BadRequest("'classId' is required.");
        return BookClassCoreAsync(principal, request.ClassId.Value, request, requireShopperRules: true, ct);
    }

    public async Task<(ClassBookingDto Booking, bool Created)> AdminBookClassAsync(AdminBookClassRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Shopper)) throw StudioException.BadRequest("'shopper' is required.");
        if (request.ClassId == null) throw StudioException.BadRequest("'classId' is required.");

        var key = request.Shopper.Trim();
        var user = await _users.FindByNameAsync(key) ?? await _users.FindByEmailAsync(key);
        if (user == null) throw StudioException.NotFound($"Shopper '{key}' does not exist.");
        var email = (user.Email ?? user.UserName ?? key).Trim();

        return await BookClassForEmailAsync(email, request.ClassId.Value, new ClientRegistration(), requireShopperRules: false, ct);
    }

    private async Task<(ClassBookingDto, bool)> BookClassCoreAsync(ClaimsPrincipal principal, long classId, ClientRegistration reg, bool requireShopperRules, CancellationToken ct)
    {
        var email = await ResolveEmailAsync(principal);
        return await BookClassForEmailAsync(email, classId, reg, requireShopperRules, ct);
    }

    private Task<(ClassBookingDto, bool)> BookClassForEmailAsync(string email, long classId, ClientRegistration reg, bool requireShopperRules, CancellationToken ct)
        => WithLockAsync(email, async () =>
        {
            var tz = await GetSiteTimeZoneAsync(ct);
            var cls = await FindClassAsync(classId, ct) ?? throw StudioException.NotFound($"Class {classId} does not exist.");
            var dto = ToClassDto(cls, tz) ?? throw StudioException.NotFound($"Class {classId} does not exist.");
            if (cls.Bool("IsCanceled")) throw StudioException.Conflict("That class has been cancelled.");
            if (dto.EndTime <= DateTimeOffset.UtcNow) throw StudioException.Conflict("That class has already finished.");
            if (requireShopperRules && !IsBookableByShopper(cls))
                throw StudioException.Conflict("That class is not open for booking.");

            string clientId;
            if (requireShopperRules)
            {
                clientId = await EnsureClientAsync(email, reg, ct);
            }
            else
            {
                clientId = await FindClientIdAsync(email, ct)
                    ?? throw new StudioException(422, $"'{email}' is not a studio client yet; they must make a first booking themselves.");
            }

            var startLocal = cls.LocalDateTime("StartDateTime")!.Value;
            var visits = await GetVisitsAsync(clientId, startLocal.Date, startLocal.Date.AddDays(1), ct);
            var existing = visits.FirstOrDefault(v => v.ClassId == classId);
            if (existing != null)
            {
                return (new ClassBookingDto(existing.BookingId, classId, dto.Name, dto.StartTime, dto.EndTime), false);
            }

            var root = await _mb.PostAsync("class/addclienttoclass", new
            {
                ClientId = clientId,
                ClassId = classId,
                RequirePayment = false,
                SendEmail = false
            }, ct);
            var visit = root.Obj("Visit");
            var bookingId = visit.Long("Id") ?? throw new StudioException(502, "The studio system did not return the booking.");
            return (new ClassBookingDto(bookingId, classId, dto.Name, dto.StartTime, dto.EndTime), true);
        }, ct);

    // ---------------------------------------------------------------- book an appointment

    public async Task<(AppointmentBookingDto Booking, bool Created)> BookAppointmentAsync(ClaimsPrincipal principal, BookAppointmentRequest request, CancellationToken ct)
    {
        if (request.SessionTypeId == null) throw StudioException.BadRequest("'sessionTypeId' is required.");
        if (request.StartTime == null) throw StudioException.BadRequest("'startTime' is required (ISO-8601 with UTC offset).");
        if (request.StartTime.Value <= DateTimeOffset.UtcNow) throw StudioException.BadRequest("'startTime' must be in the future.");

        var email = await ResolveEmailAsync(principal);
        var sessionTypeId = request.SessionTypeId.Value;

        return await WithLockAsync(email, async () =>
        {
            var tz = await GetSiteTimeZoneAsync(ct);
            var sessionType = await FindSessionTypeAsync(sessionTypeId, ct);
            var startLocal = ToLocal(request.StartTime.Value, tz);
            var day = DateOnly.FromDateTime(startLocal);

            var clientId = await EnsureClientAsync(email, request, ct);

            var visits = await GetVisitsAsync(clientId, startLocal.Date, startLocal.Date.AddDays(1), ct);
            var existing = visits.FirstOrDefault(v => v.AppointmentId > 0 && v.SessionTypeId == sessionTypeId && v.Start == startLocal);
            if (existing != null)
            {
                return (ToAppointmentDto(existing, tz), false);
            }

            var windows = await FetchWindowsAsync(sessionTypeId, day, day, request.StaffId, request.LocationId, ct);
            var length = SessionLength(sessionType);
            var candidates = windows
                .Where(w => w.Start <= startLocal && startLocal + (w.SessionLength ?? length ?? TimeSpan.FromMinutes(60)) <= w.End)
                .ToList();
            if (candidates.Count == 0)
                throw StudioException.Conflict("No trainer is free to start that session at the requested time.");

            StudioException? last = null;
            foreach (var c in candidates)
            {
                try
                {
                    var body = new Dictionary<string, object?>
                    {
                        ["ClientId"] = clientId,
                        ["SessionTypeId"] = sessionTypeId,
                        ["StartDateTime"] = Wire(startLocal),
                        ["StaffId"] = c.StaffId,
                        ["LocationId"] = c.LocationId,
                        ["SendEmail"] = false
                    };
                    var root = await _mb.PostAsync("appointment/addappointment", body, ct);
                    var a = root.Obj("Appointment");
                    var id = a.Long("Id") ?? throw new StudioException(502, "The studio system did not return the booking.");
                    var s = a.LocalDateTime("StartDateTime") ?? startLocal;
                    var e = a.LocalDateTime("EndDateTime") ?? s + (length ?? TimeSpan.FromMinutes(60));
                    return (new AppointmentBookingDto(id, sessionTypeId, sessionType.Str("Name"), ToOffset(s, tz), ToOffset(e, tz),
                        a.Int("StaffId") ?? c.StaffId, a.Int("LocationId") ?? c.LocationId), true);
                }
                catch (StudioException ex) when (ex.StatusCode == 409)
                {
                    last = ex;
                }
            }
            throw last ?? StudioException.Conflict("That session time cannot be booked.");
        }, ct);
    }

    private static AppointmentBookingDto ToAppointmentDto(Visit v, TimeZoneInfo tz)
        => new(v.AppointmentId, v.SessionTypeId ?? 0, v.Name, ToOffset(v.Start, tz), ToOffset(v.End, tz), v.StaffId, v.LocationId);

    // ---------------------------------------------------------------- cancel

    public Task CancelClassBookingAsync(ClaimsPrincipal principal, long bookingId, CancellationToken ct)
        => CancelAsync(principal, bookingId, isClass: true, ct);

    public Task CancelAppointmentBookingAsync(ClaimsPrincipal principal, long bookingId, CancellationToken ct)
        => CancelAsync(principal, bookingId, isClass: false, ct);

    private async Task CancelAsync(ClaimsPrincipal principal, long bookingId, bool isClass, CancellationToken ct)
    {
        var email = await ResolveEmailAsync(principal);
        await WithLockAsync(email, async () =>
        {
            var kind = isClass ? "class" : "appointment";
            var memoryKey = $"studio:cancelled:{kind}:{email.ToLowerInvariant()}:{bookingId}";

            var clientId = await FindClientIdAsync(email, ct);
            Visit? booking = null;
            if (clientId != null)
            {
                var tz = await GetSiteTimeZoneAsync(ct);
                var from = Today(tz).AddDays(-1).ToDateTime(TimeOnly.MinValue);
                var visits = await GetVisitsAsync(clientId, from, from.AddDays(MaxRangeDays + 1), ct);
                booking = visits.FirstOrDefault(v => v.BookingId == bookingId && (isClass ? v.ClassId > 0 : v.AppointmentId > 0));
            }

            if (booking == null)
            {
                // Repeating a cancellation that already went through is a success.
                if (_cache.TryGetValue(memoryKey, out _)) return true;
                throw StudioException.NotFound($"You have no {kind} booking {bookingId}.");
            }

            if (isClass)
            {
                await _mb.PostAsync("class/removeclientfromclass", new
                {
                    ClientId = clientId,
                    ClassId = booking.ClassId,
                    SendEmail = false,
                    LateCancel = false
                }, ct);
            }
            else
            {
                await _mb.PostAsync("appointment/updateappointment", new
                {
                    AppointmentId = booking.AppointmentId,
                    Execute = "Cancel",
                    SendEmail = false
                }, ct);
            }

            _cache.Set(memoryKey, true, CancelMemoryTtl);
            return true;
        }, ct);
    }

    // ---------------------------------------------------------------- trainers

    public async Task<List<TrainerDto>> ListTrainersAsync(CancellationToken ct)
    {
        var staff = await _mb.GetAllAsync("staff/staff", "StaffMembers", null, ct);
        return staff
            .Where(s => s.Bool("AppointmentInstructor") || s.Bool("ClassTeacher"))
            .Select(s => new TrainerDto(
                s.Long("Id") ?? 0,
                s.Str("DisplayName") ?? s.Str("Name"),
                s.Str("FirstName"),
                s.Str("LastName"),
                s.Bool("Active"),
                s.Bool("ClassTeacher"),
                s.Bool("AppointmentInstructor"),
                s.Str("Email"),
                s.Str("MobilePhone"),
                s.Str("HomePhone"),
                s.Str("WorkPhone"),
                s.Str("Address"),
                s.Str("City"),
                s.Str("State"),
                s.Str("PostalCode"),
                s.Str("Country")))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Id)
            .ToList();
    }
}
