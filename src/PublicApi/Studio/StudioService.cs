using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;
using MindbodyPublicApi.Core.Exceptions;
using MindbodyPublicApi.Models;
using MindbodyPublicApi.Requests.AppointmentApi;
using MindbodyPublicApi.Requests.ClassApi;
using MindbodyPublicApi.Requests.ClientApi;
using MindbodyPublicApi.Requests.StaffApi;

namespace Microsoft.eShopWeb.PublicApi.Studio;

public class StudioService
{
    private const int MaxRangeDays = 731;
    private static readonly TimeSpan CancelledMemory = TimeSpan.FromDays(2);
    private static readonly Regex HasOffset = new(@"(Z|[+-]\d{2}:?\d{2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly MindbodyGateway _gateway;
    private readonly StudioReference _reference;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _time;

    public StudioService(MindbodyGateway gateway, StudioReference reference,
        UserManager<ApplicationUser> users, IMemoryCache cache)
    {
        _gateway = gateway;
        _reference = reference;
        _users = users;
        _cache = cache;
        _time = TimeProvider.System;
    }

    // ------------------------------------------------------------------ time helpers

    /// <summary>Mindbody schedule times are site-local wall-clock; the SDK labels them UTC. Re-read them in the site zone.</summary>
    private static DateTimeOffset Instant(DateTimeOffset sdkWall, TimeZoneInfo tz) => Instant(sdkWall.DateTime, tz);

    private static DateTimeOffset Instant(DateTime wall, TimeZoneInfo tz)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        TimeSpan offset;
        if (tz.IsInvalidTime(wall))
            offset = tz.BaseUtcOffset;
        else if (tz.IsAmbiguousTime(wall))
            offset = tz.GetAmbiguousTimeOffsets(wall).Max();
        else
            offset = tz.GetUtcOffset(wall);
        return new DateTimeOffset(wall, offset);
    }

    private static DateTimeOffset InSiteZone(DateTimeOffset instant, TimeZoneInfo tz) => TimeZoneInfo.ConvertTime(instant, tz);

    /// <summary>A moment safely inside the given site-local day. Mindbody reads range bounds by their local date.</summary>
    private static DateTimeOffset NoonOf(DateOnly day, TimeZoneInfo tz) =>
        Instant(day.ToDateTime(new TimeOnly(12, 0)), tz).ToUniversalTime();

    private async Task<(TimeZoneInfo Tz, DateTimeOffset Now)> SiteNowAsync()
    {
        var tz = await _reference.GetTimeZoneAsync().ConfigureAwait(false);
        return (tz, InSiteZone(_time.GetUtcNow(), tz));
    }

