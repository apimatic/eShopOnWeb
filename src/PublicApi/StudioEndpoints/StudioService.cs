using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;
using MindbodyPublicApi.Models;
using MindbodyPublicApi.Requests.AppointmentApi;
using MindbodyPublicApi.Requests.ClassApi;
using MindbodyPublicApi.Requests.ClientApi;
using MindbodyPublicApi.Requests.SiteApi;
using MindbodyPublicApi.Requests.StaffApi;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>
/// The studio features, implemented on top of Mindbody (through the SDK).
/// </summary>
public class StudioService
{
    private const int PageSize = 200;
    private const int MaxRangeDays = 366;
    private const int DefaultClassDays = 7;
    private static readonly TimeSpan CancelledMemory = TimeSpan.FromDays(7);

    // One lock per shopper (striped), so a shopper whose app repeats a request cannot get two clients or two bookings.
    private static readonly SemaphoreSlim[] ShopperLocks =
        Enumerable.Range(0, 128).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly MindbodyGateway _gateway;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMemoryCache _cache;

    public StudioService(MindbodyGateway gateway, UserManager<ApplicationUser> userManager, IMemoryCache cache)
    {
        _gateway = gateway;
        _userManager = userManager;
        _cache = cache;
    }

    private sealed record Shopper(string UserName, string Email);

    // ---------------------------------------------------------------- classes

    public async Task<StudioClassListResponse> ListClassesAsync(string? fromText, string? toText, CancellationToken ct)
    {
        var zone = await _gateway.GetSiteTimeZoneAsync(ct);
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        var today = DateOnly.FromDateTime(now.DateTime);

        var from = ParseDate(fromText, "from");
        var to = ParseDate(toText, "to");
        if (from is null && to is null)
        {
            from = today;
            to = today.AddDays(DefaultClassDays);
        }
        else if (from is null)
        {
            from = today;
        }
        else if (to is null)
        {
            to = from.Value.AddDays(DefaultClassDays);
        }

        ValidateRange(from!.Value, to!.Value);

        var classes = await GetClassesAsync(from.Value, to.Value, null, zone, ct);
        var result = classes
            .Where(c => IsAttendable(c, now, zone, out _))
            .Select(c => ToClassDto(c, zone))
            .Where(c => DateOnly.FromDateTime(c.StartTime.DateTime) >= from.Value
                        && DateOnly.FromDateTime(c.StartTime.DateTime) <= to.Value)
            .OrderBy(c => c.StartTime)
            .ThenBy(c => c.ClassId)
            .ToList();

        return new StudioClassListResponse(from.Value, to.Value, result);
    }

    private async Task<List<Class>> GetClassesAsync(DateOnly from, DateOnly to, int? classId, TimeZoneInfo zone, CancellationToken ct)
    {
        var siteId = _gateway.SiteId;
        // Mindbody reads these as instants and converts them to the studio's clock.
        var start = MindbodyGateway.FromWallClock(from.ToDateTime(TimeOnly.MinValue), zone);
        var end = MindbodyGateway.FromWallClock(to.ToDateTime(new TimeOnly(23, 59, 59)), zone);
        var all = new List<Class>();
        var offset = 0;
        while (true)
        {
            var currentOffset = offset;
            var page = await _gateway.CallAsync(token => _gateway.Client.ClassApi.GetClasses(new GetClassesOperationRequest
            {
                SiteId = siteId,
                Authorization = token,
                RequestStartDateTime = start,
                RequestEndDateTime = end,
                RequestClassIds = classId is null ? null : new[] { classId.Value },
                RequestHideCanceledClasses = classId is null ? true : null,
                RequestLimit = PageSize,
                RequestOffset = currentOffset
            }, null, ct), ct);

            var items = page.Classes ?? Array.Empty<Class>();
            all.AddRange(items);
            offset += items.Count;
            var total = page.PaginationResponse?.TotalResults ?? 0;
            if (items.Count == 0 || offset >= total)
                break;
        }

        return all;
    }

