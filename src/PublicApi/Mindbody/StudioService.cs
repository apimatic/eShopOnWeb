using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.PublicApi.Mindbody;

public record ClassDto(int Id, string? Name, string? Description, int? LocationId, string? LocationName, string? TrainerName,
    DateTimeOffset StartTime, DateTimeOffset EndTime, int? SpotsLeft);

public record SlotDto(DateTimeOffset StartTime, DateTimeOffset EndTime);

public record TrainerDto(long Id, bool Active, string? FirstName, string? LastName, string? DisplayName, string? Email,
    string? MobilePhone, string? HomePhone, string? WorkPhone, string? Address, string? City, string? State,
    string? PostalCode, string? Country, string? Bio, string? ImageUrl, bool TeachesClasses, bool TakesAppointments);

public record BookingDto(long BookingId, string Kind, int? ClassId, int? SessionTypeId, int? LocationId,
    DateTimeOffset StartTime, DateTimeOffset EndTime);

/// <summary>Details the studio asks of every new client. Required with a shopper's first booking.</summary>
public class ClientDetails
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

/// <summary>
/// Studio use cases on top of the Mindbody HTTP client.
/// </summary>
public class StudioService
{
    private const int PageSize = 200;
    private const int MaxRangeDays = 366;
    private const int SlotGridMinutes = 30;
    private static readonly Regex HasOffset = new(@"(Z|[+-]\d{2}(:?\d{2})?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Serialises work per client so a retried request cannot create a second client or booking.
    private static readonly SemaphoreSlim[] ClientLocks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly MindbodyClient _mb;
    private readonly IMemoryCache _cache;
    private readonly UserManager<ApplicationUser> _userManager;

    public StudioService(MindbodyClient mb, IMemoryCache cache, UserManager<ApplicationUser> userManager)
    {
        _mb = mb;
        _cache = cache;
        _userManager = userManager;
    }

    // ---------- reference data & time ----------

    private async Task<T> CachedAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        if (_cache.TryGetValue(key, out T? hit) && hit != null) return hit;
        var value = await factory();
        _cache.Set(key, value, ttl);
        return value;
    }