    private static DateOnly ParseDay(string? text, string field)
    {
        if (!DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new StudioException(400, $"'{field}' must be a date in the form yyyy-MM-dd.");
        return day;
    }

    private static void CheckRange(DateOnly from, DateOnly to)
    {
        if (to < from) throw new StudioException(400, "'to' must not be before 'from'.");
        if (to.DayNumber - from.DayNumber >= MaxRangeDays)
            throw new StudioException(400, $"The date range may not exceed {MaxRangeDays} days.");
    }

    // ------------------------------------------------------------------ 1. classes

    public async Task<List<ClassDto>> ListClassesAsync(string? fromText, string? toText)
    {
        var (tz, now) = await SiteNowAsync().ConfigureAwait(false);
        var today = DateOnly.FromDateTime(now.DateTime);

        var from = string.IsNullOrWhiteSpace(fromText) ? today : ParseDay(fromText, "from");
        var to = string.IsNullOrWhiteSpace(toText) ? from.AddDays(7) : ParseDay(toText, "to");
        CheckRange(from, to);

        var classes = await FetchClassesAsync(from, to, null, tz).ConfigureAwait(false);

        var windowStart = from.ToDateTime(TimeOnly.MinValue);
        var windowEnd = to.AddDays(1).ToDateTime(TimeOnly.MinValue);

        return classes
            .Where(c => c.StartDateTime is not null && c.EndDateTime is not null && c.Id is not null)
            .Where(c => c.StartDateTime!.Value.DateTime >= windowStart && c.StartDateTime.Value.DateTime < windowEnd)
            .Where(c => CanAttend(c, now, tz, out _))
            .OrderBy(c => c.StartDateTime!.Value.DateTime)
            .Select(c => ToDto(c, tz))
            .ToList();
    }

    private static ClassDto ToDto(Class c, TimeZoneInfo tz) => new()
    {
        Id = c.Id!.Value,
        Name = c.ClassDescription?.Name,
        Description = c.ClassDescription?.Description,
        StartTime = Instant(c.StartDateTime!.Value, tz),
        EndTime = Instant(c.EndDateTime!.Value, tz),
        LocationId = c.Location?.Id,
        LocationName = c.Location?.Name,
        Trainer = c.Staff?.DisplayName ?? c.Staff?.Name,
    };

    /// <summary>Whether a shopper could attend (and so book) this class right now; the reason when not.</summary>
    private static bool CanAttend(Class c, DateTimeOffset now, TimeZoneInfo tz, out string reason)
    {
        reason = "";
        if (c.IsCanceled == true) { reason = "The class has been cancelled."; return false; }
        if (c.Active == false) { reason = "The class is not active."; return false; }
        if (c.IsAvailable == false) { reason = "The class is not available for booking."; return false; }
        if (c.EndDateTime is { } end && Instant(end, tz) <= now) { reason = "The class has already finished."; return false; }

        if (c.BookingWindow is { } window)
        {
            if (window.StartDateTime is { } ws && Instant(ws, tz) > now)
            { reason = "The class is not open for booking yet."; return false; }
            if (window.EndDateTime is { } we && Instant(we, tz) < now)
            { reason = "Booking for the class has closed."; return false; }
        }

        if ((c.WebCapacity ?? c.MaxCapacity) is <= 0)
        { reason = "The class cannot be booked online."; return false; }

        var full = (c.WebCapacity > 0 && c.WebBooked >= c.WebCapacity)
                   || (c.MaxCapacity > 0 && c.TotalBooked >= c.MaxCapacity);
        if (full && c.IsWaitlistAvailable != true) { reason = "The class is full."; return false; }

        return true;
    }

    private Task<List<Class>> FetchClassesAsync(DateOnly from, DateOnly to, IReadOnlyList<int>? classIds, TimeZoneInfo tz)
    {
        var start = NoonOf(from, tz);
        var end = NoonOf(to, tz);
        return _gateway.CallAsync((client, token) => MindbodyGateway.ReadAllAsync(
            (offset, limit) => client.ClassApi.GetClasses(new GetClassesOperationRequest
            {
                SiteId = _gateway.SiteId,
                Authorization = token,
                RequestStartDateTime = start,
                RequestEndDateTime = end,
                RequestClassIds = classIds,
                RequestHideCanceledClasses = classIds is null ? true : null,
                RequestLimit = limit,
                RequestOffset = offset,
            }),
            page => page.Classes,
            page => page.PaginationResponse?.TotalResults));
    }

    private async Task<Class> FindClassAsync(int classId, TimeZoneInfo tz, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        var found = await FetchClassesAsync(today.AddDays(-400), today.AddDays(800), new[] { classId }, tz).ConfigureAwait(false);
        return found.FirstOrDefault(c => c.Id == classId)
               ?? throw new StudioException(404, $"There is no class with id {classId}.");
    }

    // ------------------------------------------------------------------ 2. session times

    public async Task<List<SlotDto>> ListSlotsAsync(int? sessionTypeId, string? fromText, string? toText)
    {
        if (sessionTypeId is null or <= 0) throw new StudioException(400, "'sessionTypeId' is required.");
        var from = ParseDay(fromText, "from");
        var to = ParseDay(toText, "to");
        CheckRange(from, to);

        var tz = await _reference.GetTimeZoneAsync().ConfigureAwait(false);
        var sessionType = await GetAppointmentTypeAsync(sessionTypeId.Value).ConfigureAwait(false);
        var length = SessionLength(sessionType);

        var rawTimes = await _gateway.CallAsync((client, token) => MindbodyGateway.ReadAllAsync(
            (offset, limit) => client.AppointmentApi.GetActiveSessionTimes(new GetActiveSessionTimesOperationRequest
            {
                SiteId = _gateway.SiteId,
                Authorization = token,
                RequestSessionTypeIds = new[] { sessionTypeId.Value },
                RequestLimit = limit,
                RequestOffset = offset,
            }),
            page => page.ActiveSessionTimes,
            page => page.PaginationResponse?.TotalResults)).ConfigureAwait(false);

        var times = rawTimes
            .Select(t => TimeSpan.TryParse(t, CultureInfo.InvariantCulture, out var ts) ? (TimeSpan?)ts : null)
            .Where(t => t is not null && t.Value >= TimeSpan.Zero && t.Value < TimeSpan.FromDays(1))
            .Select(t => t!.Value)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var slots = new List<SlotDto>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            foreach (var time in times)
            {
                var wall = day.ToDateTime(TimeOnly.MinValue) + time;
                if (tz.IsInvalidTime(wall)) continue; // this wall-clock time does not exist on a daylight-saving jump
                var start = Instant(wall, tz);
                slots.Add(new SlotDto { StartTime = start, EndTime = InSiteZone(start.AddMinutes(length), tz) });
            }
        }
        return slots;
    }