    private static bool IsAttendable(Class c, DateTimeOffset now, TimeZoneInfo zone, out string reason)
    {
        reason = "";
        if (c.Id is null || c.StartDateTime is null || c.EndDateTime is null)
        {
            reason = "The class is not open for booking.";
            return false;
        }

        if (c.IsCanceled == true)
        {
            reason = "The class has been canceled.";
            return false;
        }

        if (c.Active == false)
        {
            reason = "The class is not open for booking.";
            return false;
        }

        if (MindbodyGateway.ToSiteTime(c.StartDateTime.Value, zone) <= now)
        {
            reason = "The class has already started.";
            return false;
        }

        if (c.IsAvailable != true)
        {
            reason = "The class is full or not open for booking.";
            return false;
        }

        return true;
    }

    private static StudioClassDto ToClassDto(Class c, TimeZoneInfo zone) =>
        new(
            c.Id!.Value,
            c.ClassDescription?.Name,
            c.ClassDescription?.Description,
            MindbodyGateway.ToSiteTime(c.StartDateTime!.Value, zone),
            MindbodyGateway.ToSiteTime(c.EndDateTime!.Value, zone),
            c.Location is null ? null : new StudioLocationDto(c.Location.Id, c.Location.Name),
            c.Staff?.DisplayName ?? c.Staff?.Name,
            c.MaxCapacity is null ? null : Math.Max(0, c.MaxCapacity.Value - (c.TotalBooked ?? 0)),
            c.IsWaitlistAvailable == true);

    // ---------------------------------------------------------------- session times

    public async Task<StudioSlotListResponse> ListSlotsAsync(string? sessionTypeText, string? fromText, string? toText, CancellationToken ct)
    {
        if (!int.TryParse(sessionTypeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sessionTypeId) || sessionTypeId <= 0)
            throw StudioException.BadRequest("'sessionTypeId' is required and must be a session type number.");

        var from = ParseDate(fromText, "from") ?? throw StudioException.BadRequest("'from' is required (yyyy-MM-dd).");
        var to = ParseDate(toText, "to") ?? throw StudioException.BadRequest("'to' is required (yyyy-MM-dd).");
        ValidateRange(from, to);

        var zone = await _gateway.GetSiteTimeZoneAsync(ct);
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);

        var sessionType = await GetSessionTypeAsync(sessionTypeId, ct);
        var duration = sessionType.DefaultTimeLength ?? 0;
        if (duration <= 0)
            throw StudioException.Conflict("The session type has no duration in the studio system.");

        var times = await GetActiveSessionTimesAsync(sessionTypeId, ct);

