using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Mindbody;

/// <summary>
/// Studio features on top of Mindbody. Registered as a singleton: it holds the (refreshable)
/// site time zone and the per-shopper locks that make retried requests safe.
/// </summary>
public class StudioService
{
    public const int DEFAULT_DAYS_AHEAD = 7;
    public const int MAX_RANGE_DAYS = 366;
    private static readonly TimeSpan ZoneCacheLifetime = TimeSpan.FromHours(6);
    private static readonly string[] CancelledStatuses = { "Cancelled", "Canceled", "LateCancelled", "LateCanceled" };

    private readonly MindbodyClient _mindbody;
    private readonly KeyedLock _locks = new();
    private readonly SemaphoreSlim _zoneLock = new(1, 1);
    private TimeZoneInfo? _zone;
    private DateTimeOffset _zoneFetchedAt;

    public StudioService(MindbodyClient mindbody)
    {
        _mindbody = mindbody;
    }

    // ------------------------------------------------------------------ time helpers

    private async Task<TimeZoneInfo> GetZoneAsync(CancellationToken ct)
    {
        if (_zone is not null && DateTimeOffset.UtcNow - _zoneFetchedAt < ZoneCacheLifetime) return _zone;

        await _zoneLock.WaitAsync(ct);
        try
        {
            if (_zone is not null && DateTimeOffset.UtcNow - _zoneFetchedAt < ZoneCacheLifetime) return _zone;
            try
            {
                var response = await _mindbody.GetAsync("site/sites", null, ct);
                var id = Str(response["Sites"]?.AsArray().FirstOrDefault()?["TimeZone"]);
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new StudioException(502, "The studio service did not report the studio's time zone.");
                }
                _zone = TimeZoneInfo.FindSystemTimeZoneById(id);
                _zoneFetchedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex) when (_zone is not null && ex is not OperationCanceledException)
            {
                // Keep serving with the last known zone; try again soon.
                _zoneFetchedAt = DateTimeOffset.UtcNow - ZoneCacheLifetime + TimeSpan.FromMinutes(5);
            }
            catch (TimeZoneNotFoundException)
            {
                throw new StudioException(502, "The studio's time zone is not known to this server.");
            }
            return _zone!;
        }
        finally
        {
            _zoneLock.Release();
        }
    }

    private static DateTimeOffset ToOffset(TimeZoneInfo zone, DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static DateTime ToLocal(TimeZoneInfo zone, DateTimeOffset instant)
        => DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(instant, zone).DateTime, DateTimeKind.Unspecified);

    private static DateTime ParseLocal(string? value)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new StudioException(502, "The studio service returned a time that could not be read.");
        }
        return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
    }

    private static string LocalIso(DateTime local) => local.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
    private static string DateIso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Str(JsonNode? node) => node is null ? null : node.ToString();
    private static long? Long(JsonNode? node) => long.TryParse(Str(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    private static bool Bool(JsonNode? node, bool fallback = false) => bool.TryParse(Str(node), out var v) ? v : fallback;

    private async Task<(DateOnly From, DateOnly To)> ResolveRangeAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var zone = await GetZoneAsync(ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var start = from ?? today;
        var end = to ?? start.AddDays(DEFAULT_DAYS_AHEAD);
        if (end < start) throw StudioException.BadRequest("'to' must not be before 'from'.");
        if (end.DayNumber - start.DayNumber >= MAX_RANGE_DAYS)
        {
            throw StudioException.BadRequest("The date range may cover at most " + MAX_RANGE_DAYS + " days.");
        }
        return (start, end);
    }

    // ------------------------------------------------------------------ 1. classes

    public async Task<IReadOnlyList<StudioClassDto>> ListClassesAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var (start, end) = await ResolveRangeAsync(from, to, ct);
        var zone = await GetZoneAsync(ct);
        var classes = await _mindbody.GetAllAsync("class/classes", "Classes", new KeyValuePair<string, string?>[]
        {
            new("StartDateTime", DateIso(start) + "T00:00:00"),
            new("EndDateTime", DateIso(end) + "T23:59:59")
        }, ct);

        var now = DateTimeOffset.UtcNow;
        return classes
            .Where(IsAttendableByShoppers)
            .Select(c => ToClassDto(zone, c))
            .Where(c => c.StartTime > now)
            .GroupBy(c => c.Id).Select(g => g.First())
            .OrderBy(c => c.StartTime).ThenBy(c => c.Id)
            .ToList();
    }

    private static bool IsAttendableByShoppers(JsonNode c)
    {
        if (Bool(c["IsCanceled"]) || !Bool(c["Active"], true)) return false;
        // Enrollments are multi-week series, not classes a shopper drops into.
        if (string.Equals(Str(c["ClassDescription"]?["Program"]?["ScheduleType"]), "Enrollment", StringComparison.OrdinalIgnoreCase)) return false;
        var max = Long(c["MaxCapacity"]);
        var booked = Long(c["TotalBooked"]) ?? 0;
        return !(max.HasValue && max.Value > 0 && booked >= max.Value);
    }

    private static StudioClassDto ToClassDto(TimeZoneInfo zone, JsonNode c)
    {
        var description = c["ClassDescription"];
        return new StudioClassDto(
            Long(c["Id"]) ?? 0,
            Str(description?["Name"]) ?? "Class",
            string.IsNullOrWhiteSpace(Str(description?["Description"])) ? null : Str(description?["Description"]),
            ToOffset(zone, ParseLocal(Str(c["StartDateTime"]))),
            ToOffset(zone, ParseLocal(Str(c["EndDateTime"]))),
            (int?)Long(c["Location"]?["Id"]),
            Str(c["Location"]?["Name"]),
            Str(c["Staff"]?["DisplayName"]) ?? Str(c["Staff"]?["Name"]));
    }

    private async Task<JsonNode?> FindClassAsync(long classId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var classes = await _mindbody.GetAllAsync("class/classes", "Classes", new KeyValuePair<string, string?>[]
        {
            new("ClassIds", classId.ToString(CultureInfo.InvariantCulture)),
            new("StartDateTime", DateIso(today.AddDays(-400)) + "T00:00:00"),
            new("EndDateTime", DateIso(today.AddDays(800)) + "T23:59:59")
        }, ct);
        return classes.FirstOrDefault(c => Long(c["Id"]) == classId);
    }

    // ------------------------------------------------------------------ 2. session times

    public async Task<IReadOnlyList<StudioSlotDto>> ListSlotsAsync(long sessionTypeId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (from is null || to is null) throw StudioException.BadRequest("'from' and 'to' dates are required.");
        var (start, end) = await ResolveRangeAsync(from, to, ct);
        var zone = await GetZoneAsync(ct);
        var sessionType = await GetAppointmentTypeAsync(sessionTypeId, ct);
        var minutes = Long(sessionType["DefaultTimeLength"]);
        if (minutes is null or <= 0)
        {
            throw StudioException.Unprocessable("Session type " + sessionTypeId + " has no set length, so its end times cannot be worked out.");
        }

        var times = await _mindbody.GetAllAsync("appointment/activesessiontimes", "ActiveSessionTimes", new KeyValuePair<string, string?>[]
        {
            new("ScheduleType", "Appointment"),
            new("SessionTypeIds", sessionTypeId.ToString(CultureInfo.InvariantCulture))
        }, ct);
        var timesOfDay = times.Select(t => TimeSpan.TryParse(Str(t), CultureInfo.InvariantCulture, out var ts) ? ts : (TimeSpan?)null)
            .Where(t => t.HasValue).Select(t => t!.Value).Distinct().OrderBy(t => t).ToList();

        var now = DateTimeOffset.UtcNow;
        var slots = new List<StudioSlotDto>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            foreach (var time in timesOfDay)
            {
                var local = day.ToDateTime(TimeOnly.MinValue).Add(time);
                if (zone.IsInvalidTime(local)) continue;
                var startTime = ToOffset(zone, local);
                if (startTime <= now) continue;
                var endInstant = startTime.ToUniversalTime().AddMinutes(minutes.Value);
                slots.Add(new StudioSlotDto(startTime, TimeZoneInfo.ConvertTime(endInstant, zone)));
            }
        }
        return slots;
    }

    private async Task<JsonNode> GetAppointmentTypeAsync(long sessionTypeId, CancellationToken ct)
    {
        var types = await _mindbody.GetAllAsync("site/sessiontypes", "SessionTypes", null, ct);
        var type = types.FirstOrDefault(t => Long(t["Id"]) == sessionTypeId);
        if (type is null) throw StudioException.NotFound("Session type " + sessionTypeId + " does not exist.");
        if (!string.Equals(Str(type["Type"]), "Appointment", StringComparison.OrdinalIgnoreCase))
        {
            throw StudioException.BadRequest("Session type " + sessionTypeId + " is not a one-to-one session type.");
        }
        return type;
    }

    // ------------------------------------------------------------------ clients

    private static string LockKey(StudioShopper shopper) => "client:" + shopper.Email.Trim().ToLowerInvariant();

    private async Task<StudioClientRef?> FindClientAsync(StudioShopper shopper, CancellationToken ct)
    {
        var email = shopper.Email.Trim();
        var clients = await _mindbody.GetAllAsync("client/clients", "Clients", new KeyValuePair<string, string?>[]
        {
            new("SearchText", email)
        }, ct);
        var match = clients
            .Where(c => string.Equals(Str(c["Email"])?.Trim(), email, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => Bool(c["Active"], true))
            .ThenBy(c => Long(c["Id"]) ?? long.MaxValue)
            .FirstOrDefault();
        if (match is null) return null;
        return new StudioClientRef(Str(match["Id"])!, (int?)Long(match["HomeLocation"]?["Id"]));
    }

    /// <summary>The shopper's studio client, created once (with the supplied details) if it does not exist.</summary>
    private async Task<StudioClientRef> EnsureClientAsync(StudioShopper shopper, StudioClientDetails? details, CancellationToken ct)
    {
        var existing = await FindClientAsync(shopper, ct);
        if (existing is not null) return existing;

        if (details is null)
        {
            throw StudioException.Unprocessable(
                "This is the first studio booking for this shopper: 'newClient' details (homeLocationId, addressLine1, city, state, postalCode, birthDate, mobilePhone, referredBy) are required.");
        }

        var location = await ValidateDetailsAsync(details, ct);
        var (first, last) = DeriveName(shopper, details);
        var body = new JsonObject
        {
            ["FirstName"] = first,
            ["LastName"] = last,
            ["Email"] = shopper.Email.Trim(),
            ["BirthDate"] = DateIso(details.BirthDate!.Value),
            ["AddressLine1"] = details.AddressLine1!.Trim(),
            ["City"] = details.City!.Trim(),
            ["State"] = details.State!.Trim(),
            ["PostalCode"] = details.PostalCode!.Trim(),
            ["MobilePhone"] = details.MobilePhone!.Trim(),
            ["ReferredBy"] = location.ReferredBy,
            ["HomeLocation"] = new JsonObject { ["Id"] = location.LocationId },
            ["Test"] = false
        };

        try
        {
            var response = await _mindbody.PostAsync("client/addclient", body, ct);
            var id = Str(response["Client"]?["Id"]);
            if (string.IsNullOrEmpty(id)) throw new StudioException(502, "The studio service did not return the new client.");
            return new StudioClientRef(id, location.LocationId);
        }
        catch (MindbodyApiException)
        {
            // A request of ours that timed out may have created the client after all.
            var created = await FindClientAsync(shopper, ct);
            if (created is not null) return created;
            throw;
        }
    }

    private static (string First, string Last) DeriveName(StudioShopper shopper, StudioClientDetails details)
    {
        var first = FirstNonBlank(details.FirstName, shopper.FirstName);
        var last = FirstNonBlank(details.LastName, shopper.LastName);
        if (first is not null && last is not null) return (first, last);

        var local = shopper.Email.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        string Cap(string s) => char.ToUpperInvariant(s[0]) + s[1..];
        first ??= parts.Length > 0 ? Cap(parts[0]) : "Shopper";
        last ??= parts.Length > 1 ? string.Join(' ', parts.Skip(1).Select(Cap)) : "eShopOnWeb";
        return (first, last);
    }

    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private async Task<(int LocationId, string ReferredBy)> ValidateDetailsAsync(StudioClientDetails d, CancellationToken ct)
    {
        var missing = new List<string>();
        if (d.HomeLocationId is null) missing.Add("homeLocationId");
        if (string.IsNullOrWhiteSpace(d.AddressLine1)) missing.Add("addressLine1");
        if (string.IsNullOrWhiteSpace(d.City)) missing.Add("city");
        if (string.IsNullOrWhiteSpace(d.State)) missing.Add("state");
        if (string.IsNullOrWhiteSpace(d.PostalCode)) missing.Add("postalCode");
        if (d.BirthDate is null) missing.Add("birthDate");
        if (string.IsNullOrWhiteSpace(d.MobilePhone)) missing.Add("mobilePhone");
        if (string.IsNullOrWhiteSpace(d.ReferredBy)) missing.Add("referredBy");
        if (missing.Count > 0)
        {
            throw StudioException.BadRequest("Missing new-client details: " + string.Join(", ", missing) + ".");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (d.BirthDate!.Value >= today || d.BirthDate.Value.Year < 1900)
        {
            throw StudioException.BadRequest("birthDate must be a date in the past.");
        }
        var digits = d.MobilePhone!.Count(char.IsDigit);
        if (digits < 7 || digits > 15 || d.MobilePhone!.Any(ch => !(char.IsDigit(ch) || " +-().".Contains(ch))))
        {
            throw StudioException.BadRequest("mobilePhone is not a valid phone number.");
        }

        var locations = await _mindbody.GetAllAsync("site/locations", "Locations", null, ct);
        if (!locations.Any(l => Long(l["Id"]) == d.HomeLocationId))
        {
            throw StudioException.BadRequest("homeLocationId " + d.HomeLocationId + " is not one of the studio's locations.");
        }

        var referral = await _mindbody.GetAsync("client/clientreferraltypes", null, ct);
        var types = (referral["ReferralTypes"]?.AsArray() ?? new JsonArray()).Select(Str).Where(s => s is not null).Select(s => s!).ToList();
        var canonical = types.FirstOrDefault(t => string.Equals(t, d.ReferredBy!.Trim(), StringComparison.OrdinalIgnoreCase));
        if (canonical is null)
        {
            throw StudioException.BadRequest("referredBy must be one of: " + string.Join(", ", types) + ".");
        }
        return (d.HomeLocationId!.Value, canonical);
    }

    // ------------------------------------------------------------------ 3/6. book a class

    /// <param name="staffOverride">True when a front-desk operator books: any class in the timetable may be used.</param>
    public async Task<StudioBookingDto> BookClassAsync(StudioShopper shopper, long classId, StudioClientDetails? details,
        bool staffOverride, CancellationToken ct)
    {
        var zone = await GetZoneAsync(ct);
        var cls = await FindClassAsync(classId, ct) ?? throw StudioException.NotFound("Class " + classId + " does not exist.");
        if (Bool(cls["IsCanceled"])) throw StudioException.Conflict("Class " + classId + " has been cancelled.");
        if (!staffOverride)
        {
            if (!IsAttendableByShoppers(cls)) throw StudioException.Conflict("Class " + classId + " is not open for booking.");
        }
        var start = ToOffset(zone, ParseLocal(Str(cls["StartDateTime"])));
        var end = ToOffset(zone, ParseLocal(Str(cls["EndDateTime"])));
        if (!staffOverride && start <= DateTimeOffset.UtcNow) throw StudioException.Conflict("Class " + classId + " has already started.");
        var locationId = (int?)Long(cls["Location"]?["Id"]);

        using var _ = await _locks.AcquireAsync(LockKey(shopper), ct);
        var client = await EnsureClientAsync(shopper, details, ct);

        var existing = await FindClassVisitAsync(client.Id, classId, ParseLocal(Str(cls["StartDateTime"])), ct);
        if (existing is not null)
        {
            return ClassBooking(classId, Long(existing["Id"])!.Value, client.Id, start, end, locationId, Long(cls["Staff"]?["Id"]), true);
        }

        var body = new JsonObject
        {
            ["ClientId"] = client.Id,
            ["ClassId"] = classId,
            ["RequirePayment"] = false,
            ["Waitlist"] = false,
            ["SendEmail"] = false,
            ["Test"] = false
        };
        JsonNode response;
        try
        {
            response = await _mindbody.PostAsync("class/addclienttoclass", body, ct);
        }
        catch (MindbodyApiException ex)
        {
            throw StudioException.Conflict(ex.Message);
        }
        var visitId = Long(response["Visit"]?["Id"]) ?? throw new StudioException(502, "The studio service did not return the booking.");
        return ClassBooking(classId, visitId, client.Id, start, end, locationId, Long(cls["Staff"]?["Id"]), false);
    }

    private static StudioBookingDto ClassBooking(long classId, long visitId, string clientId, DateTimeOffset start, DateTimeOffset end,
        int? locationId, long? staffId, bool already)
        => new(classId.ToString(CultureInfo.InvariantCulture) + "-" + visitId.ToString(CultureInfo.InvariantCulture),
            "class", classId, null, staffId, locationId, start, end, clientId, already);

    private async Task<JsonNode?> FindClassVisitAsync(string clientId, long classId, DateTime classStartLocal, CancellationToken ct)
    {
        var visits = await _mindbody.GetAllAsync("client/clientvisits", "Visits", new KeyValuePair<string, string?>[]
        {
            new("ClientId", clientId),
            new("StartDate", DateIso(DateOnly.FromDateTime(classStartLocal).AddDays(-1))),
            new("EndDate", DateIso(DateOnly.FromDateTime(classStartLocal).AddDays(1)))
        }, ct);
        return visits.FirstOrDefault(v => Long(v["ClassId"]) == classId && !Bool(v["LateCancelled"])
            && !CancelledStatuses.Contains(Str(v["AppointmentStatus"]) ?? string.Empty, StringComparer.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ 3. book an appointment

    public async Task<StudioBookingDto> BookAppointmentAsync(StudioShopper shopper, long sessionTypeId, DateTimeOffset startTime,
        long? staffId, int? locationId, StudioClientDetails? details, CancellationToken ct)
    {
        var zone = await GetZoneAsync(ct);
        var sessionType = await GetAppointmentTypeAsync(sessionTypeId, ct);
        if (startTime.ToUniversalTime() <= DateTimeOffset.UtcNow) throw StudioException.Conflict("That session time has already passed.");
        var startLocal = ToLocal(zone, startTime);

        using var _ = await _locks.AcquireAsync(LockKey(shopper), ct);
        var client = await EnsureClientAsync(shopper, details, ct);

        // A retried request finds the appointment it already made.
        var day = DateOnly.FromDateTime(startLocal);
        var mine = await _mindbody.GetAllAsync("appointment/staffappointments", "Appointments", new KeyValuePair<string, string?>[]
        {
            new("ClientId", client.Id),
            new("StartDate", DateIso(day)),
            new("EndDate", DateIso(day))
        }, ct);
        var existing = mine.FirstOrDefault(a => Long(a["SessionTypeId"]) == sessionTypeId
            && ParseLocal(Str(a["StartDateTime"])) == startLocal
            && !CancelledStatuses.Contains(Str(a["Status"]) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            && (staffId is null || Long(a["StaffId"]) == staffId));
        if (existing is not null)
        {
            return AppointmentBooking(zone, existing, client.Id, true);
        }

        // Pick a trainer who is free at that time.
        var query = new List<KeyValuePair<string, string?>>
        {
            new("SessionTypeIds", sessionTypeId.ToString(CultureInfo.InvariantCulture)),
            new("StartDate", DateIso(day)),
            new("EndDate", DateIso(day))
        };
        if (staffId.HasValue) query.Add(new("StaffIds", staffId.Value.ToString(CultureInfo.InvariantCulture)));
        if (locationId.HasValue) query.Add(new("LocationIds", locationId.Value.ToString(CultureInfo.InvariantCulture)));
        var windows = await _mindbody.GetAllAsync("appointment/bookableitems", "Availabilities", query, ct);

        var minutes = Long(sessionType["DefaultTimeLength"]) ?? 0;
        var candidates = windows.Where(w =>
        {
            var from = ParseLocal(Str(w["StartDateTime"]));
            var lastStartText = Str(w["BookableEndDateTime"]);
            var lastStart = lastStartText is not null ? ParseLocal(lastStartText) : ParseLocal(Str(w["EndDateTime"])).AddMinutes(-minutes);
            return from <= startLocal && startLocal <= lastStart;
        }).ToList();
        if (candidates.Count == 0)
        {
            throw StudioException.Conflict("No trainer is free for that session at that time.");
        }

        var chosen = candidates
            .OrderBy(w => locationId.HasValue || client.HomeLocationId is null || Long(w["Location"]?["Id"]) != client.HomeLocationId ? 1 : 0)
            .ThenBy(w => Long(w["Staff"]?["Id"]) ?? long.MaxValue)
            .First();
        var chosenStaff = Long(chosen["Staff"]?["Id"])!.Value;
        var chosenLocation = Long(chosen["Location"]?["Id"])!.Value;

        var body = new JsonObject
        {
            ["ClientId"] = client.Id,
            ["SessionTypeId"] = sessionTypeId,
            ["StartDateTime"] = LocalIso(startLocal),
            ["StaffId"] = chosenStaff,
            ["LocationId"] = chosenLocation,
            ["ApplyPayment"] = false,
            ["SendEmail"] = false,
            ["Test"] = false
        };
        JsonNode response;
        try
        {
            response = await _mindbody.PostAsync("appointment/addappointment", body, ct);
        }
        catch (MindbodyApiException ex)
        {
            throw StudioException.Conflict(ex.Message);
        }
        var appointment = response["Appointment"] ?? throw new StudioException(502, "The studio service did not return the booking.");
        return AppointmentBooking(zone, appointment, client.Id, false);
    }

    private static StudioBookingDto AppointmentBooking(TimeZoneInfo zone, JsonNode a, string clientId, bool already)
        => new(Str(a["Id"])!, "appointment", null, Long(a["SessionTypeId"]), Long(a["StaffId"]), (int?)Long(a["LocationId"]),
            ToOffset(zone, ParseLocal(Str(a["StartDateTime"]))), ToOffset(zone, ParseLocal(Str(a["EndDateTime"]))), clientId, already);

    // ------------------------------------------------------------------ 4. cancel

    public async Task<StudioCancellationDto> CancelClassBookingAsync(StudioShopper shopper, string bookingId, CancellationToken ct)
    {
        var parts = bookingId.Split('-');
        if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var classId)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var visitId))
        {
            throw StudioException.NotFound("Booking " + bookingId + " was not found.");
        }

        using var _ = await _locks.AcquireAsync(LockKey(shopper), ct);
        var client = await FindClientAsync(shopper, ct) ?? throw StudioException.NotFound("Booking " + bookingId + " was not found.");

        var visits = await _mindbody.GetAsync("class/classvisits", new KeyValuePair<string, string?>[]
        {
            new("ClassID", classId.ToString(CultureInfo.InvariantCulture))
        }, ct);
        var all = visits["Class"]?["Visits"]?.AsArray() ?? new JsonArray();
        var visit = all.FirstOrDefault(v => v is not null && Long(v["Id"]) == visitId);
        if (visit is null)
        {
            // Already cancelled (e.g. the app is repeating a cancel that went through).
            return new StudioCancellationDto(bookingId, "Cancelled");
        }
        if (Str(visit["ClientId"]) != client.Id) throw StudioException.NotFound("Booking " + bookingId + " was not found.");

        try
        {
            await _mindbody.PostAsync("class/removeclientfromclass", new JsonObject
            {
                ["ClientId"] = client.Id,
                ["ClassId"] = classId,
                ["LateCancel"] = false,
                ["SendEmail"] = false,
                ["Test"] = false
            }, ct);
        }
        catch (MindbodyApiException ex) when (ex.Message.Contains("No class visit found", StringComparison.OrdinalIgnoreCase))
        {
            // Cancelled by a concurrent request.
        }
        catch (MindbodyApiException ex)
        {
            throw StudioException.Conflict(ex.Message);
        }
        return new StudioCancellationDto(bookingId, "Cancelled");
    }

    public async Task<StudioCancellationDto> CancelAppointmentBookingAsync(StudioShopper shopper, string bookingId, CancellationToken ct)
    {
        if (!long.TryParse(bookingId, NumberStyles.None, CultureInfo.InvariantCulture, out var appointmentId))
        {
            throw StudioException.NotFound("Booking " + bookingId + " was not found.");
        }

        using var _ = await _locks.AcquireAsync(LockKey(shopper), ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var found = await _mindbody.GetAllAsync("appointment/staffappointments", "Appointments", new KeyValuePair<string, string?>[]
        {
            new("AppointmentIds", appointmentId.ToString(CultureInfo.InvariantCulture)),
            new("StartDate", DateIso(today.AddDays(-800))),
            new("EndDate", DateIso(today.AddDays(800)))
        }, ct);
        var appointment = found.FirstOrDefault(a => Long(a["Id"]) == appointmentId);
        if (appointment is null)
        {
            // Already cancelled (e.g. the app is repeating a cancel that went through).
            return new StudioCancellationDto(bookingId, "Cancelled");
        }

        var client = await FindClientAsync(shopper, ct);
        if (client is null || Str(appointment["ClientId"]) != client.Id) throw StudioException.NotFound("Booking " + bookingId + " was not found.");
        if (CancelledStatuses.Contains(Str(appointment["Status"]) ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            return new StudioCancellationDto(bookingId, "Cancelled");
        }

        try
        {
            await _mindbody.PostAsync("appointment/updateappointment", new JsonObject
            {
                ["AppointmentId"] = appointmentId,
                ["Execute"] = "Cancel",
                ["SendEmail"] = false,
                ["Test"] = false
            }, ct);
        }
        catch (MindbodyApiException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            // Cancelled by a concurrent request.
        }
        catch (MindbodyApiException ex)
        {
            throw StudioException.Conflict(ex.Message);
        }
        return new StudioCancellationDto(bookingId, "Cancelled");
    }

    // ------------------------------------------------------------------ 5. trainers

    public async Task<IReadOnlyList<StudioTrainerDto>> ListTrainersAsync(CancellationToken ct)
    {
        var staff = await _mindbody.GetAllAsync("staff/staff", "StaffMembers", null, ct);
        return staff
            .Where(s => Bool(s["ClassTeacher"]) || Bool(s["AppointmentInstructor"]))
            .Select(s => new StudioTrainerDto(
                Long(s["Id"]) ?? 0, Str(s["FirstName"]), Str(s["LastName"]), Str(s["DisplayName"]) ?? Str(s["Name"]),
                Bool(s["Active"], true), Bool(s["ClassTeacher"]), Bool(s["AppointmentInstructor"]),
                Str(s["Email"]), Str(s["MobilePhone"]), Str(s["HomePhone"]), Str(s["WorkPhone"]),
                Str(s["Address"]), Str(s["City"]), Str(s["State"]), Str(s["PostalCode"]), Str(s["Country"]),
                Str(s["Bio"]), Str(s["ImageUrl"])))
            .OrderBy(t => t.LastName).ThenBy(t => t.FirstName).ThenBy(t => t.Id)
            .ToList();
    }
}