    private async Task<SessionType> GetAppointmentTypeAsync(int sessionTypeId)
    {
        var types = await _reference.GetSessionTypesAsync().ConfigureAwait(false);
        var type = types.FirstOrDefault(t => t.Id == sessionTypeId)
                   ?? throw new StudioException(404, $"There is no session type with id {sessionTypeId}.");
        if (type.Type?.Value != "Appointment")
            throw new StudioException(422, $"Session type {sessionTypeId} is not a one-to-one session type.");
        return type;
    }

    private static int SessionLength(SessionType type)
    {
        var minutes = type.DefaultTimeLength ?? type.StaffTimeLength;
        if (minutes is null or <= 0)
            throw new StudioException(422, $"Session type {type.Id} has no set length, so its end times are unknown.");
        return minutes.Value;
    }

    // ------------------------------------------------------------------ 3. booking

    public async Task<BookingDto> BookClassAsync(string userName, int? classId, NewClientDetails details)
    {
        if (classId is null or <= 0) throw new StudioException(400, "'classId' is required.");
        return await BookClassCoreAsync(userName, classId.Value, details, forStaff: false).ConfigureAwait(false);
    }

    public async Task<BookingDto> BookClassForShopperAsync(string? shopper, int? classId, NewClientDetails details)
    {
        if (string.IsNullOrWhiteSpace(shopper)) throw new StudioException(400, "'shopper' is required.");
        if (classId is null or <= 0) throw new StudioException(400, "'classId' is required.");

        var user = await _users.FindByNameAsync(shopper.Trim()).ConfigureAwait(false)
                   ?? await _users.FindByEmailAsync(shopper.Trim()).ConfigureAwait(false)
                   ?? throw new StudioException(404, $"There is no shopper '{shopper}'.");
        return await BookClassCoreAsync(user.UserName!, classId.Value, details, forStaff: true).ConfigureAwait(false);
    }