        var slots = new List<StudioSlotDto>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            foreach (var time in times)
            {
                var wall = day.ToDateTime(time);
                if (zone.IsInvalidTime(wall))
                    continue;
                var start = MindbodyGateway.FromWallClock(wall, zone);
                if (start <= now)
                    continue;
                var end = TimeZoneInfo.ConvertTime(start.AddMinutes(duration), zone);
                slots.Add(new StudioSlotDto(start, end));
            }
        }

        return new StudioSlotListResponse(sessionTypeId, sessionType.Name, duration, from, to, slots);
    }

    private async Task<SessionType> GetSessionTypeAsync(int sessionTypeId, CancellationToken ct)
    {
        var siteId = _gateway.SiteId;
        var offset = 0;
        while (true)
        {
            var currentOffset = offset;
            var page = await _gateway.CallAsync(token => _gateway.Client.SiteApi.GetSessionTypes(new GetSessionTypesOperationRequest
            {
                SiteId = siteId,
                Authorization = token,
                RequestLimit = PageSize,
                RequestOffset = currentOffset
            }, null, ct), ct);

            var items = page.SessionTypes ?? Array.Empty<SessionType>();
            var found = items.FirstOrDefault(s => s.Id == sessionTypeId);
            if (found is not null)
            {
                var kind = found.Type?.Value;
                if (string.Equals(kind, "Class", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(kind, "Enrollment", StringComparison.OrdinalIgnoreCase))
                    throw StudioException.BadRequest("That session type is not a one-to-one session.");
                return found;
            }

            offset += items.Count;
            if (items.Count == 0 || offset >= (page.PaginationResponse?.TotalResults ?? 0))
                throw StudioException.NotFound($"There is no session type {sessionTypeId}.");
        }
    }

    private async Task<List<TimeOnly>> GetActiveSessionTimesAsync(int sessionTypeId, CancellationToken ct)
    {
        var siteId = _gateway.SiteId;
        var result = new List<TimeOnly>();
        var offset = 0;
        while (true)
        {
            var currentOffset = offset;
            var page = await _gateway.CallAsync(token => _gateway.Client.AppointmentApi.GetActiveSessionTimes(new GetActiveSessionTimesOperationRequest
            {
                SiteId = siteId,
                Authorization = token,
                RequestSessionTypeIds = new[] { sessionTypeId },
                RequestLimit = PageSize,
                RequestOffset = currentOffset
            }, null, ct), ct);

            var items = page.ActiveSessionTimes ?? Array.Empty<string>();
            foreach (var text in items)
            {
                if (TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                    result.Add(t);
            }

            offset += items.Count;
            if (items.Count == 0 || offset >= (page.PaginationResponse?.TotalResults ?? 0))
                break;
        }

        return result.Distinct().OrderBy(t => t).ToList();
    }

    // ---------------------------------------------------------------- booking

    public async Task<StudioBookingDto> BookClassAsync(string? userName, ClassBookingRequest request, CancellationToken ct)
    {
        var shopper = await ResolveSignedInShopperAsync(userName);
        return await BookClassForAsync(shopper, request.ClassId, request, shopperRules: true, ct);
    }

    public async Task<StudioBookingDto> BookClassForShopperAsync(AdminClassBookingRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Shopper))
            throw StudioException.BadRequest("'shopper' is required (the shopper's eShop user name).");

        var user = await _userManager.FindByNameAsync(request.Shopper.Trim())
                   ?? await _userManager.FindByEmailAsync(request.Shopper.Trim());
        if (user is null)
            throw StudioException.NotFound($"There is no shopper '{request.Shopper}'.");

        var shopper = ToShopper(user);
        return await BookClassForAsync(shopper, request.ClassId, request, shopperRules: false, ct);
    }

    private async Task<StudioBookingDto> BookClassForAsync(Shopper shopper, int? classId, NewClientDetails details, bool shopperRules, CancellationToken ct)
    {
        if (classId is null or <= 0)
            throw StudioException.BadRequest("'classId' is required.");

        var zone = await _gateway.GetSiteTimeZoneAsync(ct);
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        var today = DateOnly.FromDateTime(now.DateTime);

        var found = await GetClassesAsync(today.AddDays(-1), today.AddDays(MaxRangeDays * 2), classId, zone, ct);
        var cls = found.FirstOrDefault(c => c.Id == classId)
                  ?? throw StudioException.NotFound($"There is no class {classId} in the timetable.");

        if (shopperRules)
        {
            if (!IsAttendable(cls, now, zone, out var reason))
                throw StudioException.Conflict(reason);
        }
        else
        {
            if (cls.IsCanceled == true)
                throw StudioException.Conflict("The class has been canceled.");
            if (cls.EndDateTime is { } end && MindbodyGateway.ToSiteTime(end, zone) <= now)
                throw StudioException.Conflict("The class is already over.");
        }

        using var shopperLock = await LockAsync(shopper.Email, ct);

        var clientId = await FindClientIdAsync(shopper.Email, ct);
        if (clientId is not null)
        {
            // A repeated request: the shopper already holds a place in this class.
            var existing = (await GetScheduleAsync(clientId, zone, ct))
                .FirstOrDefault(v => v.ClassId == classId && IsActive(v));
            if (existing is not null)
                return ToBookingDto(existing, "class", zone) with { Name = cls.ClassDescription?.Name ?? existing.Name };
        }
        else
        {
            clientId = await CreateClientAsync(shopper, details, ct);
        }

        var siteId = _gateway.SiteId;
        var added = await _gateway.CallAsync(token => _gateway.Client.ClassApi.AddClientToClass(new AddClientToClassOperationRequest
        {
            SiteId = siteId,
            Authorization = token,
            Body = new AddClientToClassRequest { ClientId = clientId, ClassId = classId.Value }
        }, null, ct), ct);

        var visit = added.Visit;
        if (visit?.Id is null)
            throw new StudioException(502, "The studio system did not confirm the booking.");

        return new StudioBookingDto(
            visit.Id.Value,
            "class",
            cls.ClassDescription?.Name ?? visit.Name,
            MindbodyGateway.ToSiteTime(cls.StartDateTime!.Value, zone),
            MindbodyGateway.ToSiteTime(cls.EndDateTime!.Value, zone),
            visit.LocationId ?? cls.Location?.Id,
            visit.StaffId,
            "booked");
    }

    public async Task<StudioBookingDto> BookAppointmentAsync(string? userName, AppointmentBookingRequest request, CancellationToken ct)
    {
        var shopper = await ResolveSignedInShopperAsync(userName);

        if (request.SessionTypeId is null or <= 0)
            throw StudioException.BadRequest("'sessionTypeId' is required.");
        if (string.IsNullOrWhiteSpace(request.StartTime))
            throw StudioException.BadRequest("'startTime' is required (an ISO-8601 date-time).");

        var zone = await _gateway.GetSiteTimeZoneAsync(ct);
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);

        DateTime wall;
        if (DateTimeOffset.TryParse(request.StartTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            && HasOffset(request.StartTime))
            wall = TimeZoneInfo.ConvertTime(parsed, zone).DateTime;
        else if (DateTime.TryParse(request.StartTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var unspecified))
            wall = unspecified; // no offset given: the studio's own clock
        else
            throw StudioException.BadRequest("'startTime' is not a valid ISO-8601 date-time.");

        var start = MindbodyGateway.FromWallClock(wall, zone);
        if (start <= now)
            throw StudioException.Conflict("That time has already passed.");

        var sessionType = await GetSessionTypeAsync(request.SessionTypeId.Value, ct);
        var duration = sessionType.StaffTimeLength ?? sessionType.DefaultTimeLength ?? 0;
        if (duration <= 0)
            throw StudioException.Conflict("The session type has no duration in the studio system.");

        using var shopperLock = await LockAsync(shopper.Email, ct);

        var clientId = await FindClientIdAsync(shopper.Email, ct);
        if (clientId is not null)
        {
            // A repeated request: the shopper already holds this session.
            var existing = (await GetScheduleAsync(clientId, zone, ct)).FirstOrDefault(v =>
                v.AppointmentId is > 0 && IsActive(v) && v.VisitType == request.SessionTypeId
                && v.StartDateTime is { } s && s.DateTime == wall);
            if (existing is not null)
                return ToBookingDto(existing, "appointment", zone);
        }

        var candidates = await FindAvailabilitiesAsync(request, wall, duration, zone, ct);
        if (candidates.Count == 0)
            throw StudioException.Conflict("No trainer is free for that session at that time.");

        clientId ??= await CreateClientAsync(shopper, request, ct);

        var siteId = _gateway.SiteId;
        StudioException? lastConflict = null;
        foreach (var availability in candidates)
        {
            AddAppointmentResponse added;
            try
            {
                added = await _gateway.CallAsync(token => _gateway.Client.AppointmentApi.AddAppointment(new AddAppointmentOperationRequest
                {
                    SiteId = siteId,
                    Authorization = token,
                    Body = new AddAppointmentRequest
                    {
                        ClientId = clientId,
                        LocationId = availability.Location!.Id!.Value,
                        SessionTypeId = request.SessionTypeId,
                        StaffId = availability.Staff!.Id,
                        StartDateTime = MindbodyGateway.ToMindbodyWallClock(wall),
                        StaffRequested = request.StaffId is not null
                    }
                }, null, ct), ct);
            }
            catch (StudioException ex) when (ex.StatusCode == 409)
            {
                // That trainer cannot take the session after all; try the next one who is free.
                lastConflict = ex;
                continue;
            }

            var appointment = added.Appointment;
            if (appointment?.Id is null)
                throw new StudioException(502, "The studio system did not confirm the booking.");

            // The reply's own date-times are unreliable (offsets applied inconsistently), so report what was booked.
            var endTime = TimeZoneInfo.ConvertTime(start.AddMinutes(duration), zone);
            return new StudioBookingDto(
                appointment.Id.Value,
                "appointment",
                sessionType.Name,
                start,
                endTime,
                appointment.LocationId ?? availability.Location!.Id,
                appointment.StaffId ?? availability.Staff!.Id,
                "booked");
        }

        throw lastConflict ?? StudioException.Conflict("No trainer is free for that session at that time.");
    }

    private static bool HasOffset(string text)
    {
        var t = text.Trim();
        if (t.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
            return true;
        var tIndex = t.IndexOf('T');
        if (tIndex < 0)
            return false;
        var rest = t[(tIndex + 1)..];
        return rest.Contains('+') || rest.Contains('-');
    }

    private async Task<List<Availability>> FindAvailabilitiesAsync(AppointmentBookingRequest request, DateTime wall, int duration, TimeZoneInfo zone, CancellationToken ct)
    {
        var siteId = _gateway.SiteId;
        var dayStart = MindbodyGateway.FromWallClock(wall.Date, zone);
        var dayEnd = MindbodyGateway.FromWallClock(wall.Date.AddDays(1).AddSeconds(-1), zone);
        var end = wall.AddMinutes(duration);
        var matches = new List<Availability>();
        var offset = 0;
        while (true)
        {
            var currentOffset = offset;
            var page = await _gateway.CallAsync(token => _gateway.Client.AppointmentApi.GetBookableItems(new GetBookableItemsOperationRequest
            {
                SiteId = siteId,
                Authorization = token,
                RequestSessionTypeIds = new[] { request.SessionTypeId!.Value },
                RequestStartDate = dayStart,
                RequestEndDate = dayEnd,
                RequestStaffIds = request.StaffId is null ? null : new[] { request.StaffId.Value },
                RequestLocationIds = request.LocationId is null ? null : new[] { request.LocationId.Value },
                RequestLimit = PageSize,
                RequestOffset = currentOffset
            }, null, ct), ct);

            var items = page.Availabilities ?? Array.Empty<Availability>();
            matches.AddRange(items.Where(a =>
                a.StartDateTime is { } s && a.EndDateTime is { } e
                && s.DateTime <= wall && end <= e.DateTime
                && a.Staff?.Id is not null && a.Location?.Id is not null));

            offset += items.Count;
            if (items.Count == 0 || offset >= (page.PaginationResponse?.TotalResults ?? 0))
                break;
        }

        return matches
            .GroupBy(a => (a.Staff!.Id, a.Location!.Id))
            .Select(g => g.First())
            .ToList();
    }

    // ---------------------------------------------------------------- cancellation

    public Task<StudioCancellationDto> CancelClassBookingAsync(string? userName, string? bookingIdText, CancellationToken ct) =>
        CancelAsync(userName, bookingIdText, "class", ct);

    public Task<StudioCancellationDto> CancelAppointmentBookingAsync(string? userName, string? bookingIdText, CancellationToken ct) =>
        CancelAsync(userName, bookingIdText, "appointment", ct);

    private async Task<StudioCancellationDto> CancelAsync(string? userName, string? bookingIdText, string kind, CancellationToken ct)
    {
        if (!long.TryParse(bookingIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bookingId) || bookingId <= 0)
            throw StudioException.BadRequest("The booking id must be a number.");

        var shopper = await ResolveSignedInShopperAsync(userName);
        var zone = await _gateway.GetSiteTimeZoneAsync(ct);

        using var shopperLock = await LockAsync(shopper.Email, ct);

        var clientId = await FindClientIdAsync(shopper.Email, ct);
        if (clientId is null)
            throw StudioException.NotFound("You have no such booking.");

        var cancelledKey = $"studio-cancelled:{kind}:{clientId}:{bookingId}";
        var visit = (await GetScheduleAsync(clientId, zone, ct)).FirstOrDefault(v =>
            IsActive(v) && (kind == "class"
                ? v.ClassId is > 0 && v.Id == bookingId
                : v.AppointmentId is > 0 && v.AppointmentId == bookingId));

        if (visit is null)
        {
            // A repeated cancellation of a booking this shopper already cancelled is still a success.
            if (_cache.TryGetValue(cancelledKey, out _))
                return new StudioCancellationDto(bookingId, kind, "cancelled");
            throw StudioException.NotFound("You have no such booking.");
        }

        var siteId = _gateway.SiteId;
        try
        {
            if (kind == "class")
            {
                await _gateway.CallAsync(token => _gateway.Client.ClassApi.RemoveClientFromClass(new RemoveClientFromClassOperationRequest
                {
                    SiteId = siteId,
                    Authorization = token,
                    Body = new RemoveClientFromClassRequest { ClientId = clientId, ClassId = visit.ClassId!.Value, VisitId = (int)bookingId }
                }, null, ct), ct);
            }
            else
            {
                await _gateway.CallAsync(token => _gateway.Client.AppointmentApi.UpdateAppointment(new UpdateAppointmentOperationRequest
                {
                    SiteId = siteId,
                    Authorization = token,
                    Body = new UpdateAppointmentRequest { AppointmentId = bookingId, Execute = "cancel" }
                }, null, ct), ct);
            }
        }
        catch (StudioResponseUnreadableException)
        {
            // Mindbody carried the cancellation out but its reply could not be read; the schedule tells the truth.
            var stillThere = (await GetScheduleAsync(clientId, zone, ct)).Any(v =>
                IsActive(v) && (kind == "class" ? v.ClassId is > 0 && v.Id == bookingId : v.AppointmentId == bookingId));
            if (stillThere)
                throw;
        }

        _cache.Set(cancelledKey, true, CancelledMemory);
        return new StudioCancellationDto(bookingId, kind, "cancelled");
    }

    // ---------------------------------------------------------------- trainers

    public async Task<StudioTrainerListResponse> ListTrainersAsync(CancellationToken ct)
    {
        var siteId = _gateway.SiteId;
        var trainers = new List<StudioTrainerDto>();
        var seen = new HashSet<long>();
        var offset = 0;
        while (true)
        {
            var currentOffset = offset;
            var page = await _gateway.CallAsync(token => _gateway.Client.StaffApi.GetStaff(new GetStaffOperationRequest
            {
                SiteId = siteId,
                Authorization = token,
                RequestLimit = PageSize,
                RequestOffset = currentOffset
            }, null, ct), ct);

            var items = page.StaffMembers ?? Array.Empty<Staff>();
            foreach (var s in items)
            {
                if (s.Id is null || s.Active == false)
                    continue;
                var classes = s.ClassTeacher == true;
                var appointments = s.AppointmentInstructor == true;
                if (!classes && !appointments)
                    continue; // not a trainer (system and back-office accounts)
                if (!seen.Add(s.Id.Value))
                    continue;
                trainers.Add(new StudioTrainerDto(
                    s.Id.Value, s.FirstName, s.LastName, s.DisplayName ?? s.Name, s.Email,
                    s.MobilePhone, s.HomePhone, s.WorkPhone, s.Address, s.City, s.State, s.PostalCode, s.Country,
                    classes, appointments));
            }

            offset += items.Count;
            if (items.Count == 0 || offset >= (page.PaginationResponse?.TotalResults ?? 0))
                break;
        }

        return new StudioTrainerListResponse(trainers.OrderBy(t => t.LastName).ThenBy(t => t.FirstName).ThenBy(t => t.Id).ToList());
    }

    // ---------------------------------------------------------------- clients

    private async Task<Shopper> ResolveSignedInShopperAsync(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new StudioException(401, "Sign in to use studio bookings.");
        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
            throw new StudioException(401, "Your account was not found.");
        return ToShopper(user);
    }

    private static Shopper ToShopper(ApplicationUser user)
    {
        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email : user.UserName;
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw StudioException.Conflict("The shopper has no e-mail address to register with the studio.");
        return new Shopper(user.UserName ?? email, email.Trim());
    }

    private async Task<IDisposable> LockAsync(string key, CancellationToken ct)
    {
        var gate = ShopperLocks[(uint)StringComparer.OrdinalIgnoreCase.GetHashCode(key) % (uint)ShopperLocks.Length];
        await gate.WaitAsync(ct);
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }

    private async Task<string?> FindClientIdAsync(string email, CancellationToken ct)
    {
        var siteId = _gateway.SiteId;
        var offset = 0;
        while (true)
        {
            var currentOffset = offset;
            var page = await _gateway.CallAsync(token => _gateway.Client.ClientApi.GetClients(new GetClientsOperationRequest
            {
                SiteId = siteId,
                Authorization = token,
                RequestSearchText = email,
                RequestLimit = PageSize,
                RequestOffset = currentOffset
            }, null, ct), ct);

            var items = page.Clients ?? Array.Empty<ClientWithSuspensionInfo>();
            var match = items.FirstOrDefault(c =>
                c.Id is not null && c.Active != false
                && string.Equals(c.Email?.Trim(), email, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match.Id;

            offset += items.Count;
            if (items.Count == 0 || offset >= (page.PaginationResponse?.TotalResults ?? 0))
                return null;
        }
    }

    private async Task<string> CreateClientAsync(Shopper shopper, NewClientDetails d, CancellationToken ct)
    {
        var missing = new List<string>();
        if (d.HomeLocationId is null or <= 0) missing.Add("homeLocationId");
        if (string.IsNullOrWhiteSpace(d.StreetAddress)) missing.Add("streetAddress");
        if (string.IsNullOrWhiteSpace(d.City)) missing.Add("city");
        if (string.IsNullOrWhiteSpace(d.State)) missing.Add("state");
        if (string.IsNullOrWhiteSpace(d.PostalCode)) missing.Add("postalCode");
        if (string.IsNullOrWhiteSpace(d.DateOfBirth)) missing.Add("dateOfBirth");
        if (string.IsNullOrWhiteSpace(d.MobilePhone)) missing.Add("mobilePhone");
        if (string.IsNullOrWhiteSpace(d.ReferredBy)) missing.Add("referredBy");
        if (missing.Count > 0)
            throw StudioException.BadRequest(
                "This is the first booking for this shopper, so the studio needs these details to register them: "
                + string.Join(", ", missing) + ".");

        if (!DateOnly.TryParseExact(d.DateOfBirth!.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth)
            || birth.Year < 1900)
            throw StudioException.BadRequest("'dateOfBirth' must be a date in the form yyyy-MM-dd.");
        var zone = await _gateway.GetSiteTimeZoneAsync(ct);
        if (birth >= DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime))
            throw StudioException.BadRequest("'dateOfBirth' must be in the past.");

        var phone = d.MobilePhone!.Trim();
        if (phone.Count(char.IsDigit) < 7)
            throw StudioException.BadRequest("'mobilePhone' is not a valid phone number.");

        var siteId = _gateway.SiteId;

        var locations = await _gateway.CallAsync(token => _gateway.Client.SiteApi.GetLocations(new GetLocationsOperationRequest
        {
            SiteId = siteId,
            Authorization = token,
            RequestLimit = PageSize
        }, null, ct), ct);
        var location = locations.Locations?.FirstOrDefault(l => l.Id == d.HomeLocationId);
        if (location is null)
            throw StudioException.BadRequest($"There is no studio location {d.HomeLocationId}.");

        var referrals = await _gateway.CallAsync(token => _gateway.Client.ClientApi.GetClientReferralTypes(new GetClientReferralTypesOperationRequest
        {
            SiteId = siteId,
            Authorization = token
        }, null, ct), ct);
        var referral = referrals.ReferralTypes?.FirstOrDefault(r => string.Equals(r, d.ReferredBy!.Trim(), StringComparison.OrdinalIgnoreCase));
        if (referral is null)
            throw StudioException.BadRequest(
                "'referredBy' must be one of: " + string.Join(", ", referrals.ReferralTypes ?? Array.Empty<string>()) + ".");

        var localPart = shopper.Email[..shopper.Email.IndexOf('@')];
        var added = await _gateway.CallAsync(token => _gateway.Client.ClientApi.AddClient(new AddClientOperationRequest
        {
            SiteId = siteId,
            Authorization = token,
            Body = new AddClientRequest
            {
                FirstName = string.IsNullOrWhiteSpace(d.FirstName) ? localPart : d.FirstName.Trim(),
                LastName = string.IsNullOrWhiteSpace(d.LastName) ? "eShopOnWeb" : d.LastName.Trim(),
                Email = shopper.Email,
                AddressLine1 = d.StreetAddress!.Trim(),
                City = d.City!.Trim(),
                State = d.State!.Trim(),
                PostalCode = d.PostalCode!.Trim(),
                BirthDate = MindbodyGateway.ToMindbodyWallClock(birth.ToDateTime(TimeOnly.MinValue)),
                MobilePhone = phone,
                ReferredBy = referral,
                HomeLocation = new Location { Id = location.Id }
            }
        }, null, ct), ct);

        if (added.Client?.Id is null || (added.Errors is { Count: > 0 }))
            throw StudioException.Conflict(
                added.Errors?.FirstOrDefault()?.Message ?? "The studio system could not register the shopper.");

        return added.Client.Id;
    }

    // ---------------------------------------------------------------- schedule helpers

    private async Task<List<VisitWithWaitlistInfo>> GetScheduleAsync(string clientId, TimeZoneInfo zone, CancellationToken ct)
    {
        var siteId = _gateway.SiteId;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var start = MindbodyGateway.FromWallClock(today.AddDays(-1).ToDateTime(TimeOnly.MinValue), zone);
        var end = MindbodyGateway.FromWallClock(today.AddDays(MaxRangeDays * 2).ToDateTime(TimeOnly.MinValue), zone);
        var all = new List<VisitWithWaitlistInfo>();
        var offset = 0;
        while (true)
        {
            var currentOffset = offset;
            var page = await _gateway.CallAsync(token => _gateway.Client.ClientApi.GetClientSchedule(new GetClientScheduleOperationRequest
            {
                SiteId = siteId,
                Authorization = token,
                RequestClientId = clientId,
                RequestStartDate = start,
                RequestEndDate = end,
                RequestLimit = PageSize,
                RequestOffset = currentOffset
            }, null, ct), ct);

            var items = page.Visits ?? Array.Empty<VisitWithWaitlistInfo>();
            all.AddRange(items);
            offset += items.Count;
            if (items.Count == 0 || offset >= (page.PaginationResponse?.TotalResults ?? 0))
                break;
        }

        return all;
    }

    private static bool IsActive(VisitWithWaitlistInfo v)
    {
        var status = v.AppointmentStatus?.Value;
        return v.LateCancelled != true
               && !string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(status, "LateCancelled", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(status, "NoShow", StringComparison.OrdinalIgnoreCase);
    }

    private static StudioBookingDto ToBookingDto(VisitWithWaitlistInfo v, string kind, TimeZoneInfo zone)
    {
        var id = kind == "class" ? v.Id ?? 0 : v.AppointmentId ?? 0;
        var start = MindbodyGateway.ToSiteTime(v.StartDateTime ?? DateTimeOffset.UtcNow, zone);
        var end = MindbodyGateway.ToSiteTime(v.EndDateTime ?? v.StartDateTime ?? DateTimeOffset.UtcNow, zone);
        return new StudioBookingDto(id, kind, v.Name, start, end, v.LocationId, v.StaffId, "booked");
    }

    // ---------------------------------------------------------------- input helpers

    private static DateOnly? ParseDate(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var t = text.Trim();
        if (DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        // Also accept a full ISO-8601 date-time: its calendar date is what counts.
        if (t.Length >= 10 && DateOnly.TryParseExact(t[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
            && (t.Length == 10 || t[10] == 'T'))
            return d;
        throw StudioException.BadRequest($"'{name}' must be a date in the form yyyy-MM-dd.");
    }

    private static void ValidateRange(DateOnly from, DateOnly to)
    {
        if (to < from)
            throw StudioException.BadRequest("'to' must not be earlier than 'from'.");
        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
            throw StudioException.BadRequest($"The range may cover at most {MaxRangeDays} days.");
    }
}
