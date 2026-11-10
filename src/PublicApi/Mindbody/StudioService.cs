using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.PublicApi.Mindbody;

/// <summary>
/// Studio features on top of the Mindbody API.
/// </summary>
public class StudioService
{
    private const int DEFAULT_CLASS_DAYS = 7;
    private const int MAX_CLASS_RANGE_DAYS = 92;
    private const int MAX_SLOT_RANGE_DAYS = 366;
    private static readonly TimeSpan TimeZoneTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan CancelledTtl = TimeSpan.FromDays(30);
    private static readonly SemaphoreSlim[] ClientLocks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly MindbodyClient _mb;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMemoryCache _cache;

    // The site time zone is shared by all requests (this service is created per request).
    private static readonly SemaphoreSlim _tzLock = new(1, 1);
    private static TimeZoneInfo? _tz;
    private static DateTime _tzFetchedUtc = DateTime.MinValue;

    public StudioService(MindbodyClient mb, UserManager<ApplicationUser> userManager, IMemoryCache cache)
    {
        _mb = mb;
        _userManager = userManager;
        _cache = cache;
    }

    // ---------------------------------------------------------------- time helpers

    private async Task<TimeZoneInfo> GetSiteTimeZoneAsync(CancellationToken ct)
    {
        if (_tz != null && DateTime.UtcNow - _tzFetchedUtc < TimeZoneTtl)
        {
            return _tz;
        }

        await _tzLock.WaitAsync(ct);
        try
        {
            if (_tz != null && DateTime.UtcNow - _tzFetchedUtc < TimeZoneTtl)
            {
                return _tz;
            }

            try
            {
                var sites = await _mb.GetAllAsync("site/sites", "Sites", ct: ct);
                var id = sites.Select(s => Str(s, "TimeZone")).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                if (id == null)
                {
                    throw new StudioException(502, "The studio did not report its time zone.");
                }

                _tz = TimeZoneInfo.FindSystemTimeZoneById(id);
                _tzFetchedUtc = DateTime.UtcNow;
            }
            catch (Exception ex) when (_tz != null && ex is not OperationCanceledException)
            {
                // keep serving with the last known zone; try again shortly
                _tzFetchedUtc = DateTime.UtcNow - TimeZoneTtl + TimeSpan.FromMinutes(1);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw new StudioException(502, "The studio time zone is not known to this server.", ex);
            }

            return _tz!;
        }
        finally
        {
            _tzLock.Release();
        }
    }

    private static DateTimeOffset ToOffset(DateTime siteLocal, TimeZoneInfo tz)
    {
        var local = DateTime.SpecifyKind(siteLocal, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        var offset = tz.IsAmbiguousTime(local) ? tz.GetAmbiguousTimeOffsets(local).Max() : tz.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static DateTime LocalOf(DateTimeOffset instant, TimeZoneInfo tz)
        => DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(instant, tz).DateTime, DateTimeKind.Unspecified);

    private static string Iso(DateTime d) => d.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    private static DateTime ParseLocal(JsonElement e, string prop)
    {
        var s = Str(e, prop);
        if (s != null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d))
        {
            return DateTime.SpecifyKind(d, DateTimeKind.Unspecified);
        }

        throw new StudioException(502, "The studio system sent a time that could not be read.");
    }

    // ---------------------------------------------------------------- json helpers