    private async Task<BookingDto> BookClassCoreAsync(string userName, int classId, NewClientDetails details, bool forStaff)
    {
        var (tz, now) = await SiteNowAsync().ConfigureAwait(false);
        var cls = await FindClassAsync(classId, tz, now).ConfigureAwait(false);

        if (forStaff)
        {
            // Front desk may place a shopper in any timetabled class; only a class that cannot run is refused.
            if (cls.IsCanceled == true) throw new StudioException(409, "The class has been cancelled.");
            if (cls.Active == false) throw new StudioException(409, "The class is not active.");
        }
        else if (!CanAttend(cls, now, tz, out var reason))
        {
            throw new StudioException(409, reason);
        }

        var clientId = await EnsureClientAsync(userName, details, tz, now).ConfigureAwait(false);

        using (await _reference.Locks.AcquireAsync(clientId).ConfigureAwait(false))
        {
            var classDay = DateOnly.FromDateTime(cls.StartDateTime!.Value.DateTime);
            var visits = await ListVisitsAsync(clientId, classDay.AddDays(-1), classDay.AddDays(1), tz).ConfigureAwait(false);
            var existing = visits.FirstOrDefault(v => v.ClassId == classId && IsLive(v));
            if (existing is not null)
                return ClassBooking(existing.Id ?? 0, cls, tz); // a retried request: already done

            var response = await _gateway.CallAsync((client, token) => client.ClassApi.AddClientToClass(
                new AddClientToClassOperationRequest
                {
                    SiteId = _gateway.SiteId,
                    Authorization = token,
                    Body = new AddClientToClassRequest { ClientId = clientId, ClassId = classId, SendEmail = false },
                })).ConfigureAwait(false);

            var visitId = response.Visit?.Id
                          ?? throw new StudioException(502, "The studio service did not confirm the booking.");
            return ClassBooking(visitId, cls, tz);
        }
    }

    private static BookingDto ClassBooking(long bookingId, Class cls, TimeZoneInfo tz) => new()
    {
        BookingId = bookingId,
        Kind = "class",
        Name = cls.ClassDescription?.Name,
        ClassId = cls.Id,
        TrainerId = cls.Staff?.Id,
        LocationId = cls.Location?.Id,
        StartTime = Instant(cls.StartDateTime!.Value, tz),
        EndTime = Instant(cls.EndDateTime!.Value, tz),
    };

    private static bool IsLive(Visit v) =>
        v.LateCancelled != true && v.AppointmentStatus?.Value is not ("Cancelled" or "LateCancelled");

    public async Task<BookingDto> BookAppointmentAsync(string userName, int? sessionTypeId, string? startTimeText,
        long? staffId, NewClientDetails details)
    {
        if (sessionTypeId is null or <= 0) throw new StudioException(400, "'sessionTypeId' is required.");
        if (string.IsNullOrWhiteSpace(startTimeText) || !HasOffset.IsMatch(startTimeText.Trim()))
            throw new StudioException(400, "'startTime' is required, as an ISO-8601 date-time with its UTC offset.");
        if (!DateTimeOffset.TryParse(startTimeText.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var requested))
            throw new StudioException(400, "'startTime' is not a valid ISO-8601 date-time.");

        var (tz, now) = await SiteNowAsync().ConfigureAwait(false);
        var start = InSiteZone(requested, tz);
        if (start <= now) throw new StudioException(409, "That session time has already passed.");

        var sessionType = await GetAppointmentTypeAsync(sessionTypeId.Value).ConfigureAwait(false);
        var length = SessionLength(sessionType);
        var end = InSiteZone(start.AddMinutes(length), tz);

        var clientId = await EnsureClientAsync(userName, details, tz, now).ConfigureAwait(false);

        using (await _reference.Locks.AcquireAsync(clientId).ConfigureAwait(false))
        {
            var day = DateOnly.FromDateTime(start.DateTime);
            var mine = await ListAppointmentsAsync(clientId, day.AddDays(-1), day.AddDays(1), tz).ConfigureAwait(false);
            var existing = mine.FirstOrDefault(a => a.SessionTypeId == sessionTypeId
                                                    && a.StartDateTime is not null
                                                    && Instant(a.StartDateTime.Value, tz) == start
                                                    && (staffId is null || a.StaffId == staffId));
            if (existing is not null)
                return AppointmentBooking(existing, sessionType, start, end); // a retried request: already done

            var candidates = await FindTrainersAsync(sessionTypeId.Value, start, start.AddMinutes(length), staffId, tz)
                .ConfigureAwait(false);
            if (candidates.Count == 0)
                throw new StudioException(409, staffId is null
                    ? "No trainer is free for that session at that time."
                    : "That trainer is not free for that session at that time.");

            StudioException? last = null;
            foreach (var slot in candidates)
            {
                try
                {
                    var response = await _gateway.CallAsync((client, token) => client.AppointmentApi.AddAppointment(
                        new AddAppointmentOperationRequest
                        {
                            SiteId = _gateway.SiteId,
                            Authorization = token,
                            Body = new AddAppointmentRequest
                            {
                                ClientId = clientId,
                                SessionTypeId = sessionTypeId,
                                StaffId = slot.Staff,
                                LocationId = slot.Location,
                                // Mindbody reads a body date-time as the studio's wall-clock time, and the SDK writes
                                // the offset-zero value as given: so send the studio's wall-clock time labelled UTC.
                                StartDateTime = new DateTimeOffset(start.DateTime, TimeSpan.Zero),
                                SendEmail = false,
                            },
                        })).ConfigureAwait(false);

                    var appointment = response.Appointment
                                      ?? throw new StudioException(502, "The studio service did not confirm the booking.");
                    return AppointmentBooking(appointment, sessionType, start, end);
                }
                catch (StudioException ex) when (ex.StatusCode == 422)
                {
                    last = ex; // this trainer/location did not work out: try the next one
                }
            }
            throw new StudioException(409, last?.Message ?? "The session could not be booked.", last);
        }
    }

