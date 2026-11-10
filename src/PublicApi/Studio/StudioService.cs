using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// The studio features, implemented on top of the Mindbody API.
/// </summary>
public class StudioService
{
    private const int MaxRangeDays = 366;
    private static readonly Regex HasOffset = new(@"\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}(:?\d{2})?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] InactiveVisitStatuses = { "Cancelled", "LateCancelled", "NoShow" };
    private static readonly KeyedLocks Locks = new();

    private readonly MindbodyClient _mindbody;
    private readonly IMemoryCache _cache;

    public StudioService(MindbodyClient mindbody, IMemoryCache cache)
    {
        _mindbody = mindbody;
        _cache = cache;
    }

    // ------------------------------------------------------------------ site data

    private Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken ct) =>
        CachedAsync("studio:tz", TimeSpan.FromHours(6), async () =>
        {
            var sites = await _mindbody.GetAsync("site/sites", null, ct);
            var name = (sites["Sites"] as JsonArray)?.FirstOrDefault()?["TimeZone"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new StudioException(503, "The studio's time zone is not available.");
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(name);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw new StudioException(503, $"The studio's time zone '{name}' is not known to this server.", ex);
            }
        });

    private Task<List<JsonNode>> GetLocationsAsync(CancellationToken ct) =>
        CachedAsync("studio:locations", TimeSpan.FromMinutes(10),
            () => _mindbody.GetAllPagesAsync("site/locations", "Locations", null, ct));

    private Task<List<string>> GetReferralTypesAsync(CancellationToken ct) =>
        CachedAsync("studio:referrals", TimeSpan.FromMinutes(10), async () =>
        {
            var result = await _mindbody.GetAsync("client/clientreferraltypes", null, ct);
            return (result["ReferralTypes"] as JsonArray)?.Select(n => n?.GetValue<string>()).OfType<string>().ToList()
                   ?? new List<string>();
        });

    private Task<List<JsonNode>> GetSessionTypesAsync(CancellationToken ct) =>
        CachedAsync("studio:sessiontypes", TimeSpan.FromMinutes(10),
            () => _mindbody.GetAllPagesAsync("site/sessiontypes", "SessionTypes", null, ct));

    private async Task<T> CachedAsync<T>(string key, TimeSpan ttl, Func<Task<T>> load)
    {
        if (_cache.TryGetValue(key, out T? value) && value != null)
        {
            return value;
        }

        value = await load();
        _cache.Set(key, value, ttl);
        return value;
    }

    // ------------------------------------------------------------------ time helpers

    private static DateTimeOffset ToOffset(string local, TimeZoneInfo tz)
    {
        var dt = DateTime.Parse(local, CultureInfo.InvariantCulture, DateTimeStyles.None);
        dt = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(dt))
        {
            dt = dt.AddHours(1);
        }

