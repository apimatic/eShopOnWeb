using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Mindbody;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Studio operations, implemented over the Mindbody HTTP API.
/// Singleton: holds only caches and lock stripes, so it is safe for a process that runs for weeks.
/// </summary>
public class StudioService
{
    private const int MAX_RANGE_DAYS = 366;
    private const string DATE_TIME_FORMAT = "yyyy-MM-dd'T'HH:mm:ss";
    private static readonly TimeSpan LookupCacheTime = TimeSpan.FromHours(1);

    private readonly MindbodyClient _mb;
    private readonly IMemoryCache _cache;
    private readonly SemaphoreSlim[] _clientCreateLocks = CreateStripes();
    private readonly SemaphoreSlim[] _bookingLocks = CreateStripes();

    public StudioService(MindbodyClient mb, IMemoryCache cache)
    {
        _mb = mb;
        _cache = cache;
    }

    // ---------------------------------------------------------------- classes

    public async Task<List<ClassDto>> ListClassesAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var tz = await GetTimeZoneAsync(ct);
        var today = DateOnly.FromDateTime(LocalNow(tz));
        var (start, end) = ResolveRange(from, to, today, 7);

        var classes = await FetchClassesAsync(null, start, end, ct);
        var now = LocalNow(tz);
        return classes
            .Where(c => IsAttendable(c, now))
            .OrderBy(c => c.Start)
            .ThenBy(c => c.Id)
            .Select(c => ToDto(c, tz))
            .ToList();
    }

    public async Task<List<SlotDto>> ListSlotsAsync(int sessionTypeId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (from is null || to is null)
            throw new StudioException(400, "Both 'from' and 'to' dates are required.");
        if (to < from)
            throw new StudioException(400, "'to' must not be before 'from'.");
        if (to.Value.DayNumber - from.Value.DayNumber >= MAX_RANGE_DAYS)
            throw new StudioException(400, $"The date range may not be longer than {MAX_RANGE_DAYS} days.");

        var tz = await GetTimeZoneAsync(ct);
        var type = await GetSessionTypeAsync(sessionTypeId, ct);
        var length = SessionLength(type);
        var times = await GetActiveSessionTimesAsync(sessionTypeId, from.Value, to.Value, ct);

        var slots = new List<SlotDto>();
        for (var day = from.Value; day <= to.Value; day = day.AddDays(1))
        {
            foreach (var t in times)
            {
                var local = day.ToDateTime(t);
                if (tz.IsInvalidTime(local))
                    continue;
                var start = new DateTimeOffset(local, tz.GetUtcOffset(local));
                slots.Add(new SlotDto(Iso(start), Iso(TimeZoneInfo.ConvertTime(start.AddMinutes(length), tz))));
            }
        }
        return slots;
    }

    // ---------------------------------------------------------------- clients

    /// <summary>Finds the shopper's studio client; creates it (once) when details are supplied.</summary>
    public async Task<string> EnsureClientAsync(StudioShopper shopper, NewClientDetails? details, CancellationToken ct)
    {
        var known = await FindClientIdAsync(shopper.Email, ct);
        if (known is not null)
            return known;

        var missing = MissingDetails(details);
        if (missing.Count > 0)
        {
            throw new StudioException(400,
                "This is the shopper's first studio booking, so these details are required: " + string.Join(", ", missing) + ".");
        }

        await ValidateNewClientAsync(details!, ct);

        var gate = Stripe(_clientCreateLocks, shopper.Email.ToLowerInvariant());
        await gate.WaitAsync(ct);
        try
        {
            // A retried request may have created the client while we waited.
            known = await FindClientIdAsync(shopper.Email, ct);
            if (known is not null)
                return known;

            var (first, last) = NameFor(shopper, details!);
            try
            {
                var created = await _mb.PostAsync("client/addclient", new
                {
                    FirstName = first,
                    LastName = last,
                    Email = shopper.Email,
                    BirthDate = details!.BirthDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    AddressLine1 = details.AddressLine1!.Trim(),
                    City = details.City!.Trim(),
                    State = details.State!.Trim(),
                    PostalCode = details.PostalCode!.Trim(),
                    MobilePhone = details.MobilePhone!.Trim(),
                    ReferredBy = details.ReferredBy!.Trim(),
                    HomeLocation = new { Id = details.HomeLocationId!.Value },
                }, ct);
                var id = Str(created.GetProperty("Client"), "Id")
                    ?? throw new StudioException(502, "The studio schedule service did not return the new client.");
                Remember(shopper.Email, id);
                return id;
            }
            catch (StudioException ex) when (ex.MindbodyCode == "InvalidClientCreation")
            {
                known = await FindClientIdAsync(shopper.Email, ct);
                if (known is not null)
                    return known;
                throw new StudioException(409, ex.Message, ex.MindbodyCode);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string?> FindClientIdAsync(string email, CancellationToken ct)
    {
        var key = ClientKey(email);
        if (_cache.TryGetValue(key, out string? cached) && cached is not null)
            return cached;

        var clients = await _mb.GetAllAsync("client/clients", "Clients", new Dictionary<string, string> { ["SearchText"] = email }, ct);
        var match = clients.FirstOrDefault(c => string.Equals(Str(c, "Email")?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
        var id = match.ValueKind == JsonValueKind.Object ? Str(match, "Id") : null;
        if (id is not null)
            Remember(email, id);
        return id;
    }

    private void Remember(string email, string clientId)
        => _cache.Set(ClientKey(email), clientId, TimeSpan.FromHours(6));

    private static string ClientKey(string email) => "studio-client:" + email.Trim().ToLowerInvariant();

    private static List<string> MissingDetails(NewClientDetails? d)
    {
        var missing = new List<string>();
        if (d is null)
            d = new NewClientDetails();
        if (d.HomeLocationId is null) missing.Add("homeLocationId");
        if (string.IsNullOrWhiteSpace(d.AddressLine1)) missing.Add("addressLine1");
        if (string.IsNullOrWhiteSpace(d.City)) missing.Add("city");
        if (string.IsNullOrWhiteSpace(d.State)) missing.Add("state");
        if (string.IsNullOrWhiteSpace(d.PostalCode)) missing.Add("postalCode");
        if (d.BirthDate is null) missing.Add("birthDate");
        if (string.IsNullOrWhiteSpace(d.MobilePhone)) missing.Add("mobilePhone");
        if (string.IsNullOrWhiteSpace(d.ReferredBy)) missing.Add("referredBy");
        return missing;
    }

    private async Task ValidateNewClientAsync(NewClientDetails d, CancellationToken ct)
    {
        var tz = await GetTimeZoneAsync(ct);
        if (d.BirthDate!.Value >= DateOnly.FromDateTime(LocalNow(tz)))
            throw new StudioException(400, "birthDate must be in the past.");

        var locations = await GetLocationIdsAsync(ct);
        if (!locations.Contains(d.HomeLocationId!.Value))
            throw new StudioException(400, "homeLocationId is not one of the studio's locations.");
    }

    private static (string First, string Last) NameFor(StudioShopper shopper, NewClientDetails d)
    {
        var first = d.FirstName?.Trim();
        var last = d.LastName?.Trim();
        if (!string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(last))
            return (first, last);

        // eShop identities carry no personal name; derive one from the e-mail address.
        var local = shopper.Email.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        string Cap(string s) => char.ToUpperInvariant(s[0]) + s[1..];
        var f = !string.IsNullOrEmpty(first) ? first : parts.Length > 0 ? Cap(parts[0]) : Cap(local);
        var l = !string.IsNullOrEmpty(last) ? last : parts.Length > 1 ? Cap(parts[^1]) : "Shopper";
        return (f, l);
    }

    // ---------------------------------------------------------------- class bookings

    public async Task<ClassBookingDto> BookClassAsync(StudioShopper shopper, int classId, NewClientDetails? details, CancellationToken ct)
    {
        var tz = await GetTimeZoneAsync(ct);
        var cls = await FindClassAsync(classId, ct);
        if (cls is null || !IsAttendable(cls, LocalNow(tz)))
            throw new StudioException(409, "That class is not open for booking (it does not exist, is cancelled, is full or has finished).");

        var clientId = await EnsureClientAsync(shopper, details, ct);
        return await PlaceInClassAsync(clientId, cls, ct);
    }

    public async Task<ClassBookingDto> BookClassForShopperAsync(StudioShopper shopper, int classId, CancellationToken ct)
    {
        var cls = await FindClassAsync(classId, ct)
            ?? throw new StudioException(404, "There is no such class in the timetable.");
        if (cls.IsCanceled)
            throw new StudioException(409, "That class has been cancelled.");

        var clientId = await FindClientIdAsync(shopper.Email, ct)
            ?? throw new StudioException(409, "That shopper is not a studio client yet; they become one with their first booking.");
        return await PlaceInClassAsync(clientId, cls, ct);
    }

    private async Task<ClassBookingDto> PlaceInClassAsync(string clientId, ClassInfo cls, CancellationToken ct)
    {
        var tz = await GetTimeZoneAsync(ct);
        var gate = Stripe(_bookingLocks, clientId);
        await gate.WaitAsync(ct);
        try
        {
            // Mindbody does not refuse a second booking of the same class, so a retried request must be recognised here.
            var visits = await GetVisitsAsync(clientId, cls.Start.Date, cls.End.Date, ct);
            var existing = visits.FirstOrDefault(v => Long(v, "ClassId") == cls.Id && IsLive(v));
            if (existing.ValueKind == JsonValueKind.Object)
                return ToBooking(existing, cls, tz);

            var result = await _mb.PostAsync("class/addclienttoclass", new { ClientId = clientId, ClassId = cls.Id }, ct);
            var visit = result.GetProperty("Visit");
            return ToBooking(visit, cls, tz);
        }
        finally
        {
            gate.Release();
        }
    }

    private static ClassBookingDto ToBooking(JsonElement visit, ClassInfo cls, TimeZoneInfo tz)
        => new(Long(visit, "Id") ?? 0, cls.Id, cls.Name, Iso(cls.Start, tz), Iso(cls.End, tz), cls.LocationId);

    public async Task<CancelledBookingDto> CancelClassBookingAsync(StudioShopper shopper, long bookingId, CancellationToken ct)
    {
        var clientId = await FindClientIdAsync(shopper.Email, ct)
            ?? throw new StudioException(404, "There is no such booking.");
        var tz = await GetTimeZoneAsync(ct);
        var today = LocalNow(tz).Date;

        var gate = Stripe(_bookingLocks, clientId);
        await gate.WaitAsync(ct);
        try
        {
            var visits = await GetVisitsAsync(clientId, today.AddDays(-1), today.AddDays(400), ct);
            var visit = visits.FirstOrDefault(v => Long(v, "Id") == bookingId && (Long(v, "ClassId") ?? 0) > 0);
            if (visit.ValueKind != JsonValueKind.Object || !IsLive(visit))
            {
                // A retried cancellation must still look like a success.
                if (WasCancelled(clientId, "class", bookingId))
                    return new CancelledBookingDto(bookingId, "Cancelled");
                throw new StudioException(404, "There is no such booking.");
            }

            await _mb.PostAsync("class/removeclientfromclass", new { ClientId = clientId, ClassId = Long(visit, "ClassId") }, ct);
            MarkCancelled(clientId, "class", bookingId);
            return new CancelledBookingDto(bookingId, "Cancelled");
        }
        finally
        {
            gate.Release();
        }
    }

    // ---------------------------------------------------------------- appointment bookings

    public async Task<AppointmentBookingDto> BookAppointmentAsync(StudioShopper shopper, int sessionTypeId, DateTimeOffset startTime,
        int? locationId, long? staffId, NewClientDetails? details, CancellationToken ct)
    {
        var tz = await GetTimeZoneAsync(ct);
        var type = await GetSessionTypeAsync(sessionTypeId, ct);
        var length = SessionLength(type);
        var localStart = TimeZoneInfo.ConvertTime(startTime, tz).DateTime;
        localStart = new DateTime(localStart.Year, localStart.Month, localStart.Day, localStart.Hour, localStart.Minute, 0, DateTimeKind.Unspecified);
        if (startTime <= DateTimeOffset.UtcNow)
            throw new StudioException(400, "startTime must be in the future.");
        var localEnd = localStart.AddMinutes(length);

        var clientId = await EnsureClientAsync(shopper, details, ct);

        var gate = Stripe(_bookingLocks, clientId);
        await gate.WaitAsync(ct);
        try
        {
            var existing = (await GetAppointmentsAsync(clientId, localStart.Date, localStart.Date.AddDays(1), ct))
                .FirstOrDefault(a => Long(a, "SessionTypeId") == sessionTypeId && ParseLocal(Str(a, "StartDateTime")) == localStart && IsLiveAppointment(a));
            if (existing.ValueKind == JsonValueKind.Object)
                return ToAppointment(existing, type, tz);

            var query = new Dictionary<string, string>
            {
                ["SessionTypeIds"] = sessionTypeId.ToString(),
                ["StartDateTime"] = localStart.Date.ToString(DATE_TIME_FORMAT, CultureInfo.InvariantCulture),
                ["EndDateTime"] = localStart.Date.AddDays(1).AddSeconds(-1).ToString(DATE_TIME_FORMAT, CultureInfo.InvariantCulture),
            };
            if (locationId is not null) query["LocationIds"] = locationId.Value.ToString();
            if (staffId is not null) query["StaffIds"] = staffId.Value.ToString();

            var items = await _mb.GetAllAsync("appointment/bookableitems", "Availabilities", query, ct);
            var candidates = items
                .Select(a => (Staff: Long(a.GetProperty("Staff"), "Id"), Location: Long(a.GetProperty("Location"), "Id"),
                    Start: ParseLocal(Str(a, "StartDateTime")), End: ParseLocal(Str(a, "EndDateTime"))))
                .Where(a => a.Staff is not null && a.Location is not null && a.Start <= localStart && localEnd <= a.End)
                .DistinctBy(a => (a.Staff, a.Location))
                .ToList();
            if (candidates.Count == 0)
                throw new StudioException(409, "No trainer can take a session at that time.");

            StudioException? last = null;
            foreach (var c in candidates)
            {
                try
                {
                    var result = await _mb.PostAsync("appointment/addappointment", new
                    {
                        ClientId = clientId,
                        LocationId = c.Location,
                        SessionTypeId = sessionTypeId,
                        StaffId = c.Staff,
                        StartDateTime = localStart.ToString(DATE_TIME_FORMAT, CultureInfo.InvariantCulture),
                    }, ct);
                    return ToAppointment(result.GetProperty("Appointment"), type, tz);
                }
                catch (StudioException ex) when (ex.StatusCode is 422 or 404)
                {
                    last = ex;
                }
            }
            throw new StudioException(409, last?.Message ?? "That time could not be booked.", last?.MindbodyCode);
        }
        finally
        {
            gate.Release();
        }
    }

    private static AppointmentBookingDto ToAppointment(JsonElement a, JsonElement type, TimeZoneInfo tz)
    {
        var start = ParseLocal(Str(a, "StartDateTime"));
        var end = ParseLocal(Str(a, "EndDateTime"));
        return new AppointmentBookingDto(Long(a, "Id") ?? 0, (int)(Long(a, "SessionTypeId") ?? Long(type, "Id") ?? 0),
            Str(type, "Name") ?? "", Iso(start, tz), Iso(end, tz), (int?)Long(a, "LocationId"), Long(a, "StaffId"));
    }

    public async Task<CancelledBookingDto> CancelAppointmentAsync(StudioShopper shopper, long bookingId, CancellationToken ct)
    {
        var clientId = await FindClientIdAsync(shopper.Email, ct)
            ?? throw new StudioException(404, "There is no such booking.");
        var tz = await GetTimeZoneAsync(ct);
        var today = LocalNow(tz).Date;

        var gate = Stripe(_bookingLocks, clientId);
        await gate.WaitAsync(ct);
        try
        {
            var appointments = await GetAppointmentsAsync(clientId, today.AddDays(-1), today.AddDays(400), ct);
            var appointment = appointments.FirstOrDefault(a => Long(a, "Id") == bookingId);
            if (appointment.ValueKind != JsonValueKind.Object || !IsLiveAppointment(appointment))
            {
                if (WasCancelled(clientId, "appointment", bookingId))
                    return new CancelledBookingDto(bookingId, "Cancelled");
                throw new StudioException(404, "There is no such booking.");
            }

            await _mb.PostAsync("appointment/updateappointment", new { AppointmentId = bookingId, Execute = "Cancel" }, ct);
            MarkCancelled(clientId, "appointment", bookingId);
            return new CancelledBookingDto(bookingId, "Cancelled");
        }
        finally
        {
            gate.Release();
        }
    }

    // ---------------------------------------------------------------- trainers

    public async Task<List<TrainerDto>> ListTrainersAsync(CancellationToken ct)
    {
        var staff = await _mb.GetAllAsync("staff/staff", "StaffMembers", null, ct);
        return staff
            .Where(s => Bool(s, "ClassTeacher") || Bool(s, "AppointmentInstructor"))
            .Select(s => new TrainerDto(
                Long(s, "Id") ?? 0,
                Str(s, "Name") ?? Str(s, "DisplayName") ?? "",
                Str(s, "FirstName"), Str(s, "LastName"), Bool(s, "Active"),
                Bool(s, "ClassTeacher"), Bool(s, "AppointmentInstructor"),
                Str(s, "Email"), Str(s, "MobilePhone"), Str(s, "HomePhone"), Str(s, "WorkPhone"),
                Join(Str(s, "Address"), Str(s, "Address2")), Str(s, "City"), Str(s, "State"), Str(s, "PostalCode"), Str(s, "Country")))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? Join(string? a, string? b)
    {
        var parts = new[] { a, b }.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return parts.Length == 0 ? null : string.Join(", ", parts);
    }

    // ---------------------------------------------------------------- Mindbody lookups

    private record ClassInfo(long Id, string Name, string? Description, DateTime Start, DateTime End, int? LocationId,
        string? LocationName, string? Trainer, bool IsCanceled, bool Active, bool IsAvailable, string ScheduleType, int WebCapacity, int WebBooked);

    private async Task<List<ClassInfo>> FetchClassesAsync(int? classId, DateTime start, DateTime end, CancellationToken ct)
    {
        var query = new Dictionary<string, string>
        {
            ["StartDateTime"] = start.ToString(DATE_TIME_FORMAT, CultureInfo.InvariantCulture),
            ["EndDateTime"] = end.ToString(DATE_TIME_FORMAT, CultureInfo.InvariantCulture),
        };
        if (classId is not null)
            query["ClassIds"] = classId.Value.ToString();

        var items = await _mb.GetAllAsync("class/classes", "Classes", query, ct);
        var result = new List<ClassInfo>();
        foreach (var c in items)
        {
            var desc = Obj(c, "ClassDescription");
            var program = desc is null ? null : Obj(desc.Value, "Program");
            var location = Obj(c, "Location");
            var staff = Obj(c, "Staff");
            result.Add(new ClassInfo(
                Long(c, "Id") ?? 0,
                (desc is null ? null : Str(desc.Value, "Name")) ?? "Class",
                desc is null ? null : Str(desc.Value, "Description"),
                ParseLocal(Str(c, "StartDateTime")),
                ParseLocal(Str(c, "EndDateTime")),
                location is null ? null : (int?)Long(location.Value, "Id"),
                location is null ? null : Str(location.Value, "Name"),
                staff is null ? null : Str(staff.Value, "DisplayName") ?? Str(staff.Value, "Name"),
                Bool(c, "IsCanceled"), Bool(c, "Active"), Bool(c, "IsAvailable"),
                (program is null ? null : Str(program.Value, "ScheduleType")) ?? "Class",
                (int)(Long(c, "WebCapacity") ?? 0), (int)(Long(c, "WebBooked") ?? 0)));
        }
        return result;
    }

    private async Task<ClassInfo?> FindClassAsync(int classId, CancellationToken ct)
    {
        var tz = await GetTimeZoneAsync(ct);
        var today = LocalNow(tz).Date;
        var found = await FetchClassesAsync(classId, today.AddDays(-7), today.AddDays(400), ct);
        return found.FirstOrDefault(c => c.Id == classId);
    }

    /// <summary>A class a shopper can still attend: live, open to online booking, with room, not finished.</summary>
    private static bool IsAttendable(ClassInfo c, DateTime localNow)
        => !c.IsCanceled && c.Active && c.IsAvailable
           && string.Equals(c.ScheduleType, "Class", StringComparison.OrdinalIgnoreCase)
           && c.WebCapacity > 0 && c.WebBooked < c.WebCapacity
           && c.End > localNow;

    private static ClassDto ToDto(ClassInfo c, TimeZoneInfo tz)
        => new(c.Id, c.Name, string.IsNullOrWhiteSpace(c.Description) ? null : c.Description,
            Iso(c.Start, tz), Iso(c.End, tz), c.LocationId, c.LocationName, c.Trainer, c.WebCapacity - c.WebBooked);

    private Task<List<JsonElement>> GetVisitsAsync(string clientId, DateTime from, DateTime to, CancellationToken ct)
        => _mb.GetAllAsync("client/clientvisits", "Visits", new Dictionary<string, string>
        {
            ["ClientId"] = clientId,
            ["StartDate"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["EndDate"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        }, ct);

    private Task<List<JsonElement>> GetAppointmentsAsync(string clientId, DateTime from, DateTime to, CancellationToken ct)
        => _mb.GetAllAsync("appointment/staffappointments", "Appointments", new Dictionary<string, string>
        {
            ["ClientIds"] = clientId,
            ["StartDate"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["EndDate"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        }, ct);

    private static bool IsLive(JsonElement visit)
    {
        var status = Str(visit, "AppointmentStatus") ?? "";
        return !Bool(visit, "LateCancelled") && !status.Contains("Cancel", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLiveAppointment(JsonElement a)
    {
        var status = Str(a, "Status") ?? "";
        return !status.Contains("Cancel", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue("studio-tz", out TimeZoneInfo? cached) && cached is not null)
            return cached;

        var root = await _mb.GetAsync("site/sites", null, ct);
        var id = root.TryGetProperty("Sites", out var sites) && sites.GetArrayLength() > 0 ? Str(sites[0], "TimeZone") : null;
        if (string.IsNullOrWhiteSpace(id))
            throw new StudioException(502, "The studio schedule service did not say which time zone the studio is in.");

        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId))
                tz = TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            else
                throw new StudioException(502, "The studio's time zone '" + id + "' is not known to this server.", null, ex);
        }
        _cache.Set("studio-tz", tz, TimeSpan.FromHours(6));
        return tz;
    }

    private async Task<JsonElement> GetSessionTypeAsync(int id, CancellationToken ct)
    {
        var types = await _cache.GetOrCreateAsync("studio-session-types", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = LookupCacheTime;
            return await _mb.GetAllAsync("site/sessiontypes", "SessionTypes", new Dictionary<string, string> { ["OnlineOnly"] = "false" }, ct);
        }) ?? new List<JsonElement>();

        var type = types.FirstOrDefault(t => Long(t, "Id") == id);
        if (type.ValueKind != JsonValueKind.Object)
            throw new StudioException(404, "There is no such session type.");

        var kind = Str(type, "Type") ?? "";
        if (kind.Equals("Class", StringComparison.OrdinalIgnoreCase) || kind.Equals("Enrollment", StringComparison.OrdinalIgnoreCase))
            throw new StudioException(400, "That session type is for classes, not one-to-one sessions.");
        return type;
    }

    private static int SessionLength(JsonElement type)
    {
        var length = Long(type, "DefaultTimeLength") ?? Long(type, "StaffTimeLength");
        if (length is null or <= 0)
            throw new StudioException(422, "That session type has no length set in the studio, so its times cannot be given.");
        return (int)length.Value;
    }

    private async Task<List<TimeOnly>> GetActiveSessionTimesAsync(int sessionTypeId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var root = await _mb.GetAsync("appointment/activesessiontimes", new Dictionary<string, string>
        {
            ["SessionTypeIds"] = sessionTypeId.ToString(),
            ["StartDate"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["EndDate"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        }, ct);

        JsonElement? list = null;
        if (root.TryGetProperty("SessionTypeActiveSessionTimes", out var perType) && perType.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in perType.EnumerateArray())
            {
                if (Long(entry, "SessionTypeId") == sessionTypeId && entry.TryGetProperty("ActiveSessionTimes", out var l))
                    list = l;
            }
        }
        if (list is null && root.TryGetProperty("ActiveSessionTimes", out var all))
            list = all;

        var times = new SortedSet<TimeOnly>();
        if (list is { ValueKind: JsonValueKind.Array })
        {
            foreach (var t in list.Value.EnumerateArray())
            {
                if (TimeOnly.TryParse(t.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    times.Add(parsed);
            }
        }
        return times.ToList();
    }

    private async Task<HashSet<int>> GetLocationIdsAsync(CancellationToken ct)
    {
        var ids = await _cache.GetOrCreateAsync("studio-locations", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = LookupCacheTime;
            var items = await _mb.GetAllAsync("site/locations", "Locations", null, ct);
            return items.Select(l => (int?)Long(l, "Id")).Where(i => i is not null).Select(i => i!.Value).ToHashSet();
        });
        return ids ?? new HashSet<int>();
    }

    // ---------------------------------------------------------------- idempotent cancel memory

    private void MarkCancelled(string clientId, string kind, long id)
        => _cache.Set($"studio-cancelled:{clientId}:{kind}:{id}", true, TimeSpan.FromDays(2));

    private bool WasCancelled(string clientId, string kind, long id)
        => _cache.TryGetValue($"studio-cancelled:{clientId}:{kind}:{id}", out _);

    // ---------------------------------------------------------------- helpers

    private static (DateTime Start, DateTime End) ResolveRange(DateOnly? from, DateOnly? to, DateOnly today, int defaultDays)
    {
        var f = from ?? today;
        var t = to ?? (from is null ? today.AddDays(defaultDays) : f.AddDays(defaultDays));
        if (t < f)
            throw new StudioException(400, "'to' must not be before 'from'.");
        if (t.DayNumber - f.DayNumber >= MAX_RANGE_DAYS)
            throw new StudioException(400, $"The date range may not be longer than {MAX_RANGE_DAYS} days.");
        return (f.ToDateTime(TimeOnly.MinValue), t.ToDateTime(new TimeOnly(23, 59, 59)));
    }

    private static DateTime LocalNow(TimeZoneInfo tz) => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime;

    private static DateTime ParseLocal(string? s)
        => s is null ? default : DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static string Iso(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>Studio wall-clock time to an ISO-8601 date-time with the studio's UTC offset for that instant.</summary>
    private static string Iso(DateTime local, TimeZoneInfo tz)
        => Iso(new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), tz.GetUtcOffset(local)));

    private static SemaphoreSlim[] CreateStripes() => Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private static SemaphoreSlim Stripe(SemaphoreSlim[] stripes, string key)
    {
        var hash = 17;
        foreach (var ch in key)
            hash = unchecked(hash * 31 + ch);
        return stripes[(hash & int.MaxValue) % stripes.Length];
    }

    private static JsonElement? Obj(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    private static string? Str(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
    }

    private static long? Long(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
            return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n))
            return n;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s))
            return s;
        return null;
    }

    private static bool Bool(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