    private async Task<List<(long Staff, int Location)>> FindTrainersAsync(int sessionTypeId, DateTimeOffset start,
        DateTimeOffset end, long? staffId, TimeZoneInfo tz)
    {
        var day = DateOnly.FromDateTime(start.DateTime);
        var noon = NoonOf(day, tz);
        var items = await _gateway.CallAsync((client, token) => MindbodyGateway.ReadAllAsync(
            (offset, limit) => client.AppointmentApi.GetBookableItems(new GetBookableItemsOperationRequest
            {
                SiteId = _gateway.SiteId,
                Authorization = token,
                RequestSessionTypeIds = new[] { sessionTypeId },
                RequestStartDate = noon,
                RequestEndDate = noon,
                RequestStaffIds = staffId is null ? null : new[] { staffId.Value },
                RequestLimit = limit,
                RequestOffset = offset,
            }),
            page => page.Availabilities,
            page => page.PaginationResponse?.TotalResults)).ConfigureAwait(false);

        return items
            .Where(a => a.Staff?.Id is not null && a.Location?.Id is not null
                        && a.StartDateTime is not null && a.EndDateTime is not null
                        && Instant(a.StartDateTime.Value, tz) <= start
                        && Instant(a.EndDateTime.Value, tz) >= end)
            .Select(a => (a.Staff!.Id!.Value, a.Location!.Id!.Value))
            .Distinct()
            .ToList();
    }

    private static BookingDto AppointmentBooking(Appointment a, SessionType type, DateTimeOffset start, DateTimeOffset end)
        => new()
    {
        BookingId = a.Id ?? 0,
        Kind = "appointment",
        Name = type.Name,
        SessionTypeId = type.Id,
        TrainerId = a.StaffId,
        LocationId = a.LocationId,
        // The session starts when the shopper asked (an existing booking was matched on exactly that instant).
        StartTime = start,
        EndTime = end,
    };

    // ------------------------------------------------------------------ 4. cancelling

    public Task<CancelledBookingDto> CancelClassBookingAsync(string userName, long bookingId) =>
        CancelAsync(userName, bookingId, "class", async (clientId, tz) =>
        {
            var today = DateOnly.FromDateTime(InSiteZone(_time.GetUtcNow(), tz).DateTime);
            var visits = await ListVisitsAsync(clientId, today.AddDays(-1), today.AddDays(400), tz).ConfigureAwait(false);
            var visit = visits.FirstOrDefault(v => v.Id == bookingId && v.ClassId is > 0 && IsLive(v));
            if (visit is null) return false;

            await PerformAsync(
                () => _gateway.CallAsync((client, token) => client.ClassApi.RemoveClientFromClass(
                    new RemoveClientFromClassOperationRequest
                    {
                        SiteId = _gateway.SiteId,
                        Authorization = token,
                        Body = new RemoveClientFromClassRequest
                        {
                            ClientId = clientId,
                            ClassId = visit.ClassId!.Value,
                            VisitId = (int)bookingId,
                            SendEmail = false,
                        },
                    })),
                async () => (await ListVisitsAsync(clientId, today.AddDays(-1), today.AddDays(400), tz).ConfigureAwait(false))
                    .Any(v => v.Id == bookingId && IsLive(v))).ConfigureAwait(false);
            return true;
        });