    private Task<TimeZoneInfo> GetSiteTimeZoneAsync(CancellationToken ct) =>
        CachedAsync("studio:tz", TimeSpan.FromHours(1), async () =>
        {
            var sites = await _mb.GetAsync<MbSitesResponse>("/site/sites", null, false, ct);
            var id = sites.Sites?.FirstOrDefault()?.TimeZone;
            if (string.IsNullOrEmpty(id)) throw new StudioException(502, "The studio's time zone is unknown.");
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException ex) { throw new StudioException(502, "The studio's time zone is not supported.", ex); }
        });

    private static DateTimeOffset ToOffset(TimeZoneInfo tz, DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }

    private static DateTime NowLocal(TimeZoneInfo tz) => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime;

    private static string D(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string DT(DateTime d) => d.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    private async Task<List<T>> PageAllAsync<T, TResp>(string path, Func<int, List<KeyValuePair<string, string?>>> query, bool asStaff,
        Func<TResp, List<T>?> items, CancellationToken ct) where TResp : MbPaged
    {
        var all = new List<T>();
        var offset = 0;
        while (true)
        {
            var q = query(offset);
            q.Add(new("limit", PageSize.ToString(CultureInfo.InvariantCulture)));
            q.Add(new("offset", offset.ToString(CultureInfo.InvariantCulture)));
            var page = await _mb.GetAsync<TResp>(path, q, asStaff, ct);
            var list = items(page) ?? new List<T>();
            all.AddRange(list);
            var total = page.PaginationResponse?.TotalResults ?? all.Count;
            offset += list.Count;
            if (list.Count == 0 || offset >= total) break;
        }
        return all;
    }

    private Task<List<MbLocation>> GetLocationsAsync(CancellationToken ct) =>
        CachedAsync("studio:locations", TimeSpan.FromMinutes(10), () =>
            PageAllAsync<MbLocation, MbLocationsResponse>("/site/locations", _ => new(), false, r => r.Locations, ct));

    private Task<List<MbSessionType>> GetSessionTypesAsync(CancellationToken ct) =>
        CachedAsync("studio:sessiontypes", TimeSpan.FromMinutes(10), () =>
            PageAllAsync<MbSessionType, MbSessionTypesResponse>("/site/sessiontypes", _ => new(), false, r => r.SessionTypes, ct));

    private Task<List<string>> GetReferralTypesAsync(CancellationToken ct) =>
        CachedAsync("studio:referrals", TimeSpan.FromMinutes(10), async () =>
            (await _mb.GetAsync<MbReferralTypesResponse>("/client/clientreferraltypes", null, true, ct)).ReferralTypes ?? new List<string>());

    // ---------- 1. classes ----------

    public async Task<List<ClassDto>> ListClassesAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var tz = await GetSiteTimeZoneAsync(ct);
        var now = NowLocal(tz);
        var start = from ?? DateOnly.FromDateTime(now);
        var end = to ?? start.AddDays(7);
        ValidateRange(start, end);

        var classes = await PageAllAsync<MbClass, MbClassesResponse>("/class/classes", _ => new()
        {
            new("startDateTime", DT(start.ToDateTime(TimeOnly.MinValue))),
            new("endDateTime", DT(end.ToDateTime(new TimeOnly(23, 59, 59)))),
        }, false, r => r.Classes, ct);

        return classes
            .Where(c => IsShopperClass(c) && c.EndDateTime > now)
            .OrderBy(c => c.StartDateTime).ThenBy(c => c.Id)
            .Select(c => ToDto(tz, c))
            .ToList();
    }

    private static bool IsShopperClass(MbClass c) =>
        !c.IsCanceled && c.Active && c.IsAvailable && !IsEnrollment(c);

    private static bool IsEnrollment(MbClass c) =>
        string.Equals(c.ClassDescription?.Program?.ScheduleType, "Enrollment", StringComparison.OrdinalIgnoreCase);

    private static ClassDto ToDto(TimeZoneInfo tz, MbClass c) => new(
        c.Id, c.ClassDescription?.Name?.Trim(), c.ClassDescription?.Description, c.Location?.Id, c.Location?.Name,
        c.Staff?.DisplayName ?? c.Staff?.Name,
        ToOffset(tz, c.StartDateTime), ToOffset(tz, c.EndDateTime),
        c.MaxCapacity is { } max ? Math.Max(0, max - (c.TotalBooked ?? 0)) : null);

    private static void ValidateRange(DateOnly from, DateOnly to)
    {
        if (to < from) throw StudioException.BadRequest("'to' must not be before 'from'.");
        if (to.DayNumber - from.DayNumber >= MaxRangeDays)
            throw StudioException.BadRequest($"The date range may not exceed {MaxRangeDays} days.");
    }

    // ---------- 2. session times ----------

    private async Task<MbSessionType> RequireAppointmentTypeAsync(int sessionTypeId, CancellationToken ct)
    {
        var type = (await GetSessionTypesAsync(ct)).FirstOrDefault(t => t.Id == sessionTypeId)
            ?? throw StudioException.NotFound($"Session type {sessionTypeId} does not exist.");
        if (!string.Equals(type.Type, "Appointment", StringComparison.OrdinalIgnoreCase))
            throw StudioException.BadRequest($"Session type {sessionTypeId} is not a one-to-one session type.");
        return type;
    }

    private async Task<List<MbAvailability>> GetAvailabilitiesAsync(int sessionTypeId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var gate = new SemaphoreSlim(3);
        var tasks = new List<Task<List<MbAvailability>>>();
        for (var chunk = from; chunk <= to; chunk = chunk.AddDays(31))
        {
            var chunkStart = chunk;
            var chunkEnd = chunk.AddDays(30) < to ? chunk.AddDays(30) : to;
            tasks.Add(Task.Run(async () =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    return await PageAllAsync<MbAvailability, MbAvailabilitiesResponse>("/appointment/bookableitems", _ => new()
                    {
                        new("sessionTypeIds", sessionTypeId.ToString(CultureInfo.InvariantCulture)),
                        new("startDate", D(chunkStart.ToDateTime(TimeOnly.MinValue))),
                        new("endDate", D(chunkEnd.ToDateTime(TimeOnly.MinValue))),
                    }, false, r => r.Availabilities, ct);
                }
                finally { gate.Release(); }
            }, ct));
        }
        return (await Task.WhenAll(tasks)).SelectMany(x => x).ToList();
    }

    private static int LengthMinutes(MbAvailability a, MbSessionType type) =>
        a.SessionType?.StaffTimeLength ?? a.SessionType?.DefaultTimeLength ?? type.StaffTimeLength ?? type.DefaultTimeLength ?? 60;

    private static DateTime BookableEnd(MbAvailability a, int length) => a.BookableEndDateTime ?? a.EndDateTime.AddMinutes(-length);

    private static IEnumerable<DateTime> StartsIn(MbAvailability a, int length)
    {
        var last = BookableEnd(a, length);
        var t = a.StartDateTime;
        var rem = (int)(t.TimeOfDay.TotalMinutes % SlotGridMinutes);
        if (rem != 0) t = t.AddMinutes(SlotGridMinutes - rem);
        for (; t <= last; t = t.AddMinutes(SlotGridMinutes)) yield return t;
    }

    public async Task<List<SlotDto>> ListSlotsAsync(int sessionTypeId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        ValidateRange(from, to);
        var type = await RequireAppointmentTypeAsync(sessionTypeId, ct);
        var tz = await GetSiteTimeZoneAsync(ct);
        var now = NowLocal(tz);
        var availabilities = await GetAvailabilitiesAsync(sessionTypeId, from, to, ct);

        var starts = new HashSet<(DateTime, int)>();
        foreach (var a in availabilities)
        {
            var length = LengthMinutes(a, type);
            foreach (var s in StartsIn(a, length))
            {
                var day = DateOnly.FromDateTime(s);
                if (day < from || day > to || s <= now) continue;
                starts.Add((s, length));
            }
        }

        return starts.OrderBy(s => s.Item1).ThenBy(s => s.Item2)
            .Select(s => new SlotDto(ToOffset(tz, s.Item1), ToOffset(tz, s.Item1.AddMinutes(s.Item2))))
            .ToList();
    }

    // ---------- clients ----------

    private async Task<string> ResolveEmailAsync(string identityName)
    {
        var user = await _userManager.FindByNameAsync(identityName);
        var email = user?.Email;
        if (string.IsNullOrWhiteSpace(email)) email = identityName;
        if (!email.Contains('@')) throw StudioException.BadRequest("Your account has no email address, which the studio requires.");
        return email.Trim();
    }

    private async Task<MbClient?> FindClientAsync(string email, CancellationToken ct)
    {
        var clients = await PageAllAsync<MbClient, MbClientsResponse>("/client/clients",
            _ => new() { new("searchText", email) }, true, r => r.Clients, ct);
        var matches = clients.Where(c => string.Equals(c.Email?.Trim(), email, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.FirstOrDefault(c => c.Active) ?? matches.FirstOrDefault();
    }

    private async Task<MbClient> CreateClientAsync(string email, ClientDetails d, CancellationToken ct)
    {
        var missing = new List<string>();
        if (d.HomeLocationId == null) missing.Add("homeLocationId");
        if (string.IsNullOrWhiteSpace(d.StreetAddress)) missing.Add("streetAddress");
        if (string.IsNullOrWhiteSpace(d.City)) missing.Add("city");
        if (string.IsNullOrWhiteSpace(d.State)) missing.Add("state");
        if (string.IsNullOrWhiteSpace(d.PostalCode)) missing.Add("postalCode");
        if (string.IsNullOrWhiteSpace(d.DateOfBirth)) missing.Add("dateOfBirth");
        if (string.IsNullOrWhiteSpace(d.MobilePhone)) missing.Add("mobilePhone");
        if (string.IsNullOrWhiteSpace(d.ReferredBy)) missing.Add("referredBy");
        if (missing.Count > 0)
            throw StudioException.BadRequest("This is your first booking with the studio; these details are required: " + string.Join(", ", missing) + ".");

        if (!DateOnly.TryParseExact(d.DateOfBirth!.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob)
            || dob > DateOnly.FromDateTime(DateTime.UtcNow) || dob.Year < 1900)
            throw StudioException.BadRequest("dateOfBirth must be a past date formatted yyyy-MM-dd.");

        var location = (await GetLocationsAsync(ct)).FirstOrDefault(l => l.Id == d.HomeLocationId)
            ?? throw StudioException.BadRequest($"homeLocationId {d.HomeLocationId} is not a studio location.");

        var referral = (await GetReferralTypesAsync(ct)).FirstOrDefault(r => string.Equals(r, d.ReferredBy!.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw StudioException.BadRequest("referredBy must be one of: " + string.Join(", ", await GetReferralTypesAsync(ct)) + ".");

        var local = email[..email.IndexOf('@')];
        var first = string.IsNullOrWhiteSpace(d.FirstName) ? local : d.FirstName.Trim();
        var last = string.IsNullOrWhiteSpace(d.LastName) ? "Shopper" : d.LastName.Trim();

        var created = await _mb.PostAsync<MbAddClientResponse>("/client/addclient", new
        {
            FirstName = first,
            LastName = last,
            Email = email,
            AddressLine1 = d.StreetAddress!.Trim(),
            City = d.City!.Trim(),
            State = d.State!.Trim(),
            PostalCode = d.PostalCode!.Trim(),
            BirthDate = dob.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            MobilePhone = d.MobilePhone!.Trim(),
            ReferredBy = referral,
            HomeLocation = new { Id = location.Id },
        });
        if (created.Client?.Id == null) throw new StudioException(502, "The studio system did not return the new client.");
        return created.Client;
    }

    private static async Task<T> WithClientLockAsync<T>(string email, Func<Task<T>> action, CancellationToken ct)
    {
        var gate = ClientLocks[(int)((uint)StringComparer.OrdinalIgnoreCase.GetHashCode(email) % ClientLocks.Length)];
        await gate.WaitAsync(ct);
        try { return await action(); }
        finally { gate.Release(); }
    }

    // ---------- 3. book ----------

    private async Task<List<MbVisit>> GetVisitsAsync(string clientId, DateTime from, DateTime to, CancellationToken ct)
    {
        var visits = await PageAllAsync<MbVisit, MbVisitsResponse>("/client/clientvisits", _ => new()
        {
            new("clientId", clientId), new("startDate", D(from)), new("endDate", D(to)),
        }, true, r => r.Visits, ct);
        return visits.Where(v => !IsCancelled(v.AppointmentStatus)).ToList();
    }

    private static bool IsCancelled(string? status) =>
        status != null && status.Contains("cancel", StringComparison.OrdinalIgnoreCase);

    private async Task<MbClass?> GetClassAsync(int classId, bool asStaff, CancellationToken ct)
    {
        var res = await _mb.GetAsync<MbClassesResponse>("/class/classes", new KeyValuePair<string, string?>[]
        {
            new("classIds", classId.ToString(CultureInfo.InvariantCulture)),
        }, asStaff, ct);
        return res.Classes?.FirstOrDefault(c => c.Id == classId);
    }

    public Task<BookingDto> BookClassAsync(string identityName, int classId, ClientDetails details, CancellationToken ct) =>
        BookClassCoreAsync(identityName, classId, details, admin: false, ct);

    public async Task<BookingDto> AdminBookClassAsync(string shopperEmail, int classId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(shopperEmail) || !shopperEmail.Contains('@'))
            throw StudioException.BadRequest("'shopper' must be the shopper's email address.");
        return await BookClassCoreAsync(shopperEmail.Trim(), classId, new ClientDetails(), admin: true, ct);
    }

    private async Task<BookingDto> BookClassCoreAsync(string who, int classId, ClientDetails details, bool admin, CancellationToken ct)
    {
        if (classId <= 0) throw StudioException.BadRequest("classId is required.");
        var email = admin ? who : await ResolveEmailAsync(who);
        var tz = await GetSiteTimeZoneAsync(ct);

        return await WithClientLockAsync(email, async () =>
        {
            var client = await FindClientAsync(email, ct);
            var cls = await GetClassAsync(classId, admin, ct) ?? throw StudioException.NotFound($"Class {classId} does not exist.");

            // A retry of a request that already succeeded must succeed again, not fail or double-book.
            if (client?.Id != null)
            {
                var existing = (await GetVisitsAsync(client.Id, cls.StartDateTime.Date, cls.StartDateTime.Date.AddDays(1), ct))
                    .FirstOrDefault(v => v.ClassId == classId);
                if (existing != null) return ClassBooking(tz, existing.Id, cls);
            }

            if (cls.IsCanceled || !cls.Active) throw StudioException.Conflict("This class has been cancelled.");
            if (cls.EndDateTime <= NowLocal(tz)) throw StudioException.Conflict("This class has already finished.");
            if (admin)
            {
                if (cls.MaxCapacity is { } max && (cls.TotalBooked ?? 0) >= max) throw StudioException.Conflict("This class is full.");
                if (client?.Id == null)
                    throw StudioException.NotFound("That shopper is not a studio client yet; they must make their first booking themselves.");
            }
            else
            {
                if (IsEnrollment(cls)) throw StudioException.BadRequest("This is an enrolment course and cannot be booked as a single class.");
                if (!cls.IsAvailable) throw StudioException.Conflict("This class is full or not open for online booking.");
                client ??= await CreateClientAsync(email, details, ct);
            }

            var visit = await _mb.PostAsync<MbAddToClassResponse>("/class/addclienttoclass", new
            {
                ClientId = client!.Id,
                ClassId = classId,
                RequirePayment = false,
                Test = false,
                SendEmail = false,
            }, CancellationToken.None);
            if (visit.Visit == null) throw new StudioException(502, "The studio system did not confirm the booking.");
            return ClassBooking(tz, visit.Visit.Id, cls);
        }, ct);
    }

    private static BookingDto ClassBooking(TimeZoneInfo tz, long visitId, MbClass c) =>
        new(visitId, "class", c.Id, null, c.Location?.Id, ToOffset(tz, c.StartDateTime), ToOffset(tz, c.EndDateTime));

    public async Task<BookingDto> BookAppointmentAsync(string identityName, int sessionTypeId, string startTime, int? locationId,
        long? staffId, ClientDetails details, CancellationToken ct)
    {
        if (sessionTypeId <= 0) throw StudioException.BadRequest("sessionTypeId is required.");
        if (string.IsNullOrWhiteSpace(startTime)) throw StudioException.BadRequest("startTime is required.");
        var type = await RequireAppointmentTypeAsync(sessionTypeId, ct);
        var tz = await GetSiteTimeZoneAsync(ct);
        var start = ParseStart(tz, startTime);
        var email = await ResolveEmailAsync(identityName);

        return await WithClientLockAsync(email, async () =>
        {
            var client = await FindClientAsync(email, ct);

            if (client?.Id != null)
            {
                var existing = (await GetAppointmentsAsync(client.Id, start.Date, start.Date.AddDays(1), ct))
                    .FirstOrDefault(a => a.SessionTypeId == sessionTypeId && a.StartDateTime == start);
                if (existing != null) return AppointmentBooking(tz, existing);
            }

            if (start <= NowLocal(tz)) throw StudioException.Conflict("That time has already passed.");

            var day = DateOnly.FromDateTime(start);
            var candidates = (await GetAvailabilitiesAsync(sessionTypeId, day, day, ct))
                .Where(a => a.Staff != null && a.Location != null
                    && a.StartDateTime <= start && start <= BookableEnd(a, LengthMinutes(a, type)))
                .Where(a => locationId == null || a.Location!.Id == locationId)
                .Where(a => staffId == null || a.Staff!.Id == staffId)
                .ToList();
            if (candidates.Count == 0)
                throw StudioException.Conflict("No trainer can take that session at that time.");

            client ??= await CreateClientAsync(email, details, ct);
            var home = client.HomeLocation?.Id ?? details.HomeLocationId;

            StudioException? last = null;
            foreach (var c in candidates.OrderByDescending(c => c.Location!.Id == home).ThenBy(c => c.Staff!.Id)
                         .DistinctBy(c => (c.Staff!.Id, c.Location!.Id)))
            {
                try
                {
                    var res = await _mb.PostAsync<MbAddAppointmentResponse>("/appointment/addappointment", new
                    {
                        ClientId = client.Id,
                        LocationId = c.Location!.Id,
                        SessionTypeId = sessionTypeId,
                        StaffId = c.Staff!.Id,
                        StartDateTime = DT(start),
                        SendEmail = false,
                        ApplyPayment = false,
                        Test = false,
                    }, CancellationToken.None);
                    if (res.Appointment == null || res.Appointment.Id == 0)
                        throw new StudioException(502, "The studio system did not confirm the booking.");
                    return AppointmentBooking(tz, res.Appointment);
                }
                catch (StudioException ex) when (ex.StatusCode == 409)
                {
                    last = ex; // try the next trainer
                }
            }
            throw last ?? StudioException.Conflict("That time cannot be booked.");
        }, ct);
    }

    private static DateTime ParseStart(TimeZoneInfo tz, string text)
    {
        text = text.Trim();
        if (HasOffset.IsMatch(text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            return TimeZoneInfo.ConvertTime(dto, tz).DateTime;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        throw StudioException.BadRequest("startTime must be an ISO-8601 date-time.");
    }

    private async Task<List<MbAppointment>> GetAppointmentsAsync(string clientId, DateTime from, DateTime to, CancellationToken ct)
    {
        var list = await PageAllAsync<MbAppointment, MbAppointmentsResponse>("/appointment/staffappointments", _ => new()
        {
            new("clientIds", clientId), new("startDate", D(from)), new("endDate", D(to)),
        }, true, r => r.Appointments, ct);
        return list.Where(a => a.ClientId == clientId && !IsCancelled(a.Status)).ToList();
    }

    private static BookingDto AppointmentBooking(TimeZoneInfo tz, MbAppointment a) =>
        new(a.Id, "appointment", null, a.SessionTypeId, a.LocationId, ToOffset(tz, a.StartDateTime), ToOffset(tz, a.EndDateTime));

    // ---------- 4. cancel ----------

    private static string CancelKey(string kind, long id) => $"studio:cancelled:{kind}:{id}";

    public async Task CancelClassBookingAsync(string identityName, long bookingId, CancellationToken ct)
    {
        var email = await ResolveEmailAsync(identityName);
        var tz = await GetSiteTimeZoneAsync(ct);
        await WithClientLockAsync(email, async () =>
        {
            var client = await FindClientAsync(email, ct);
            var today = NowLocal(tz).Date;
            var visit = client?.Id == null ? null
                : (await GetVisitsAsync(client.Id, today.AddDays(-1), today.AddDays(MaxRangeDays), ct))
                    .FirstOrDefault(v => v.Id == bookingId && v.ClassId is > 0);

            if (visit == null)
            {
                // Already cancelled by an earlier (retried) request: still a success.
                if (client?.Id != null && _cache.TryGetValue(CancelKey("class", bookingId), out string? owner) && owner == client.Id) return 0;
                throw StudioException.NotFound("No such class booking.");
            }
            if (visit.StartDateTime <= NowLocal(tz)) throw StudioException.Conflict("This class has already started.");

            await _mb.PostAsync<object>("/class/removeclientfromclass", new
            {
                ClientId = client!.Id,
                ClassId = visit.ClassId,
                Test = false,
                SendEmail = false,
                LateCancel = false,
            }, CancellationToken.None);
            _cache.Set(CancelKey("class", bookingId), client.Id, TimeSpan.FromDays(2));
            return 0;
        }, ct);
    }

    public async Task CancelAppointmentBookingAsync(string identityName, long bookingId, CancellationToken ct)
    {
        var email = await ResolveEmailAsync(identityName);
        var tz = await GetSiteTimeZoneAsync(ct);
        await WithClientLockAsync(email, async () =>
        {
            var client = await FindClientAsync(email, ct);
            var today = NowLocal(tz).Date;
            var appt = client?.Id == null ? null
                : (await GetAppointmentsAsync(client.Id, today.AddDays(-1), today.AddDays(MaxRangeDays), ct))
                    .FirstOrDefault(a => a.Id == bookingId);

            if (appt == null)
            {
                if (client?.Id != null && _cache.TryGetValue(CancelKey("appointment", bookingId), out string? owner) && owner == client.Id) return 0;
                throw StudioException.NotFound("No such appointment booking.");
            }
            if (appt.StartDateTime <= NowLocal(tz)) throw StudioException.Conflict("This session has already started.");

            await _mb.PostAsync<object>("/appointment/updateappointment", new
            {
                AppointmentId = bookingId,
                Execute = "Cancel",
                SendEmail = false,
                Test = false,
            }, CancellationToken.None);
            _cache.Set(CancelKey("appointment", bookingId), client!.Id, TimeSpan.FromDays(2));
            return 0;
        }, ct);
    }

    // ---------- 5. trainers ----------

    public async Task<List<TrainerDto>> ListTrainersAsync(CancellationToken ct)
    {
        var staff = await PageAllAsync<MbStaff, MbStaffResponse>("/staff/staff", _ => new(), true, r => r.StaffMembers, ct);
        return staff.Where(s => s.ClassTeacher || s.AppointmentInstructor)
            .OrderBy(s => s.DisplayName ?? s.Name).ThenBy(s => s.Id)
            .Select(s => new TrainerDto(s.Id, s.Active, s.FirstName, s.LastName, s.DisplayName ?? s.Name, s.Email, s.MobilePhone,
                s.HomePhone, s.WorkPhone, s.Address, s.City, s.State, s.PostalCode, s.Country, s.Bio, s.ImageUrl,
                s.ClassTeacher, s.AppointmentInstructor))
            .ToList();
    }
}