        return new DateTimeOffset(dt, tz.GetUtcOffset(dt));
    }

    private static DateTimeOffset ToOffset(DateTime local, TimeZoneInfo tz)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }

    private static DateOnly Today(TimeZoneInfo tz) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);

    private static string LocalText(DateTime local) => local.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    private static DateOnly? ParseDate(string? text, string name, TimeZoneInfo tz)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim();
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        // A full ISO-8601 date-time is accepted too; its calendar day in the studio's time zone is used.
        if (text.Contains('T') && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto) &&
            HasOffset.IsMatch(text))
        {
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(dto, tz).DateTime);
        }

        if (text.Contains('T') && DateOnly.TryParse(text[..text.IndexOf('T')], CultureInfo.InvariantCulture, DateTimeStyles.None, out var datePart))
        {
            return datePart;
        }

        throw StudioException.BadRequest($"'{name}' must be a date in the form yyyy-MM-dd.");
    }

    private static (DateOnly From, DateOnly To) ResolveRange(string? fromText, string? toText, TimeZoneInfo tz, bool defaultRange)
    {
        var from = ParseDate(fromText, "from", tz);
        var to = ParseDate(toText, "to", tz);
        if (defaultRange)
        {
            from ??= Today(tz);
            to ??= from.Value.AddDays(7);
        }
        else if (from == null || to == null)
        {
            throw StudioException.BadRequest("Both 'from' and 'to' dates are required.");
        }

        if (to < from)
        {
            throw StudioException.BadRequest("'to' must not be before 'from'.");
        }

        if (to.Value.DayNumber - from.Value.DayNumber > MaxRangeDays)
        {
            throw StudioException.BadRequest($"The range may span at most {MaxRangeDays} days.");
        }

        return (from!.Value, to!.Value);
    }

    // ------------------------------------------------------------------ 1. classes

    public async Task<List<StudioClassDto>> ListClassesAsync(string? fromText, string? toText, CancellationToken ct)
    {
        var tz = await GetTimeZoneAsync(ct);
        var (from, to) = ResolveRange(fromText, toText, tz, defaultRange: true);
        var now = DateTimeOffset.UtcNow;
        var classes = await FetchClassesAsync(from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"), null, ct);

        return classes
            .Where(c => IsAttendable(c, tz, now, out _))
            .Select(c => ToClassDto(c, tz))
            .OrderBy(c => c.StartTime)
            .ThenBy(c => c.ClassId)
            .ToList();
    }

    private async Task<List<JsonNode>> FetchClassesAsync(string from, string to, long? classId, CancellationToken ct)
    {
        var query = new List<KeyValuePair<string, string>> { new("StartDateTime", from), new("EndDateTime", to) };
        if (classId != null)
        {
            query.Add(new("ClassIds", classId.Value.ToString(CultureInfo.InvariantCulture)));
        }

        return await _mindbody.GetAllPagesAsync("class/classes", "Classes", query, ct);
    }

    private async Task<JsonNode> GetClassAsync(long classId, CancellationToken ct)
    {
        // Mindbody only returns a class when its date falls inside the requested window.
        var tz = await GetTimeZoneAsync(ct);
        var today = Today(tz);
        var found = await FetchClassesAsync(today.AddYears(-2).ToString("yyyy-MM-dd"), today.AddYears(5).ToString("yyyy-MM-dd"), classId, ct);
        return found.FirstOrDefault(c => c["Id"]?.GetValue<long>() == classId)
               ?? throw StudioException.NotFound($"Class {classId} does not exist.");
    }

    private static bool IsAttendable(JsonNode c, TimeZoneInfo tz, DateTimeOffset now, out string reason)
    {
        reason = "";
        if (c["IsCanceled"]?.GetValue<bool>() == true)
        {
            reason = "The class has been cancelled.";
            return false;
        }

        if (c["Active"]?.GetValue<bool>() == false || c["IsAvailable"]?.GetValue<bool>() == false)
        {
            reason = "The class is not available.";
            return false;
        }

        var webCapacity = c["WebCapacity"]?.GetValue<int>() ?? 0;
        if (webCapacity <= 0)
        {
            reason = "The class cannot be booked online.";
            return false;
        }

        var webBooked = c["WebBooked"]?.GetValue<int>() ?? 0;
        var maxCapacity = c["MaxCapacity"]?.GetValue<int>() ?? int.MaxValue;
        var totalBooked = c["TotalBooked"]?.GetValue<int>() ?? 0;
        if (webBooked >= webCapacity || totalBooked >= maxCapacity)
        {
            reason = "The class is full.";
            return false;
        }

        if (ToOffset(c["EndDateTime"]!.GetValue<string>(), tz) <= now)
        {
            reason = "The class has already finished.";
            return false;
        }

        return true;
    }

    private static StudioClassDto ToClassDto(JsonNode c, TimeZoneInfo tz) => new()
    {
        ClassId = c["Id"]!.GetValue<long>(),
        Name = c["ClassDescription"]?["Name"]?.GetValue<string>()?.Trim() ?? "",
        StartTime = ToOffset(c["StartDateTime"]!.GetValue<string>(), tz),
        EndTime = ToOffset(c["EndDateTime"]!.GetValue<string>(), tz),
        LocationId = c["Location"]?["Id"]?.GetValue<int>(),
        LocationName = c["Location"]?["Name"]?.GetValue<string>(),
        Trainer = c["Staff"]?["DisplayName"]?.GetValue<string>() ?? c["Staff"]?["Name"]?.GetValue<string>()
    };

    // ------------------------------------------------------------------ 2. session times

    private async Task<JsonNode> RequireAppointmentTypeAsync(int sessionTypeId, CancellationToken ct)
    {
        var types = await GetSessionTypesAsync(ct);
        var type = types.FirstOrDefault(t => t["Id"]?.GetValue<int>() == sessionTypeId)
                   ?? throw StudioException.NotFound($"Session type {sessionTypeId} does not exist.");
        if (!string.Equals(type["Type"]?.GetValue<string>(), "Appointment", StringComparison.OrdinalIgnoreCase))
        {
            throw StudioException.BadRequest($"Session type {sessionTypeId} is not a one-to-one session type.");
        }

        return type;
    }

    private async Task<List<(JsonNode Item, DateTime Start, DateTime End, int Length)>> GetAvailabilityWindowsAsync(
        JsonNode sessionType, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var id = sessionType["Id"]!.GetValue<int>();
        var query = new List<KeyValuePair<string, string>>
        {
            new("SessionTypeIds", id.ToString(CultureInfo.InvariantCulture)),
            new("StartDate", from.ToString("yyyy-MM-dd")),
            new("EndDate", to.ToString("yyyy-MM-dd"))
        };
        var items = await _mindbody.GetAllPagesAsync("appointment/bookableitems", "Availabilities", query, ct);
        var windows = new List<(JsonNode, DateTime, DateTime, int)>();
        foreach (var item in items)
        {
            if (item["ShowPublic"]?.GetValue<bool>() == false || item["IsMasked"]?.GetValue<bool>() == true)
            {
                continue;
            }

            var length = item["SessionType"]?["StaffTimeLength"]?.GetValue<int>()
                         ?? item["SessionType"]?["DefaultTimeLength"]?.GetValue<int>()
                         ?? sessionType["StaffTimeLength"]?.GetValue<int>()
                         ?? sessionType["DefaultTimeLength"]?.GetValue<int>()
                         ?? 60;
            if (length <= 0)
            {
                continue;
            }

            var start = DateTime.Parse(item["StartDateTime"]!.GetValue<string>(), CultureInfo.InvariantCulture);
            var end = DateTime.Parse(item["EndDateTime"]!.GetValue<string>(), CultureInfo.InvariantCulture);
            windows.Add((item, start, end, length));
        }

        return windows;
    }

    public async Task<List<StudioSlotDto>> ListSlotsAsync(int sessionTypeId, string? fromText, string? toText, CancellationToken ct)
    {
        if (sessionTypeId <= 0)
        {
            throw StudioException.BadRequest("'sessionTypeId' is required.");
        }

        var tz = await GetTimeZoneAsync(ct);
        var (from, to) = ResolveRange(fromText, toText, tz, defaultRange: false);
        var type = await RequireAppointmentTypeAsync(sessionTypeId, ct);
        var length = type["DefaultTimeLength"]?.GetValue<int>() ?? type["StaffTimeLength"]?.GetValue<int>() ?? 60;
        if (length <= 0)
        {
            length = 60;
        }

        // The studio's times of day at which this session type can start, whether or not a trainer is free.
        var timesOfDay = await CachedAsync($"studio:activetimes:{sessionTypeId}", TimeSpan.FromMinutes(10), async () =>
        {
            var query = new List<KeyValuePair<string, string>>
            {
                new("ScheduleType", "Appointment"),
                new("SessionTypeId", sessionTypeId.ToString(CultureInfo.InvariantCulture))
            };
            var items = await _mindbody.GetAllPagesAsync("appointment/activesessiontimes", "ActiveSessionTimes", query, ct);
            return items
                .Select(i => i.GetValue<string>())
                .Select(t => DateTime.Parse(t, CultureInfo.InvariantCulture).TimeOfDay)
                .Distinct()
                .OrderBy(t => t)
                .ToList();
        });

        var now = DateTimeOffset.UtcNow;
        var slots = new List<StudioSlotDto>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            foreach (var time in timesOfDay)
            {
                var start = day.ToDateTime(TimeOnly.FromTimeSpan(time));
                var startTime = ToOffset(start, tz);
                if (startTime <= now)
                {
                    continue;
                }

                slots.Add(new StudioSlotDto
                {
                    SessionTypeId = sessionTypeId,
                    StartTime = startTime,
                    EndTime = ToOffset(start.AddMinutes(length), tz),
                    DurationMinutes = length
                });
            }
        }

        return slots.OrderBy(sl => sl.StartTime).ToList();
    }

    // ------------------------------------------------------------------ clients

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private async Task<JsonNode?> FindClientByEmailAsync(string email, CancellationToken ct)
    {
        var query = new List<KeyValuePair<string, string>> { new("SearchText", email), new("limit", "200") };
        var result = await _mindbody.GetAsync("client/clients", query, ct);
        return (result["Clients"] as JsonArray)?
            .OfType<JsonNode>()
            .Where(c => string.Equals(c["Email"]?.GetValue<string>()?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c["Active"]?.GetValue<bool>() ?? true)
            .FirstOrDefault();
    }

    private async Task<JsonNode?> FindClientByIdAsync(string id, CancellationToken ct)
    {
        var result = await _mindbody.GetAsync("client/clients", new[] { new KeyValuePair<string, string>("ClientIds", id) }, ct);
        return (result["Clients"] as JsonArray)?.OfType<JsonNode>().FirstOrDefault();
    }

    private async Task<string> CreateClientAsync(string email, NewClientDetails d, CancellationToken ct)
    {
        var missing = new List<string>();
        void Need(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) missing.Add(name);
        }

        if (d.HomeLocationId == null) missing.Add("homeLocationId");
        Need(d.StreetAddress, "streetAddress");
        Need(d.City, "city");
        Need(d.State, "state");
        Need(d.PostalCode, "postalCode");
        Need(d.DateOfBirth, "dateOfBirth");
        Need(d.MobilePhone, "mobilePhone");
        Need(d.ReferredBy, "referredBy");
        if (missing.Count > 0)
        {
            throw StudioException.BadRequest(
                "This is the shopper's first booking, so the studio needs their details. Missing: " + string.Join(", ", missing) + ".");
        }

        var locations = await GetLocationsAsync(ct);
        if (!locations.Any(l => l["Id"]?.GetValue<int>() == d.HomeLocationId))
        {
            throw StudioException.BadRequest(
                $"Location {d.HomeLocationId} does not exist. Valid locations: " +
                string.Join(", ", locations.Select(l => $"{l["Id"]} ({l["Name"]})")) + ".");
        }

        if (!DateOnly.TryParse(d.DateOfBirth!.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob) ||
            dob >= DateOnly.FromDateTime(DateTime.UtcNow) || dob < DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-120))
        {
            throw StudioException.BadRequest("'dateOfBirth' must be a valid date in the past (yyyy-MM-dd).");
        }

        var digits = new string(d.MobilePhone!.Where(char.IsDigit).ToArray());
        if (digits.Length < 7 || digits.Length > 15)
        {
            throw StudioException.BadRequest("'mobilePhone' is not a valid phone number.");
        }

        var referrals = await GetReferralTypesAsync(ct);
        var referral = referrals.FirstOrDefault(r => string.Equals(r, d.ReferredBy!.Trim(), StringComparison.OrdinalIgnoreCase));
        if (referral == null)
        {
            throw StudioException.BadRequest("'referredBy' must be one of: " + string.Join(", ", referrals) + ".");
        }

        var (first, last) = DeriveNames(email, d);
        var body = new JsonObject
        {
            ["FirstName"] = first,
            ["LastName"] = last,
            ["Email"] = email,
            ["BirthDate"] = dob.ToString("yyyy-MM-dd"),
            ["AddressLine1"] = d.StreetAddress!.Trim(),
            ["City"] = d.City!.Trim(),
            ["State"] = d.State!.Trim(),
            ["PostalCode"] = d.PostalCode!.Trim(),
            ["MobilePhone"] = d.MobilePhone!.Trim(),
            ["ReferredBy"] = referral,
            ["HomeLocation"] = new JsonObject { ["Id"] = d.HomeLocationId }
        };

        try
        {
            var created = await _mindbody.PostAsync("client/addclient", body, ct);
            return created["Client"]!["Id"]!.GetValue<string>();
        }
        catch (MindbodyException ex) when (ex.Code == "InvalidClientCreation")
        {
            // Created in the meantime (for example by an earlier attempt whose answer was lost).
            var existing = await FindClientByEmailAsync(email, ct);
            if (existing != null)
            {
                return existing["Id"]!.GetValue<string>();
            }

            throw;
        }
    }

    private static (string First, string Last) DeriveNames(string email, NewClientDetails d)
    {
        static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
        var local = email.Split('@')[0];
        var parts = Regex.Split(local, @"[._\-+\s]+").Where(p => p.Length > 0).Select(Cap).ToList();
        var first = !string.IsNullOrWhiteSpace(d.FirstName) ? d.FirstName.Trim() : parts.FirstOrDefault() ?? "eShop";
        var last = !string.IsNullOrWhiteSpace(d.LastName)
            ? d.LastName.Trim()
            : parts.Count > 1 ? string.Join(' ', parts.Skip(1)) : "Shopper";
        return (first, last);
    }

    // ------------------------------------------------------------------ visits

    private sealed record Visit(long Id, long ClassId, long AppointmentId, string Status, DateTime Start, DateTime End, long? StaffId, int? LocationId, string Name);

    private async Task<List<Visit>> GetVisitsAsync(string clientId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            new("ClientId", clientId),
            new("StartDate", from.ToString("yyyy-MM-dd")),
            new("EndDate", to.ToString("yyyy-MM-dd"))
        };
        var items = await _mindbody.GetAllPagesAsync("client/clientvisits", "Visits", query, ct);
        return items
            .Select(v => new Visit(
                v["Id"]?.GetValue<long>() ?? 0,
                v["ClassId"]?.GetValue<long>() ?? 0,
                v["AppointmentId"]?.GetValue<long>() ?? 0,
                (v["LateCancelled"]?.GetValue<bool>() == true ? "LateCancelled" : v["AppointmentStatus"]?.GetValue<string>()) ?? "",
                DateTime.Parse(v["StartDateTime"]!.GetValue<string>(), CultureInfo.InvariantCulture),
                DateTime.Parse(v["EndDateTime"]!.GetValue<string>(), CultureInfo.InvariantCulture),
                v["StaffId"]?.GetValue<long>(),
                v["LocationId"]?.GetValue<int>(),
                v["Name"]?.GetValue<string>() ?? ""))
            .Where(v => !InactiveVisitStatuses.Contains(v.Status, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static readonly DateOnly WideFrom = new(2000, 1, 1);
    private static readonly DateOnly WideTo = new(2100, 1, 1);

    // ------------------------------------------------------------------ 3. book

    public async Task<(StudioBookingDto Booking, bool Created)> BookClassAsync(string email, ClassBookingRequest request, CancellationToken ct)
    {
        if (request.ClassId <= 0)
        {
            throw StudioException.BadRequest("'classId' is required.");
        }

        email = Normalize(email);
        using var _ = await Locks.AcquireAsync(email, ct);

        var tz = await GetTimeZoneAsync(ct);
        var cls = await GetClassAsync(request.ClassId, ct);
        var dto = ToClassDto(cls, tz);
        var client = await FindClientByEmailAsync(email, ct);

        if (client != null)
        {
            var existing = await FindClassVisitAsync(client["Id"]!.GetValue<string>(), request.ClassId, dto, ct);
            if (existing != null)
            {
                return (ClassBooking(existing.Value, dto, already: true), false);
            }
        }

        if (!IsAttendable(cls, tz, DateTimeOffset.UtcNow, out var reason))
        {
            throw StudioException.Conflict(reason);
        }

        var clientId = client != null ? client["Id"]!.GetValue<string>() : await CreateClientAsync(email, request, ct);
        var booked = await AddToClassAsync(clientId, request.ClassId, ct);
        return (ClassBooking(booked, dto, already: false), true);
    }

    public async Task<(StudioBookingDto Booking, bool Created)> BookClassForShopperAsync(AdminClassBookingRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Shopper))
        {
            throw StudioException.BadRequest("'shopper' is required.");
        }

        if (request.ClassId <= 0)
        {
            throw StudioException.BadRequest("'classId' is required.");
        }

        var shopper = request.Shopper.Trim();
        var byId = shopper.All(char.IsDigit);
        var key = byId ? "id:" + shopper : Normalize(shopper);
        using var _ = await Locks.AcquireAsync(key, ct);

        var tz = await GetTimeZoneAsync(ct);
        var client = byId ? await FindClientByIdAsync(shopper, ct) : await FindClientByEmailAsync(Normalize(shopper), ct);
        if (client == null)
        {
            throw StudioException.NotFound(
                $"'{shopper}' is not a client of the studio yet; a shopper becomes a client with their own first booking.");
        }

        var cls = await GetClassAsync(request.ClassId, ct);
        var dto = ToClassDto(cls, tz);
        var clientId = client["Id"]!.GetValue<string>();

        var existing = await FindClassVisitAsync(clientId, request.ClassId, dto, ct);
        if (existing != null)
        {
            return (ClassBooking(existing.Value, dto, already: true), false);
        }

        // Front-desk operators may place a shopper in any class of the timetable that can still take place.
        if (cls["IsCanceled"]?.GetValue<bool>() == true)
        {
            throw StudioException.Conflict("The class has been cancelled.");
        }

        if (dto.EndTime <= DateTimeOffset.UtcNow)
        {
            throw StudioException.Conflict("The class has already finished.");
        }

        var booked = await AddToClassAsync(clientId, request.ClassId, ct);
        return (ClassBooking(booked, dto, already: false), true);
    }

    private async Task<long> AddToClassAsync(string clientId, long classId, CancellationToken ct)
    {
        var body = new JsonObject { ["ClientId"] = clientId, ["ClassId"] = classId };
        var result = await _mindbody.PostAsync("class/addclienttoclass", body, ct);
        return result["Visit"]?["Id"]?.GetValue<long>()
               ?? throw new StudioException(502, "The studio system did not confirm the booking.");
    }

    private async Task<long?> FindClassVisitAsync(string clientId, long classId, StudioClassDto cls, CancellationToken ct)
    {
        var day = DateOnly.FromDateTime(cls.StartTime.DateTime);
        var visits = await GetVisitsAsync(clientId, day.AddDays(-1), day.AddDays(1), ct);
        var visit = visits.FirstOrDefault(v => v.ClassId == classId);
        return visit?.Id;
    }

    private static StudioBookingDto ClassBooking(long visitId, StudioClassDto cls, bool already) => new()
    {
        BookingId = visitId,
        Type = "class",
        Name = cls.Name,
        ClassId = cls.ClassId,
        StartTime = cls.StartTime,
        EndTime = cls.EndTime,
        LocationId = cls.LocationId,
        AlreadyBooked = already
    };

    public async Task<(StudioBookingDto Booking, bool Created)> BookAppointmentAsync(string email, AppointmentBookingRequest request, CancellationToken ct)
    {
        if (request.SessionTypeId <= 0)
        {
            throw StudioException.BadRequest("'sessionTypeId' is required.");
        }

        if (string.IsNullOrWhiteSpace(request.StartTime) || !HasOffset.IsMatch(request.StartTime.Trim()) ||
            !DateTimeOffset.TryParse(request.StartTime.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var requested))
        {
            throw StudioException.BadRequest("'startTime' must be an ISO-8601 date-time with a UTC offset, e.g. 2026-11-12T11:00:00-08:00.");
        }

        email = Normalize(email);
        using var _ = await Locks.AcquireAsync(email, ct);

        var tz = await GetTimeZoneAsync(ct);
        var type = await RequireAppointmentTypeAsync(request.SessionTypeId, ct);
        var localStart = TimeZoneInfo.ConvertTime(requested, tz).DateTime;
        localStart = new DateTime(localStart.Year, localStart.Month, localStart.Day, localStart.Hour, localStart.Minute, localStart.Second);
        var client = await FindClientByEmailAsync(email, ct);

        if (client != null)
        {
            var visits = await GetVisitsAsync(client["Id"]!.GetValue<string>(), DateOnly.FromDateTime(localStart).AddDays(-1), DateOnly.FromDateTime(localStart).AddDays(1), ct);
            var existing = visits.FirstOrDefault(v => v.AppointmentId > 0 && v.Start == localStart);
            if (existing != null)
            {
                return (new StudioBookingDto
                {
                    BookingId = existing.AppointmentId,
                    Type = "appointment",
                    Name = type["Name"]?.GetValue<string>() ?? existing.Name,
                    SessionTypeId = request.SessionTypeId,
                    StartTime = ToOffset(existing.Start, tz),
                    EndTime = ToOffset(existing.End, tz),
                    LocationId = existing.LocationId,
                    StaffId = existing.StaffId,
                    AlreadyBooked = true
                }, false);
            }
        }

        if (ToOffset(localStart, tz) <= DateTimeOffset.UtcNow)
        {
            throw StudioException.Conflict("That time has already passed.");
        }

        var day = DateOnly.FromDateTime(localStart);
        var windows = await GetAvailabilityWindowsAsync(type, day, day, ct);
        var candidates = windows
            .Where(w => localStart >= w.Start && localStart.AddMinutes(w.Length) <= w.End)
            .Where(w => request.StaffId == null || w.Item["Staff"]?["Id"]?.GetValue<long>() == request.StaffId)
            .Where(w => request.LocationId == null || w.Item["Location"]?["Id"]?.GetValue<int>() == request.LocationId)
            .ToList();
        if (candidates.Count == 0)
        {
            throw StudioException.Conflict("No trainer is free for that session type at that time.");
        }

        var clientId = client != null ? client["Id"]!.GetValue<string>() : await CreateClientAsync(email, request, ct);
        var homeLocation = client?["HomeLocation"]?["Id"]?.GetValue<int>() ?? request.HomeLocationId;
        var ordered = candidates
            .OrderBy(w => w.Item["Location"]?["Id"]?.GetValue<int>() == homeLocation ? 0 : 1)
            .ToList();

        MindbodyException? last = null;
        foreach (var w in ordered)
        {
            var staffId = w.Item["Staff"]!["Id"]!.GetValue<long>();
            var locationId = w.Item["Location"]!["Id"]!.GetValue<int>();
            var body = new JsonObject
            {
                ["ClientId"] = clientId,
                ["LocationId"] = locationId,
                ["SessionTypeId"] = request.SessionTypeId,
                ["StaffId"] = staffId,
                ["StartDateTime"] = LocalText(localStart)
            };
            try
            {
                var result = await _mindbody.PostAsync("appointment/addappointment", body, ct);
                var id = result["Appointment"]?["Id"]?.GetValue<long>()
                         ?? throw new StudioException(502, "The studio system did not confirm the booking.");
                return (new StudioBookingDto
                {
                    BookingId = id,
                    Type = "appointment",
                    Name = type["Name"]?.GetValue<string>() ?? "",
                    SessionTypeId = request.SessionTypeId,
                    StartTime = ToOffset(localStart, tz),
                    EndTime = ToOffset(localStart.AddMinutes(w.Length), tz),
                    LocationId = locationId,
                    StaffId = staffId
                }, true);
            }
            catch (MindbodyException ex) when (ex.Code == "InvalidBookingTime")
            {
                last = ex;
            }
        }

        throw (StudioException?)last ?? StudioException.Conflict("That time is no longer available.");
    }

    // ------------------------------------------------------------------ 4. cancel

    private static string CancelledKey(string kind, string clientId, long id) => $"studio:cancelled:{kind}:{clientId}:{id}";

    public async Task<StudioCancellationDto> CancelClassBookingAsync(string email, long bookingId, CancellationToken ct)
    {
        email = Normalize(email);
        using var _ = await Locks.AcquireAsync(email, ct);

        var client = await FindClientByEmailAsync(email, ct)
                     ?? throw StudioException.NotFound("You have no bookings with the studio.");
        var clientId = client["Id"]!.GetValue<string>();
        var visits = await GetVisitsAsync(clientId, WideFrom, WideTo, ct);
        var visit = visits.FirstOrDefault(v => v.Id == bookingId && v.ClassId > 0);
        if (visit == null)
        {
            return AlreadyCancelledOrNotFound("class", clientId, bookingId);
        }

        // A client may hold several places in one class if earlier attempts raced; one call removes them all.
        var body = new JsonObject { ["ClientId"] = clientId, ["ClassId"] = visit.ClassId, ["LateCancel"] = false };
        try
        {
            await _mindbody.PostAsync("class/removeclientfromclass", body, ct);
        }
        catch (MindbodyException ex)
        {
            throw StudioException.Conflict(ex.Message);
        }

        foreach (var v in visits.Where(v => v.ClassId == visit.ClassId))
        {
            _cache.Set(CancelledKey("class", clientId, v.Id), true, TimeSpan.FromDays(30));
        }

        return new StudioCancellationDto { BookingId = bookingId };
    }

    public async Task<StudioCancellationDto> CancelAppointmentBookingAsync(string email, long bookingId, CancellationToken ct)
    {
        email = Normalize(email);
        using var _ = await Locks.AcquireAsync(email, ct);

        var client = await FindClientByEmailAsync(email, ct)
                     ?? throw StudioException.NotFound("You have no bookings with the studio.");
        var clientId = client["Id"]!.GetValue<string>();
        var visits = await GetVisitsAsync(clientId, WideFrom, WideTo, ct);
        var visit = visits.FirstOrDefault(v => v.AppointmentId == bookingId);
        if (visit == null)
        {
            return AlreadyCancelledOrNotFound("appointment", clientId, bookingId);
        }

        var body = new JsonObject { ["AppointmentId"] = bookingId, ["Execute"] = "Cancel" };
        try
        {
            await _mindbody.PostAsync("appointment/updateappointment", body, ct);
        }
        catch (MindbodyException ex)
        {
            throw StudioException.Conflict(ex.Message);
        }

        _cache.Set(CancelledKey("appointment", clientId, bookingId), true, TimeSpan.FromDays(30));
        return new StudioCancellationDto { BookingId = bookingId };
    }

    // A repeated cancel (the app retries when an answer is lost) must also succeed; anything else that is
    // not the caller's own booking is reported as not found.
    private StudioCancellationDto AlreadyCancelledOrNotFound(string kind, string clientId, long bookingId)
    {
        if (_cache.TryGetValue(CancelledKey(kind, clientId, bookingId), out _))
        {
            return new StudioCancellationDto { BookingId = bookingId };
        }

        throw StudioException.NotFound($"You have no {kind} booking {bookingId}.");
    }

    // ------------------------------------------------------------------ 5. trainers

    public async Task<List<StudioTrainerDto>> ListTrainersAsync(CancellationToken ct)
    {
        var staff = await _mindbody.GetAllPagesAsync("staff/staff", "StaffMembers", null, ct);
        return staff
            .Where(s => s["Id"]?.GetValue<long>() > 0)
            .Where(s => s["ClassTeacher"]?.GetValue<bool>() == true || s["AppointmentInstructor"]?.GetValue<bool>() == true)
            .Select(s => new StudioTrainerDto
            {
                Id = s["Id"]!.GetValue<long>(),
                FirstName = s["FirstName"]?.GetValue<string>(),
                LastName = s["LastName"]?.GetValue<string>(),
                DisplayName = s["DisplayName"]?.GetValue<string>(),
                Active = s["Active"]?.GetValue<bool>() ?? false,
                ClassTeacher = s["ClassTeacher"]?.GetValue<bool>() ?? false,
                AppointmentInstructor = s["AppointmentInstructor"]?.GetValue<bool>() ?? false,
                Email = s["Email"]?.GetValue<string>(),
                MobilePhone = s["MobilePhone"]?.GetValue<string>(),
                HomePhone = s["HomePhone"]?.GetValue<string>(),
                WorkPhone = s["WorkPhone"]?.GetValue<string>(),
                Address = s["Address"]?.GetValue<string>(),
                City = s["City"]?.GetValue<string>(),
                State = s["State"]?.GetValue<string>(),
                PostalCode = s["PostalCode"]?.GetValue<string>(),
                Country = s["Country"]?.GetValue<string>()
            })
            .OrderBy(t => t.LastName).ThenBy(t => t.FirstName).ThenBy(t => t.Id)
            .ToList();
    }

    // ------------------------------------------------------------------ locking

    /// <summary>
    /// Serialises work per key (a shopper) so a request sent again while the first attempt is still running
    /// cannot create the client or the booking twice. Striped, so memory stays bounded.
    /// </summary>
    private sealed class KeyedLocks
    {
        private readonly SemaphoreSlim[] _stripes = Enumerable.Range(0, 128).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

        public async Task<IDisposable> AcquireAsync(string key, CancellationToken ct)
        {
            var stripe = _stripes[(uint)StringComparer.Ordinal.GetHashCode(key) % (uint)_stripes.Length];
            await stripe.WaitAsync(ct);
            return new Releaser(stripe);
        }

        private sealed class Releaser : IDisposable
        {
            private SemaphoreSlim? _semaphore;
            public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
            public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
        }
    }
}