    public Task<CancelledBookingDto> CancelAppointmentBookingAsync(string userName, long bookingId) =>
        CancelAsync(userName, bookingId, "appointment", async (clientId, tz) =>
        {
            var today = DateOnly.FromDateTime(InSiteZone(_time.GetUtcNow(), tz).DateTime);
            var mine = await ListAppointmentsAsync(clientId, today.AddDays(-1), today.AddDays(400), tz).ConfigureAwait(false);
            var appointment = mine.FirstOrDefault(a => a.Id == bookingId);
            if (appointment is null) return false;

            await PerformAsync(
                () => _gateway.CallAsync((client, token) => client.AppointmentApi.UpdateAppointment(
                    new UpdateAppointmentOperationRequest
                    {
                        SiteId = _gateway.SiteId,
                        Authorization = token,
                        Body = new UpdateAppointmentRequest { AppointmentId = bookingId, Execute = "Cancel", SendEmail = false },
                    })),
                async () => (await ListAppointmentsAsync(clientId, today.AddDays(-1), today.AddDays(400), tz).ConfigureAwait(false))
                    .Any(a => a.Id == bookingId)).ConfigureAwait(false);
            return true;
        });

    /// <summary>
    /// Runs a change at Mindbody. Some of its answers to successful changes cannot be read by the SDK
    /// (empty 0001-01-01 dates); the change has then been made, so confirm the outcome instead of failing.
    /// </summary>
    private static async Task PerformAsync(Func<Task> change, Func<Task<bool>> changeMissing)
    {
        try
        {
            await change().ConfigureAwait(false);
        }
        catch (StudioException ex) when (ex.InnerException is ResponseDeserializationException
                                         { StatusCode: >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices })
        {
            if (await changeMissing().ConfigureAwait(false)) throw;
        }
    }

    private async Task<CancelledBookingDto> CancelAsync(string userName, long bookingId, string kind,
        Func<string, TimeZoneInfo, Task<bool>> cancel)
    {
        if (bookingId <= 0) throw new StudioException(404, "There is no such booking.");

        var tz = await _reference.GetTimeZoneAsync().ConfigureAwait(false);
        var clientId = await FindClientAsync(userName).ConfigureAwait(false);
        var memoryKey = $"studio-cancelled:{kind}:{clientId}:{bookingId}";

        if (clientId is null)
            throw new StudioException(404, "There is no such booking.");

        using (await _reference.Locks.AcquireAsync(clientId).ConfigureAwait(false))
        {
            if (await cancel(clientId, tz).ConfigureAwait(false))
            {
                _cache.Set(memoryKey, true, CancelledMemory);
                return new CancelledBookingDto { BookingId = bookingId };
            }

            // Gone already. If this very booking was cancelled moments ago, the caller is retrying: answer as before.
            if (_cache.TryGetValue(memoryKey, out _))
                return new CancelledBookingDto { BookingId = bookingId };

            // Not theirs, or never existed: do not say which.
            throw new StudioException(404, "There is no such booking.");
        }
    }

    // ------------------------------------------------------------------ 5. trainers