    private static string? Str(JsonElement e, string prop)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string prop)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

    private static long? Long(JsonElement e, string prop)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(prop, out var v))
        {
            return null;
        }

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n))
        {
            return n;
        }

        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s))
        {
            return s;
        }

        return null;
    }

    private static JsonElement Obj(JsonElement e, string prop)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) ? v : default;

    private static IEnumerable<JsonElement> Arr(JsonElement e, string prop)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private static StudioException Reject(MindbodyApiException ex, string context)
    {
        if (ex.HttpStatus == 401 || ex.HttpStatus == 403)
        {
            return new StudioException(502, "The studio system refused this app's credentials.", ex);
        }

        if (ex.HttpStatus == 429 || ex.HttpStatus >= 500)
        {
            return new StudioException(503, "The studio system is busy, try again shortly.", ex);
        }

        return new StudioException(409, string.IsNullOrWhiteSpace(ex.Message) ? context : ex.Message, ex);
    }

    // ---------------------------------------------------------------- dates

    public static DateOnly? ParseDate(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return d;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            return DateOnly.FromDateTime(dto.DateTime);
        }

        throw new StudioException(400, $"'{name}' is not a valid date; use yyyy-MM-dd.");
    }

    // ---------------------------------------------------------------- 1. classes

    public async Task<List<StudioClassDto>> ListClassesAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var tz = await GetSiteTimeZoneAsync(ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);
        var start = from ?? today;
        var end = to ?? start.AddDays(DEFAULT_CLASS_DAYS);
        if (end < start)
        {
            throw new StudioException(400, "'to' must not be before 'from'.");
        }

        if (end.DayNumber - start.DayNumber > MAX_CLASS_RANGE_DAYS)
        {
            throw new StudioException(400, $"The range may cover at most {MAX_CLASS_RANGE_DAYS} days.");
        }

        List<JsonElement> raw;
        try
        {
            raw = await _mb.GetAllAsync("class/classes", "Classes", new[]
            {
                new KeyValuePair<string, string>("StartDateTime", Iso(start.ToDateTime(TimeOnly.MinValue))),
                new KeyValuePair<string, string>("EndDateTime", Iso(end.ToDateTime(new TimeOnly(23, 59, 59)))),
            }, ct: ct);
        }
        catch (MindbodyApiException ex)
        {
            throw Reject(ex, "The classes could not be listed.");
        }

        return raw.Where(IsAttendable)
            .Select(c => ToClassDto(c, tz))
            .OrderBy(c => c.StartTime)
            .ThenBy(c => c.ClassId)
            .ToList();
    }

    private static bool IsEnrollment(JsonElement c)
        => string.Equals(Str(Obj(Obj(c, "ClassDescription"), "Program"), "ScheduleType"), "Enrollment", StringComparison.OrdinalIgnoreCase);

    private static bool IsAttendable(JsonElement c)
    {
        if (Bool(c, "IsCanceled"))
        {
            return false;
        }

        // Active / IsAvailable are missing only on odd payloads; treat missing as not attendable.
        if (!Bool(c, "Active") || !Bool(c, "IsAvailable"))
        {
            return false;
        }

        return !IsEnrollment(c);
    }

    private static StudioClassDto ToClassDto(JsonElement c, TimeZoneInfo tz)
    {
        var desc = Obj(c, "ClassDescription");
        var loc = Obj(c, "Location");
        var staff = Obj(c, "Staff");
        var spots = (int)((Long(c, "WebCapacity") ?? 0) - (Long(c, "WebBooked") ?? 0));
        return new StudioClassDto
        {
            ClassId = Long(c, "Id") ?? 0,
            Name = Str(desc, "Name"),
            Description = Str(desc, "Description"),
            StartTime = ToOffset(ParseLocal(c, "StartDateTime"), tz),
            EndTime = ToOffset(ParseLocal(c, "EndDateTime"), tz),
            LocationId = (int?)Long(loc, "Id"),
            LocationName = Str(loc, "Name"),
            TrainerName = Str(staff, "DisplayName") ?? Str(staff, "Name"),
            SpotsAvailable = Math.Max(0, spots),
        };
    }

    private async Task<JsonElement?> FindClassAsync(long classId, TimeZoneInfo tz, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        try
        {
            var found = await _mb.GetAllAsync("class/classes", "Classes", new[]
            {
                new KeyValuePair<string, string>("ClassIds", classId.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("StartDateTime", Iso(today.AddDays(-2))),
                new KeyValuePair<string, string>("EndDateTime", Iso(today.AddDays(800))),
            }, ct: ct);
            foreach (var c in found)
            {
                if (Long(c, "Id") == classId)
                {
                    return c;
                }
            }

            return null;
        }
        catch (MindbodyApiException ex)
        {
            throw Reject(ex, "The class could not be looked up.");
        }
    }

    // ---------------------------------------------------------------- 2. session times

    private async Task<(JsonElement Type, int Minutes)> GetAppointmentTypeAsync(int sessionTypeId, CancellationToken ct)
    {
        if (sessionTypeId <= 0)
        {
            throw new StudioException(400, "'sessionTypeId' is required.");
        }

        JsonElement? found = null;
        try
        {
            var types = await _mb.GetAllAsync("site/sessiontypes", "SessionTypes", new[]
            {
                new KeyValuePair<string, string>("SessionTypeIds", sessionTypeId.ToString(CultureInfo.InvariantCulture)),
            }, ct: ct);
            foreach (var t in types)
            {
                if (Long(t, "Id") == sessionTypeId)
                {
                    found = t;
                    break;
                }
            }
        }
        catch (MindbodyApiException ex)
        {
            throw Reject(ex, "The session type could not be looked up.");
        }

        if (found == null)
        {
            throw new StudioException(404, $"There is no session type {sessionTypeId}.");
        }

        if (!string.Equals(Str(found.Value, "Type"), "Appointment", StringComparison.OrdinalIgnoreCase))
        {
            throw new StudioException(400, $"Session type {sessionTypeId} is not a one-to-one session.");
        }

        var minutes = (int)(Long(found.Value, "DefaultTimeLength") ?? Long(found.Value, "StaffTimeLength") ?? 0);
        return (found.Value, minutes);
    }

    public async Task<List<StudioSlotDto>> ListSlotsAsync(int sessionTypeId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (from == null || to == null)
        {
            throw new StudioException(400, "'from' and 'to' dates are required.");
        }

        if (to < from)
        {
            throw new StudioException(400, "'to' must not be before 'from'.");
        }

        if (to.Value.DayNumber - from.Value.DayNumber > MAX_SLOT_RANGE_DAYS)
        {
            throw new StudioException(400, $"The range may cover at most {MAX_SLOT_RANGE_DAYS} days.");
        }

        var (_, minutes) = await GetAppointmentTypeAsync(sessionTypeId, ct);
        if (minutes <= 0)
        {
            throw new StudioException(422, $"Session type {sessionTypeId} has no set length, so its end times are not known.");
        }

        var tz = await GetSiteTimeZoneAsync(ct);
        List<JsonElement> raw;
        try
        {
            raw = await _mb.GetAllAsync("appointment/activesessiontimes", "ActiveSessionTimes", new[]
            {
                new KeyValuePair<string, string>("ScheduleType", "Appointment"),
                new KeyValuePair<string, string>("SessionTypeIds", sessionTypeId.ToString(CultureInfo.InvariantCulture)),
            }, ct: ct);
        }
        catch (MindbodyApiException ex)
        {
            throw Reject(ex, "The session times could not be listed.");
        }

        var times = raw.Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => TimeOnly.TryParse(e.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? (TimeOnly?)t : null)
            .Where(t => t != null).Select(t => t!.Value).Distinct().OrderBy(t => t).ToList();

        var result = new List<StudioSlotDto>();
        for (var day = from.Value; day <= to.Value; day = day.AddDays(1))
        {
            foreach (var t in times)
            {
                var local = day.ToDateTime(t);
                if (tz.IsInvalidTime(local))
                {
                    continue;
                }

                var start = ToOffset(local, tz);
                var end = start.AddMinutes(minutes);
                result.Add(new StudioSlotDto
                {
                    SessionTypeId = sessionTypeId,
                    StartTime = start,
                    EndTime = TimeZoneInfo.ConvertTime(end, tz),
                });
            }
        }

        return result;
    }

    // ---------------------------------------------------------------- identity / clients

    private static string Key(string email) => email.Trim().ToLowerInvariant();

    private static SemaphoreSlim LockFor(string email)
        => ClientLocks[(uint)Key(email).GetHashCode() % ClientLocks.Length];

    private async Task<string> EmailOfAsync(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new StudioException(401, "The token does not identify a user.");
        }

        var user = await _userManager.FindByNameAsync(userName);
        var email = user?.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            email = userName.Contains('@') ? userName : null;
        }

        if (email == null)
        {
            throw new StudioException(400, "Your account has no email address, which the studio needs.");
        }

        return email.Trim();
    }

    private async Task<JsonElement?> FindClientAsync(string email, CancellationToken ct)
    {
        try
        {
            var clients = await _mb.GetAllAsync("client/clients", "Clients", new[]
            {
                new KeyValuePair<string, string>("SearchText", email),
            }, true, ct);
            var matches = clients.Where(c => string.Equals(Str(c, "Email")?.Trim(), email, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
            {
                return null;
            }

            return matches.FirstOrDefault(c => !c.TryGetProperty("Active", out var a) || a.ValueKind != JsonValueKind.False);
        }
        catch (MindbodyApiException ex)
        {
            throw Reject(ex, "The studio client could not be looked up.");
        }
    }

    private async Task<string> EnsureClientAsync(string email, NewClientDetails details, CancellationToken ct)
    {
        var existing = await FindClientAsync(email, ct);
        if (existing != null)
        {
            return Str(existing.Value, "Id") ?? throw new StudioException(502, "The studio client has no id.");
        }

        var missing = new List<string>();
        if (details.HomeLocationId is null or <= 0) missing.Add("homeLocationId");
        if (string.IsNullOrWhiteSpace(details.StreetAddress)) missing.Add("streetAddress");
        if (string.IsNullOrWhiteSpace(details.City)) missing.Add("city");
        if (string.IsNullOrWhiteSpace(details.State)) missing.Add("state");
        if (string.IsNullOrWhiteSpace(details.PostalCode)) missing.Add("postalCode");
        if (string.IsNullOrWhiteSpace(details.DateOfBirth)) missing.Add("dateOfBirth");
        if (string.IsNullOrWhiteSpace(details.MobilePhone)) missing.Add("mobilePhone");
        if (string.IsNullOrWhiteSpace(details.ReferredBy)) missing.Add("referredBy");
        if (missing.Count > 0)
        {
            throw new StudioException(400, "This is your first booking, so the studio needs more details: " + string.Join(", ", missing) + ".");
        }

        var dob = ParseDate(details.DateOfBirth, "dateOfBirth")!.Value;
        if (dob > DateOnly.FromDateTime(DateTime.UtcNow) || dob.Year < 1900)
        {
            throw new StudioException(400, "'dateOfBirth' is not a plausible date of birth.");
        }

        // The home location has to be a real studio location.
        try
        {
            var locations = await _mb.GetAllAsync("site/locations", "Locations", ct: ct);
            if (!locations.Any(l => Long(l, "Id") == details.HomeLocationId))
            {
                throw new StudioException(400, $"There is no studio location {details.HomeLocationId}.");
            }
        }
        catch (MindbodyApiException ex)
        {
            throw Reject(ex, "The studio locations could not be listed.");
        }

        var local = email.Split('@')[0];
        var firstName = string.IsNullOrWhiteSpace(details.FirstName) ? local : details.FirstName!.Trim();
        var lastName = string.IsNullOrWhiteSpace(details.LastName) ? "eShop" : details.LastName!.Trim();

        try
        {
            var created = await _mb.PostAsync("client/addclient", new
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                AddressLine1 = details.StreetAddress!.Trim(),
                City = details.City!.Trim(),
                State = details.State!.Trim(),
                PostalCode = details.PostalCode!.Trim(),
                BirthDate = dob.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                MobilePhone = details.MobilePhone!.Trim(),
                ReferredBy = details.ReferredBy!.Trim(),
                HomeLocation = new { Id = details.HomeLocationId },
                SendAccountEmails = false,
            }, ct);
            return Str(Obj(created, "Client"), "Id") ?? throw new StudioException(502, "The studio did not return the new client.");
        }
        catch (MindbodyApiException ex) when (ex.Code == "InvalidClientCreation")
        {
            // created by a parallel request or an earlier attempt that timed out
            var again = await FindClientAsync(email, ct);
            if (again != null)
            {
                return Str(again.Value, "Id")!;
            }

            throw Reject(ex, "The studio client could not be created.");
        }
        catch (MindbodyApiException ex)
        {
            throw Reject(ex, "The studio client could not be created.");
        }
    }

    // ---------------------------------------------------------------- 3. book

    public async Task<(StudioBookingDto Booking, bool Created)> BookClassAsync(string? userName, ClassBookingRequest request, CancellationToken ct)
    {
        if (request.ClassId <= 0)
        {
            throw new StudioException(400, "'classId' is required.");
        }

        var email = await EmailOfAsync(userName);
        var tz = await GetSiteTimeZoneAsync(ct);
        var cls = await FindClassAsync(request.ClassId, tz, ct)
            ?? throw new StudioException(404, $"There is no class {request.ClassId}.");
        if (!IsAttendable(cls))
        {
            throw new StudioException(409, "This class is cancelled, full or not open for booking.");
        }

        var gate = LockFor(email);
        await gate.WaitAsync(ct);
        try
        {
            var clientId = await EnsureClientAsync(email, request, ct);
            return await PlaceInClassAsync(clientId, cls, tz, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<(StudioBookingDto Booking, bool Created)> AdminBookClassAsync(AdminClassBookingRequest request, CancellationToken ct)
    {
        var shopper = new[] { request.Shopper, request.Email, request.UserName }.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim();
        if (shopper == null)
        {
            throw new StudioException(400, "'shopper' is required.");
        }

        if (request.ClassId <= 0)
        {
            throw new StudioException(400, "'classId' is required.");
        }

        var user = await _userManager.FindByNameAsync(shopper) ?? await _userManager.FindByEmailAsync(shopper);
        if (user == null)
        {
            throw new StudioException(404, $"There is no shopper '{shopper}'.");
        }

        var email = await EmailOfAsync(user.UserName);
        var tz = await GetSiteTimeZoneAsync(ct);
        var cls = await FindClassAsync(request.ClassId, tz, ct)
            ?? throw new StudioException(404, $"There is no class {request.ClassId}.");
        if (Bool(cls, "IsCanceled"))
        {
            throw new StudioException(409, "This class is cancelled.");
        }

        var gate = LockFor(email);
        await gate.WaitAsync(ct);
        try
        {
            var client = await FindClientAsync(email, ct)
                ?? throw new StudioException(409, "This shopper is not a studio client yet; they must make their first booking themselves.");
            return await PlaceInClassAsync(Str(client, "Id")!, cls, tz, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<JsonElement>> VisitsAsync(string clientId, CancellationToken ct)
    {
        var from = DateTime.UtcNow.Date.AddDays(-2);
        try
        {
            return await _mb.GetAllAsync("client/clientvisits", "Visits", new[]
            {
                new KeyValuePair<string, string>("ClientId", clientId),
                new KeyValuePair<string, string>("StartDate", Iso(from)),
                new KeyValuePair<string, string>("EndDate", Iso(from.AddDays(800))),
            }, true, ct);
        }
        catch (MindbodyApiException ex)
        {
            throw Reject(ex, "The client's bookings could not be listed.");
        }
    }

    private static bool IsLive(JsonElement e)
    {
        var status = Str(e, "AppointmentStatus") ?? Str(e, "Status");
        return status == null || !(status.Contains("Cancel", StringComparison.OrdinalIgnoreCase) || status.Equals("NoShow", StringComparison.OrdinalIgnoreCase));
    }

    private static StudioBookingDto ClassBooking(long visitId, JsonElement cls, TimeZoneInfo tz, bool already)
    {
        var dto = ToClassDto(cls, tz);
        return new StudioBookingDto
        {
            BookingId = visitId,
            Type = "class",
            ClassId = dto.ClassId,
            Name = dto.Name,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            StaffId = Long(Obj(cls, "Staff"), "Id"),
            LocationId = dto.LocationId,
            AlreadyBooked = already,
        };
    }

    private async Task<(StudioBookingDto, bool)> PlaceInClassAsync(string clientId, JsonElement cls, TimeZoneInfo tz, CancellationToken ct)
    {
        var classId = Long(cls, "Id")!.Value;
        var existing = (await VisitsAsync(clientId, ct)).FirstOrDefault(v => Long(v, "ClassId") == classId && IsLive(v));
        if (existing.ValueKind == JsonValueKind.Object)
        {
            return (ClassBooking(Long(existing, "Id") ?? 0, cls, tz, true), false);
        }

        try
        {
            var added = await _mb.PostAsync("class/addclienttoclass", new
            {
                ClientId = clientId,
                ClassId = classId,
                RequirePayment = false,
                SendEmail = false,
            }, ct);
            var visitId = Long(Obj(added, "Visit"), "Id") ?? throw new StudioException(502, "The studio did not return the booking.");
            return (ClassBooking(visitId, cls, tz, false), true);
        }
        catch (MindbodyApiException ex)
        {
            throw Reject(ex, "The class could not be booked.");
        }
    }

    public async Task<(StudioBookingDto Booking, bool Created)> BookAppointmentAsync(string? userName, AppointmentBookingRequest request, CancellationToken ct)
    {
        if (request.StartTime == null)
        {
            throw new StudioException(400, "'startTime' is required (ISO-8601 with UTC offset).");
        }

        var (_, minutes) = await GetAppointmentTypeAsync(request.SessionTypeId, ct);
        if (minutes <= 0)
        {
            throw new StudioException(422, $"Session type {request.SessionTypeId} has no set length and cannot be booked online.");
        }

        var email = await EmailOfAsync(userName);
        var tz = await GetSiteTimeZoneAsync(ct);
        var startInstant = request.StartTime.Value;
        if (startInstant <= DateTimeOffset.UtcNow)
        {
            throw new StudioException(400, "'startTime' is in the past.");
        }

        var localStart = LocalOf(startInstant, tz);
        var localEnd = LocalOf(startInstant.AddMinutes(minutes), tz);

        // Only the studio's own start times are bookable.
        List<JsonElement> active;
        try
        {
            active = await _mb.GetAllAsync("appointment/activesessiontimes", "ActiveSessionTimes", new[]
            {
                new KeyValuePair<string, string>("ScheduleType", "Appointment"),
                new KeyValuePair<string, string>("SessionTypeIds", request.SessionTypeId.ToString(CultureInfo.InvariantCulture)),
            }, ct: ct);
        }
        catch (MindbodyApiException ex)
        {
            throw Reject(ex, "The session times could not be listed.");
        }

        var isActive = active.Any(e => e.ValueKind == JsonValueKind.String
            && TimeOnly.TryParse(e.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            && t == TimeOnly.FromDateTime(localStart));
        if (!isActive)
        {
            throw new StudioException(409, "A session of this type cannot start at that time.");
        }

        // trainers who are free for the whole session
        var query = new List<KeyValuePair<string, string>>
        {
            new("SessionTypeIds", request.SessionTypeId.ToString(CultureInfo.InvariantCulture)),
            new("StartDate", Iso(localStart.Date)),
            new("EndDate", Iso(localStart.Date.AddDays(1).AddSeconds(-1))),
        };
        if (request.StaffId != null)
        {
            query.Add(new("StaffIds", request.StaffId.Value.ToString(CultureInfo.InvariantCulture)));
        }

        List<JsonElement> windows;
        try
        {
            windows = await _mb.GetAllAsync("appointment/bookableitems", "Availabilities", query, true, ct);
        }
        catch (MindbodyApiException ex)
        {
            throw Reject(ex, "The trainers' availability could not be read.");
        }

        var candidates = windows
            .Where(w => ParseLocal(w, "StartDateTime") <= localStart && ParseLocal(w, "EndDateTime") >= localEnd)
            .Select(w => (Staff: Long(Obj(w, "Staff"), "Id"), Location: Long(Obj(w, "Location"), "Id")))
            .Where(c => c.Staff != null && c.Location != null)
            .Distinct()
            .ToList();
        if (candidates.Count == 0)
        {
            throw new StudioException(409, "No trainer is free at that time.");
        }

        var gate = LockFor(email);
        await gate.WaitAsync(ct);
        try
        {
            var clientId = await EnsureClientAsync(email, request, ct);

            // A repeated request must not book twice.
            var already = await FindAppointmentsAsync(clientId, localStart.Date, ct);
            var same = already.FirstOrDefault(a => Long(a, "SessionTypeId") == request.SessionTypeId
                && ParseLocal(a, "StartDateTime") == localStart && IsLive(a));
            if (same.ValueKind == JsonValueKind.Object)
            {
                return (AppointmentBooking(same, request.SessionTypeId, minutes, tz, true), false);
            }

            MindbodyApiException? last = null;
            foreach (var (staff, location) in candidates)
            {
                try
                {
                    var added = await _mb.PostAsync("appointment/addappointment", new
                    {
                        ClientId = clientId,
                        SessionTypeId = request.SessionTypeId,
                        StartDateTime = Iso(localStart),
                        StaffId = staff,
                        LocationId = location,
                        ApplyPayment = false,
                        SendEmail = false,
                    }, ct);
                    return (AppointmentBooking(Obj(added, "Appointment"), request.SessionTypeId, minutes, tz, false), true);
                }
                catch (MindbodyApiException ex) when (ex.HttpStatus is >= 400 and < 500 && ex.HttpStatus != 401 && ex.HttpStatus != 403 && ex.HttpStatus != 429)
                {
                    last = ex;
                }
            }

            throw Reject(last!, "The session could not be booked.");
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<JsonElement>> FindAppointmentsAsync(string clientId, DateTime fromLocal, CancellationToken ct)
    {
        try
        {
            return await _mb.GetAllAsync("appointment/staffappointments", "Appointments", new[]
            {
                new KeyValuePair<string, string>("ClientIds", clientId),
                new KeyValuePair<string, string>("StartDate", Iso(fromLocal)),
                new KeyValuePair<string, string>("EndDate", Iso(fromLocal.AddDays(800))),
            }, true, ct);
        }
        catch (MindbodyApiException ex)
        {
            throw Reject(ex, "The client's sessions could not be listed.");
        }
    }

    private static StudioBookingDto AppointmentBooking(JsonElement a, int sessionTypeId, int fallbackMinutes, TimeZoneInfo tz, bool already)
    {
        var start = ParseLocal(a, "StartDateTime");
        var endStr = Str(a, "EndDateTime");
        var startOffset = ToOffset(start, tz);
        var end = endStr != null ? ParseLocal(a, "EndDateTime") : start.AddMinutes(fallbackMinutes);
        return new StudioBookingDto
        {
            BookingId = Long(a, "Id") ?? 0,
            Type = "appointment",
            SessionTypeId = sessionTypeId,
            StartTime = startOffset,
            EndTime = ToOffset(end, tz),
            StaffId = Long(a, "StaffId"),
            LocationId = (int?)Long(a, "LocationId"),
            AlreadyBooked = already,
        };
    }

    // ---------------------------------------------------------------- 4. cancel

    private static string CancelKey(string kind, long id, string email) => $"studio-cancelled:{kind}:{id}:{Key(email)}";

    public async Task<StudioCancelDto> CancelClassBookingAsync(string? userName, long bookingId, CancellationToken ct)
    {
        var email = await EmailOfAsync(userName);
        var gate = LockFor(email);
        await gate.WaitAsync(ct);
        try
        {
            var client = await FindClientAsync(email, ct);
            if (client == null)
            {
                return AlreadyCancelledOrThrow("class", bookingId, email);
            }

            var clientId = Str(client.Value, "Id")!;
            var visit = (await VisitsAsync(clientId, ct)).FirstOrDefault(v => Long(v, "Id") == bookingId && (Long(v, "ClassId") ?? 0) > 0);
            if (visit.ValueKind != JsonValueKind.Object || !IsLive(visit))
            {
                return AlreadyCancelledOrThrow("class", bookingId, email);
            }

            try
            {
                await _mb.PostAsync("class/removeclientfromclass", new
                {
                    ClientId = clientId,
                    ClassId = Long(visit, "ClassId"),
                    SendEmail = false,
                    LateCancel = false,
                }, ct);
            }
            catch (MindbodyApiException ex) when (ex.Message.Contains("No class visit found", StringComparison.OrdinalIgnoreCase))
            {
                // already gone
            }
            catch (MindbodyApiException ex)
            {
                throw Reject(ex, "The booking could not be cancelled.");
            }

            _cache.Set(CancelKey("class", bookingId, email), true, CancelledTtl);
            return new StudioCancelDto { BookingId = bookingId };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<StudioCancelDto> CancelAppointmentBookingAsync(string? userName, long bookingId, CancellationToken ct)
    {
        var email = await EmailOfAsync(userName);
        var gate = LockFor(email);
        await gate.WaitAsync(ct);
        try
        {
            var client = await FindClientAsync(email, ct);
            if (client == null)
            {
                return AlreadyCancelledOrThrow("appointment", bookingId, email);
            }

            var clientId = Str(client.Value, "Id")!;
            var tz = await GetSiteTimeZoneAsync(ct);
            var yesterday = LocalOf(DateTimeOffset.UtcNow, tz).Date.AddDays(-2);
            var appointment = (await FindAppointmentsAsync(clientId, yesterday, ct)).FirstOrDefault(a => Long(a, "Id") == bookingId);
            if (appointment.ValueKind != JsonValueKind.Object || !IsLive(appointment))
            {
                return AlreadyCancelledOrThrow("appointment", bookingId, email);
            }

            try
            {
                await _mb.PostAsync("appointment/updateappointment", new
                {
                    AppointmentId = bookingId,
                    Execute = "Cancel",
                    SendEmail = false,
                }, ct);
            }
            catch (MindbodyApiException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                // already gone
            }
            catch (MindbodyApiException ex)
            {
                throw Reject(ex, "The booking could not be cancelled.");
            }

            _cache.Set(CancelKey("appointment", bookingId, email), true, CancelledTtl);
            return new StudioCancelDto { BookingId = bookingId };
        }
        finally
        {
            gate.Release();
        }
    }

    private StudioCancelDto AlreadyCancelledOrThrow(string kind, long bookingId, string email)
    {
        // A repeated cancel of this shopper's own booking is a success; anything else is not theirs.
        if (_cache.TryGetValue(CancelKey(kind, bookingId, email), out _))
        {
            return new StudioCancelDto { BookingId = bookingId };
        }

        throw new StudioException(404, $"You have no {kind} booking {bookingId}.");
    }

    // ---------------------------------------------------------------- 5. trainers

    public async Task<List<StudioTrainerDto>> ListTrainersAsync(CancellationToken ct)
    {
        List<JsonElement> staff;
        try
        {
            staff = await _mb.GetAllAsync("staff/staff", "StaffMembers", authenticated: true, ct: ct);
        }
        catch (MindbodyApiException ex)
        {
            throw Reject(ex, "The trainers could not be listed.");
        }

        return staff
            .Where(s => Bool(s, "ClassTeacher") || Bool(s, "AppointmentInstructor"))
            .Select(s => new StudioTrainerDto
            {
                Id = Long(s, "Id") ?? 0,
                FirstName = Str(s, "FirstName"),
                LastName = Str(s, "LastName"),
                DisplayName = Str(s, "DisplayName") ?? Str(s, "Name"),
                Active = Bool(s, "Active"),
                ClassTeacher = Bool(s, "ClassTeacher"),
                AppointmentInstructor = Bool(s, "AppointmentInstructor"),
                Email = Str(s, "Email"),
                MobilePhone = Str(s, "MobilePhone"),
                HomePhone = Str(s, "HomePhone"),
                WorkPhone = Str(s, "WorkPhone"),
                Address = Str(s, "Address"),
                City = Str(s, "City"),
                State = Str(s, "State"),
                PostalCode = Str(s, "PostalCode"),
                Country = Str(s, "Country"),
                Bio = Str(s, "Bio"),
                ImageUrl = Str(s, "ImageUrl"),
            })
            .OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Id)
            .ToList();
    }
}