    public async Task<List<TrainerDto>> ListTrainersAsync()
    {
        var staff = await _gateway.CallAsync((client, token) => MindbodyGateway.ReadAllAsync(
            (offset, limit) => client.StaffApi.GetStaff(new GetStaffOperationRequest
            {
                SiteId = _gateway.SiteId,
                Authorization = token,
                RequestLimit = limit,
                RequestOffset = offset,
            }),
            page => page.StaffMembers,
            page => page.PaginationResponse?.TotalResults)).ConfigureAwait(false);

        return staff
            .Where(s => s.Id is > 0 && (s.ClassTeacher == true || s.AppointmentInstructor == true))
            .GroupBy(s => s.Id!.Value)
            .Select(g => g.First())
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ThenBy(s => s.Id)
            .Select(s => new TrainerDto
            {
                Id = s.Id!.Value,
                FirstName = s.FirstName,
                LastName = s.LastName,
                DisplayName = s.DisplayName ?? s.Name,
                Active = s.Active ?? false,
                Email = s.Email,
                MobilePhone = s.MobilePhone,
                HomePhone = s.HomePhone,
                WorkPhone = s.WorkPhone,
                Address = s.Address,
                City = s.City,
                State = s.State,
                PostalCode = s.PostalCode,
                Country = s.Country,
                TeachesClasses = s.ClassTeacher ?? false,
                TakesAppointments = s.AppointmentInstructor ?? false,
            })
            .ToList();
    }

    // ------------------------------------------------------------------ clients

    private async Task<string?> EmailOfAsync(string userName)
    {
        var user = await _users.FindByNameAsync(userName).ConfigureAwait(false);
        var email = user?.Email;
        if (string.IsNullOrWhiteSpace(email) && userName.Contains('@')) email = userName;
        return string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }

    private async Task<string?> FindClientAsync(string userName)
    {
        var email = await EmailOfAsync(userName).ConfigureAwait(false);
        return email is null ? null : await FindClientByEmailAsync(email).ConfigureAwait(false);
    }

    private async Task<string?> FindClientByEmailAsync(string email)
    {
        var clients = await _gateway.CallAsync((client, token) => MindbodyGateway.ReadAllAsync(
            (offset, limit) => client.ClientApi.GetClients(new GetClientsOperationRequest
            {
                SiteId = _gateway.SiteId,
                Authorization = token,
                RequestSearchText = email,
                RequestIncludeInactive = true,
                RequestLimit = limit,
                RequestOffset = offset,
            }),
            page => page.Clients,
            page => page.PaginationResponse?.TotalResults)).ConfigureAwait(false);

        return clients
            .Where(c => string.Equals(c.Email?.Trim(), email, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(c.Id))
            .OrderByDescending(c => c.Active == true)
            .Select(c => c.Id)
            .FirstOrDefault();
    }

    /// <summary>The studio client for an eShop identity; created (once) from the first booking's details if absent.</summary>
    private async Task<string> EnsureClientAsync(string userName, NewClientDetails details, TimeZoneInfo tz, DateTimeOffset now)
    {
        var email = await EmailOfAsync(userName).ConfigureAwait(false)
                    ?? throw new StudioException(422, "Your account has no email address, so it cannot be registered with the studio.");

        using (await _reference.Locks.AcquireAsync("client:" + email).ConfigureAwait(false))
        {
            var existing = await FindClientByEmailAsync(email).ConfigureAwait(false);
            if (existing is not null) return existing;

            var request = await BuildNewClientAsync(email, details, tz, now).ConfigureAwait(false);
            var response = await _gateway.CallAsync((client, token) => client.ClientApi.AddClient(
                new AddClientOperationRequest { SiteId = _gateway.SiteId, Authorization = token, Body = request })).ConfigureAwait(false);

            return response.Client?.Id
                   ?? throw new StudioException(502, "The studio service did not confirm the new client.");
        }
    }

    private async Task<AddClientRequest> BuildNewClientAsync(string email, NewClientDetails d, TimeZoneInfo tz, DateTimeOffset now)
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
            throw new StudioException(400,
                "This is the first booking for this account, so the studio needs more details to register the client. Missing: "
                + string.Join(", ", missing) + ".");

        if (!DateOnly.TryParseExact(d.DateOfBirth!.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth))
            throw new StudioException(400, "'dateOfBirth' must be a date in the form yyyy-MM-dd.");
        var today = DateOnly.FromDateTime(now.DateTime);
        if (birth >= today || birth.Year < 1900)
            throw new StudioException(400, "'dateOfBirth' is not a plausible date of birth.");

        var locations = await _reference.GetLocationsAsync().ConfigureAwait(false);
        var home = locations.FirstOrDefault(l => l.Id == d.HomeLocationId)
                   ?? throw new StudioException(400, $"There is no studio location with id {d.HomeLocationId}.");

        var referrals = await _reference.GetReferralTypesAsync().ConfigureAwait(false);
        var referral = referrals.FirstOrDefault(r => string.Equals(r, d.ReferredBy!.Trim(), StringComparison.OrdinalIgnoreCase))
                       ?? throw new StudioException(400,
                           "'referredBy' must be one of: " + string.Join(", ", referrals) + ".");

        var (first, last) = NamesFor(email, d);
        return new AddClientRequest
        {
            FirstName = first,
            LastName = last,
            Email = email,
            AddressLine1 = d.StreetAddress!.Trim(),
            City = d.City!.Trim(),
            State = d.State!.Trim(),
            PostalCode = d.PostalCode!.Trim(),
            // A date of birth is a calendar date: send it at midday so no zone shift can move it to another day.
            BirthDate = new DateTimeOffset(birth.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
            MobilePhone = d.MobilePhone!.Trim(),
            ReferredBy = referral,
            HomeLocation = new Location { Id = home.Id },
        };
    }

    private static (string First, string Last) NamesFor(string email, NewClientDetails d)
    {
        if (!string.IsNullOrWhiteSpace(d.FirstName) && !string.IsNullOrWhiteSpace(d.LastName))
            return (d.FirstName.Trim(), d.LastName.Trim());

        var local = email.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        var first = !string.IsNullOrWhiteSpace(d.FirstName) ? d.FirstName.Trim() : Cap(parts.FirstOrDefault() ?? local);
        var last = !string.IsNullOrWhiteSpace(d.LastName) ? d.LastName.Trim()
            : parts.Length > 1 ? Cap(parts[^1]) : "Shopper";
        return (first, last);
    }

    private Task<List<Visit>> ListVisitsAsync(string clientId, DateOnly from, DateOnly to, TimeZoneInfo tz)
    {
        var start = NoonOf(from, tz);
        var end = NoonOf(to, tz);
        return _gateway.CallAsync((client, token) => MindbodyGateway.ReadAllAsync(
            (offset, limit) => client.ClientApi.GetClientVisits(new GetClientVisitsOperationRequest
            {
                SiteId = _gateway.SiteId,
                Authorization = token,
                RequestClientId = clientId,
                RequestStartDate = start,
                RequestEndDate = end,
                RequestLimit = limit,
                RequestOffset = offset,
            }),
            page => page.Visits,
            page => page.PaginationResponse?.TotalResults));
    }

    private async Task<List<Appointment>> ListAppointmentsAsync(string clientId, DateOnly from, DateOnly to, TimeZoneInfo tz)
    {
        var start = NoonOf(from, tz);
        var end = NoonOf(to, tz);
        var all = await _gateway.CallAsync((client, token) => MindbodyGateway.ReadAllAsync(
            (offset, limit) => client.AppointmentApi.GetStaffAppointments(new GetStaffAppointmentsOperationRequest
            {
                SiteId = _gateway.SiteId,
                Authorization = token,
                RequestClientId = clientId,
                RequestStartDate = start,
                RequestEndDate = end,
                RequestLimit = limit,
                RequestOffset = offset,
            }),
            page => page.Appointments,
            page => page.PaginationResponse?.TotalResults)).ConfigureAwait(false);

        return all
            .Where(a => string.Equals(a.ClientId, clientId, StringComparison.Ordinal)
                        && a.Status?.Value is not ("Cancelled" or "LateCancelled" or "NoShow"))
            .ToList();
    }
}
