using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using MindbodyPublicApi.Models;
using MindbodyPublicApi.Models.Enums;
using MindbodyPublicApi.Requests.AppointmentApi;
using MindbodyPublicApi.Requests.ClassApi;
using MindbodyPublicApi.Requests.ClientApi;
using MindbodyPublicApi.Requests.SiteApi;
using MindbodyPublicApi.Requests.StaffApi;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// The studio features of the public API, carried out against Mindbody through the SDK.
/// </summary>
public class StudioService
{
    private const int PageSize = 200;
    private const int MaxClassRangeDays = 92;
    private const int MaxSlotRangeDays = 62;
    private const int DefaultClassDays = 7;
    private static readonly TimeSpan CancelledMemory = TimeSpan.FromHours(24);

    // Serialises work per studio client so that a repeated request (the mobile app resends when
    // it gets no answer in time) never creates a second client or a second booking.
    private static readonly SemaphoreSlim[] ClientLocks =
        Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly MindbodyGateway _gateway;
    private readonly IMemoryCache _cache;

    public StudioService(MindbodyGateway gateway, IMemoryCache cache)
    {
        _gateway = gateway;
        _cache = cache;
    }

    // ------------------------------------------------------------------ classes

    public async Task<List<StudioClassDto>> GetClassesAsync(string? from, string? to, CancellationToken ct)
    {
        var tz = await _gateway.GetSiteZoneAsync(ct);
        var today = Today(tz);

        var fromDate = ParseDate(from, "from");
        var toDate = ParseDate(to, "to");
        fromDate ??= today;
        toDate ??= fromDate.Value.AddDays(DefaultClassDays);
        if (toDate < fromDate)
            throw StudioException.BadRequest("'to' must not be before 'from'.");
        if (toDate.Value.DayNumber - fromDate.Value.DayNumber > MaxClassRangeDays)
            throw StudioException.BadRequest($"The date range may not be longer than {MaxClassRangeDays} days.");

        var classes = await FetchClassesAsync(LocalMidnight(fromDate.Value, tz), LocalMidnight(toDate.Value.AddDays(1), tz),
            null, true, ct);

        var now = DateTimeOffset.UtcNow;
        return classes
            .Where(c => c.StartDateTime.HasValue)
            .Select(c => (Class: c, Start: ToSiteTime(c.StartDateTime!.Value, tz)))
            .Where(x => DateOnly.FromDateTime(x.Start.DateTime) >= fromDate && DateOnly.FromDateTime(x.Start.DateTime) <= toDate)
            .Where(x => WhyNotAttendable(x.Class, now, tz) == null)
            .OrderBy(x => x.Start.UtcDateTime)
            .ThenBy(x => x.Class.Id)
            .Select(x => ToDto(x.Class, tz))
            .ToList();
    }

    private async Task<List<Class>> FetchClassesAsync(DateTimeOffset start, DateTimeOffset end, int? classId,
        bool hideCanceled, CancellationToken ct)
    {
        var all = await PageAllAsync<Class>(async offset =>
        {
            var response = await _gateway.CallAsync(token => _gateway.Client.ClassApi.GetClasses(
                new GetClassesOperationRequest
                {
                    SiteId = _gateway.SiteId,
                    Authorization = token,
                    RequestStartDateTime = start,
                    RequestEndDateTime = end,
                    RequestClassIds = classId.HasValue ? new[] { classId.Value } : null,
                    RequestHideCanceledClasses = hideCanceled,
                    RequestLimit = PageSize,
                    RequestOffset = offset
                }, null, ct), ct);
            return (response.Classes, response.PaginationResponse?.TotalResults);
        });
        return all.GroupBy(c => c.Id).Select(g => g.First()).ToList();
    }

    /// <summary>Returns why a shopper cannot attend the class, or null when they can.</summary>
    private static string? WhyNotAttendable(Class c, DateTimeOffset now, TimeZoneInfo tz)
    {
        if (c.IsCanceled == true)
            return "This class has been cancelled.";
        if (c.Active == false)
            return "This class is not active.";
        if (c.ClassDescription?.Program?.ScheduleType == ScheduleType.Enrollment)
            return "This is an enrollment, not a drop-in class.";
        if (c.StartDateTime is null || ToSiteTime(c.StartDateTime.Value, tz) <= now)
            return "This class has already started.";
        if (c.IsAvailable == false)
            return "This class is not available for booking.";
        if (c.WebCapacity is <= 0)
            return "This class cannot be booked online.";
        if (c.WebCapacity is > 0 && c.WebBooked >= c.WebCapacity)
            return "This class is full.";
        var window = c.BookingWindow;
        if (window?.StartDateTime is { } windowStart && ToSiteTime(windowStart, tz) > now)
            return "Booking for this class has not opened yet.";
        if (window?.EndDateTime is { } windowEnd && ToSiteTime(windowEnd, tz) < now)
            return "Booking for this class has closed.";
        return null;
    }

    private static StudioClassDto ToDto(Class c, TimeZoneInfo tz)
    {
        var start = ToSiteTime(c.StartDateTime!.Value, tz);
        var end = c.EndDateTime.HasValue ? ToSiteTime(c.EndDateTime.Value, tz) : start;
        return new StudioClassDto
        {
            Id = c.Id ?? 0,
            Name = c.ClassDescription?.Name,
            Description = c.ClassDescription?.Description,
            StartTime = start,
            EndTime = end,
            LocationId = c.Location?.Id,
            LocationName = c.Location?.Name,
            TrainerName = c.Staff?.DisplayName ?? c.Staff?.Name,
            SpotsLeft = c.WebCapacity.HasValue ? Math.Max(0, c.WebCapacity.Value - (c.WebBooked ?? 0)) : null
        };
    }

    private async Task<Class> GetClassByIdAsync(int classId, TimeZoneInfo tz, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var found = await FetchClassesAsync(now.AddDays(-30), now.AddDays(3 * 366), classId, false, ct);
        return found.FirstOrDefault(c => c.Id == classId)
               ?? throw StudioException.NotFound($"There is no class with id {classId}.");
    }

    // ------------------------------------------------------------------ session times

    public async Task<List<StudioSlotDto>> GetSlotsAsync(string? sessionType, string? from, string? to, CancellationToken ct)
    {
        if (!int.TryParse(sessionType, NumberStyles.None, CultureInfo.InvariantCulture, out var sessionTypeId) || sessionTypeId <= 0)
            throw StudioException.BadRequest("'sessionType' is required and must be a session type id.");
        var fromDate = ParseDate(from, "from") ?? throw StudioException.BadRequest("'from' is required (yyyy-MM-dd).");
        var toDate = ParseDate(to, "to") ?? throw StudioException.BadRequest("'to' is required (yyyy-MM-dd).");
        if (toDate < fromDate)
            throw StudioException.BadRequest("'to' must not be before 'from'.");
        if (toDate.DayNumber - fromDate.DayNumber >= MaxSlotRangeDays)
            throw StudioException.BadRequest($"The date range may not be longer than {MaxSlotRangeDays} days.");

        var tz = await _gateway.GetSiteZoneAsync(ct);
        var type = await GetSessionTypeAsync(sessionTypeId, ct);
        var minutes = type.DefaultTimeLength ?? type.StaffTimeLength;
        if (minutes is null or <= 0)
            throw StudioException.Conflict("This session type has no set length, so its times cannot be listed.");

        var response = await _gateway.CallAsync(token => _gateway.Client.AppointmentApi.GetActiveSessionTimes(
            new GetActiveSessionTimesOperationRequest
            {
                SiteId = _gateway.SiteId,
                Authorization = token,
                RequestSessionTypeIds = new[] { sessionTypeId },
                RequestLimit = PageSize
            }, null, ct), ct);

        var rawTimes = response.SessionTypeActiveSessionTimes?.FirstOrDefault(s => s.SessionTypeId == sessionTypeId)?.ActiveSessionTimes
                       ?? response.ActiveSessionTimes
                       ?? Array.Empty<string>();
        var times = rawTimes
            .Select(t => TimeOnly.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : (TimeOnly?)null)
            .Where(t => t.HasValue).Select(t => t!.Value)
            .Distinct().OrderBy(t => t).ToList();

        var slots = new List<StudioSlotDto>();
        for (var day = fromDate; day <= toDate; day = day.AddDays(1))
        {
            foreach (var time in times)
            {
                var wall = day.ToDateTime(time);
                if (tz.IsInvalidTime(wall))
                    continue; // skipped by a daylight-saving change: this time does not exist
                var start = new DateTimeOffset(wall, tz.GetUtcOffset(wall));
                var end = TimeZoneInfo.ConvertTime(start.AddMinutes(minutes.Value), tz);
                slots.Add(new StudioSlotDto { SessionTypeId = sessionTypeId, StartTime = start, EndTime = end });
            }
        }
        return slots;
    }

    private async Task<SessionType> GetSessionTypeAsync(int sessionTypeId, CancellationToken ct)
    {
        var all = await PageAllAsync<SessionType>(async offset =>
        {
            var response = await _gateway.CallAsync(token => _gateway.Client.SiteApi.GetSessionTypes(
                new GetSessionTypesOperationRequest
                {
                    SiteId = _gateway.SiteId,
                    Authorization = token,
                    RequestLimit = PageSize,
                    RequestOffset = offset
                }, null, ct), ct);
            return (response.SessionTypes, response.PaginationResponse?.TotalResults);
        });
        var type = all.FirstOrDefault(t => t.Id == sessionTypeId)
                   ?? throw StudioException.NotFound($"There is no session type with id {sessionTypeId}.");
        if (type.Type == Type1.Class || type.Type == Type1.Enrollment)
            throw StudioException.BadRequest("That session type is a class or enrollment, not a one-to-one session.");
        return type;
    }

    // ------------------------------------------------------------------ booking

    public async Task<(StudioBookingDto Booking, bool Created)> BookClassAsync(
        string userName, int classId, NewClientDetails? details, CancellationToken ct)
    {
        var tz = await _gateway.GetSiteZoneAsync(ct);
        var cls = await GetClassByIdAsync(classId, tz, ct);

        return await WithClientLockAsync(userName, async () =>
        {
            var clientId = await EnsureClientAsync(userName, details, true, ct);

            var existing = (await GetClientVisitsAsync(clientId, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(3 * 366), ct))
                .FirstOrDefault(v => v.ClassId == classId && IsActive(v));
            if (existing != null)
                return (ClassBooking(existing, cls, tz, true), false);

            var why = WhyNotAttendable(cls, DateTimeOffset.UtcNow, tz);
            if (why != null)
                throw StudioException.Conflict(why);

            var visitId = await AddToClassAsync(clientId, classId, ct);
            return (ClassBooking(visitId, cls, tz, false), true);
        }, ct);
    }

    public async Task<(StudioBookingDto Booking, bool Created)> AdminBookClassAsync(
        string shopper, int classId, CancellationToken ct)
    {
        var tz = await _gateway.GetSiteZoneAsync(ct);
        var cls = await GetClassByIdAsync(classId, tz, ct);

        return await WithClientLockAsync(shopper, async () =>
        {
            var clientId = await EnsureClientAsync(shopper, null, false, ct);

            var existing = (await GetClientVisitsAsync(clientId, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(3 * 366), ct))
                .FirstOrDefault(v => v.ClassId == classId && IsActive(v));
            if (existing != null)
                return (ClassBooking(existing, cls, tz, true), false);

            // Front-desk staff may book any class of the timetable, including ones closed to online booking.
            if (cls.IsCanceled == true)
                throw StudioException.Conflict("This class has been cancelled.");
            if (cls.StartDateTime is null || ToSiteTime(cls.StartDateTime.Value, tz) <= DateTimeOffset.UtcNow)
                throw StudioException.Conflict("This class has already started.");

            var visitId = await AddToClassAsync(clientId, classId, ct);
            return (ClassBooking(visitId, cls, tz, false), true);
        }, ct);
    }

    public async Task<(StudioBookingDto Booking, bool Created)> BookAppointmentAsync(
        string userName, int sessionTypeId, DateTimeOffset startTime, long? staffId, int? locationId,
        NewClientDetails? details, CancellationToken ct)
    {
        var tz = await _gateway.GetSiteZoneAsync(ct);
        var start = TimeZoneInfo.ConvertTime(startTime, tz);
        var type = await GetSessionTypeAsync(sessionTypeId, ct);
        var minutes = type.DefaultTimeLength ?? type.StaffTimeLength;
        if (minutes is null or <= 0)
            throw StudioException.Conflict("This session type has no set length, so it cannot be booked.");
        var end = TimeZoneInfo.ConvertTime(start.AddMinutes(minutes.Value), tz);

        return await WithClientLockAsync(userName, async () =>
        {
            var clientId = await EnsureClientAsync(userName, details, true, ct);

            var existing = (await GetClientVisitsAsync(clientId, start.AddDays(-1), start.AddDays(2), ct))
                .FirstOrDefault(v => v.AppointmentId is > 0 && IsActive(v) && v.StartDateTime.HasValue
                                     && ToSiteTime(v.StartDateTime.Value, tz) == start
                                     && (staffId is null || v.StaffId == staffId)
                                     && (locationId is null || v.LocationId == locationId));
            if (existing != null)
            {
                return (new StudioBookingDto
                {
                    BookingId = existing.AppointmentId!.Value,
                    Kind = "appointment",
                    SessionTypeId = sessionTypeId,
                    TrainerId = existing.StaffId,
                    LocationId = existing.LocationId,
                    StartTime = start,
                    EndTime = existing.EndDateTime.HasValue ? ToSiteTime(existing.EndDateTime.Value, tz) : end,
                    AlreadyBooked = true
                }, false);
            }

            if (start <= DateTimeOffset.UtcNow)
                throw StudioException.Conflict("That time has already passed.");

            var candidates = await FindAvailabilitiesAsync(sessionTypeId, start, minutes.Value, staffId, locationId, tz, ct);

            // Several trainers may be on shift; try them in turn until one can really take the session.
            AddAppointmentResponse? response = null;
            Availability? slot = null;
            StudioException? lastRefusal = null;
            foreach (var candidate in candidates)
            {
                try
                {
                    response = await _gateway.CallAsync(token => _gateway.Client.AppointmentApi.AddAppointment(
                        new AddAppointmentOperationRequest
                        {
                            SiteId = _gateway.SiteId,
                            Authorization = token,
                            Body = new AddAppointmentRequest
                            {
                                ClientId = clientId,
                                LocationId = candidate.Location!.Id!.Value,
                                StaffId = candidate.Staff!.Id,
                                StaffRequested = staffId.HasValue,
                                SessionTypeId = sessionTypeId,
                                StartDateTime = start.ToUniversalTime()
                            }
                        }, null, CancellationToken.None), CancellationToken.None);
                    slot = candidate;
                    break;
                }
                catch (StudioException ex) when (ex.StatusCode == 409)
                {
                    lastRefusal = ex;
                }
                catch (MindbodyUnreadableResponseException)
                {
                    // The session may well have been booked; the studio's records tell.
                    var made = (await GetClientVisitsAsync(clientId, start.AddDays(-1), start.AddDays(2), ct))
                        .FirstOrDefault(v => v.AppointmentId is > 0 && IsActive(v) && v.StartDateTime.HasValue
                                             && ToSiteTime(v.StartDateTime.Value, tz) == start);
                    if (made == null)
                        throw new StudioException(502, "The studio system did not confirm the booking.");
                    return (new StudioBookingDto
                    {
                        BookingId = made.AppointmentId!.Value,
                        Kind = "appointment",
                        SessionTypeId = sessionTypeId,
                        TrainerId = made.StaffId,
                        LocationId = made.LocationId,
                        StartTime = start,
                        EndTime = made.EndDateTime.HasValue ? ToSiteTime(made.EndDateTime.Value, tz) : end
                    }, true);
                }
            }
            if (response is null || slot is null)
                throw lastRefusal ?? StudioException.Conflict("No trainer is available for that session type at that time.");

            var appointment = response.Appointment;
            if (appointment?.Id is null)
                throw new StudioException(502, "The studio system did not confirm the booking.");
            return (new StudioBookingDto
            {
                BookingId = appointment.Id.Value,
                Kind = "appointment",
                SessionTypeId = sessionTypeId,
                TrainerId = appointment.StaffId ?? slot.Staff!.Id,
                LocationId = appointment.LocationId ?? slot.Location!.Id,
                StartTime = appointment.StartDateTime.HasValue ? ToSiteTime(appointment.StartDateTime.Value, tz) : start,
                EndTime = appointment.EndDateTime.HasValue ? ToSiteTime(appointment.EndDateTime.Value, tz) : end
            }, true);
        }, ct);
    }

    private async Task<long> AddToClassAsync(string clientId, int classId, CancellationToken ct)
    {
        try
        {
            var response = await _gateway.CallAsync(token => _gateway.Client.ClassApi.AddClientToClass(
                new AddClientToClassOperationRequest
                {
                    SiteId = _gateway.SiteId,
                    Authorization = token,
                    Body = new AddClientToClassRequest { ClientId = clientId, ClassId = classId }
                }, null, CancellationToken.None), CancellationToken.None);
            return response.Visit?.Id ?? throw new StudioException(502, "The studio system did not confirm the booking.");
        }
        catch (MindbodyUnreadableResponseException)
        {
            // The booking may well have been made; the studio's records tell.
            var visits = await GetClientVisitsAsync(clientId, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(3 * 366), ct);
            var visit = visits.FirstOrDefault(v => v.ClassId == classId && IsActive(v));
            return visit?.Id ?? throw new StudioException(502, "The studio system did not confirm the booking.");
        }
    }

    private async Task<List<Availability>> FindAvailabilitiesAsync(int sessionTypeId, DateTimeOffset start, int minutes,
        long? staffId, int? locationId, TimeZoneInfo tz, CancellationToken ct)
    {
        var dayStart = LocalMidnight(DateOnly.FromDateTime(start.DateTime), tz);
        var dayEnd = LocalMidnight(DateOnly.FromDateTime(start.DateTime).AddDays(1), tz);
        var items = await PageAllAsync<Availability>(async offset =>
        {
            var response = await _gateway.CallAsync(token => _gateway.Client.AppointmentApi.GetBookableItems(
                new GetBookableItemsOperationRequest
                {
                    SiteId = _gateway.SiteId,
                    Authorization = token,
                    RequestSessionTypeIds = new[] { sessionTypeId },
                    RequestStartDate = dayStart,
                    RequestEndDate = dayEnd,
                    RequestStaffIds = staffId.HasValue ? new[] { staffId.Value } : null,
                    RequestLocationIds = locationId.HasValue ? new[] { locationId.Value } : null,
                    RequestLimit = PageSize,
                    RequestOffset = offset
                }, null, ct), ct);
            return (response.Availabilities, response.PaginationResponse?.TotalResults);
        });

        var wanted = start.DateTime;
        var wantedEnd = wanted.AddMinutes(minutes);
        var matches = items.Where(a =>
            a.Staff?.Id != null && a.Location?.Id != null
            && a.StartDateTime.HasValue && a.EndDateTime.HasValue
            && a.StartDateTime.Value.DateTime <= wanted && wantedEnd <= a.EndDateTime.Value.DateTime
            && (staffId is null || a.Staff.Id == staffId)
            && (locationId is null || a.Location.Id == locationId))
            .GroupBy(a => (a.Staff!.Id, a.Location!.Id)).Select(g => g.First())
            .ToList();
        return matches.Count > 0
            ? matches
            : throw StudioException.Conflict("No trainer is available for that session type at that time.");
    }

    private static StudioBookingDto ClassBooking(Visit visit, Class cls, TimeZoneInfo tz, bool already) =>
        ClassBooking(visit.Id ?? 0, cls, tz, already);

    private static StudioBookingDto ClassBooking(long visitId, Class cls, TimeZoneInfo tz, bool already)
    {
        var start = ToSiteTime(cls.StartDateTime!.Value, tz);
        return new StudioBookingDto
        {
            BookingId = visitId,
            Kind = "class",
            ClassId = cls.Id,
            TrainerId = cls.Staff?.Id,
            LocationId = cls.Location?.Id,
            StartTime = start,
            EndTime = cls.EndDateTime.HasValue ? ToSiteTime(cls.EndDateTime.Value, tz) : start,
            AlreadyBooked = already
        };
    }

    // ------------------------------------------------------------------ cancel

    public Task CancelClassBookingAsync(string userName, long bookingId, CancellationToken ct) =>
        CancelAsync(userName, bookingId, true, ct);

    public Task CancelAppointmentBookingAsync(string userName, long bookingId, CancellationToken ct) =>
        CancelAsync(userName, bookingId, false, ct);

    private async Task CancelAsync(string userName, long bookingId, bool isClass, CancellationToken ct)
    {
        await WithClientLockAsync(userName, async () =>
        {
            var clientId = await FindExistingClientAsync(userName, ct)
                           ?? throw StudioException.NotFound($"You have no {(isClass ? "class" : "appointment")} booking with id {bookingId}.");
            var memoryKey = $"studio-cancelled:{(isClass ? "class" : "appointment")}:{clientId}:{bookingId}";
            var tz = await _gateway.GetSiteZoneAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var visits = await GetClientVisitsAsync(clientId, now.AddDays(-2), now.AddDays(3 * 366), ct);
            var visit = visits.FirstOrDefault(v => IsActive(v) && (isClass
                ? v.ClassId is > 0 && v.Id == bookingId
                : v.AppointmentId is > 0 && v.AppointmentId == bookingId));

            if (visit is null)
            {
                // A repeated request for a booking that was just cancelled is a success, not an error.
                if (_cache.TryGetValue(memoryKey, out _))
                    return 0;
                throw StudioException.NotFound($"You have no {(isClass ? "class" : "appointment")} booking with id {bookingId}.");
            }

            if (visit.StartDateTime.HasValue && ToSiteTime(visit.StartDateTime.Value, tz) <= now)
                throw StudioException.Conflict("This booking has already started and can no longer be cancelled.");

            try
            {
                if (isClass)
                {
                    await _gateway.CallAsync(token => _gateway.Client.ClassApi.RemoveClientFromClass(
                        new RemoveClientFromClassOperationRequest
                        {
                            SiteId = _gateway.SiteId,
                            Authorization = token,
                            Body = new RemoveClientFromClassRequest
                            {
                                ClientId = clientId,
                                ClassId = visit.ClassId!.Value,
                                VisitId = checked((int)bookingId)
                            }
                        }, null, CancellationToken.None), CancellationToken.None);
                }
                else
                {
                    await _gateway.CallAsync(token => _gateway.Client.AppointmentApi.UpdateAppointment(
                        new UpdateAppointmentOperationRequest
                        {
                            SiteId = _gateway.SiteId,
                            Authorization = token,
                            Body = new UpdateAppointmentRequest { AppointmentId = bookingId, Execute = "cancel" }
                        }, null, CancellationToken.None), CancellationToken.None);
                }
            }
            catch (MindbodyUnreadableResponseException)
            {
                // The cancellation may well have been made; the studio's records tell.
                var after = await GetClientVisitsAsync(clientId, now.AddDays(-2), now.AddDays(3 * 366), ct);
                if (after.Any(v => IsActive(v) && (isClass ? v.Id == bookingId && v.ClassId is > 0 : v.AppointmentId == bookingId)))
                    throw new StudioException(502, "The studio system did not confirm the cancellation.");
            }

            _cache.Set(memoryKey, true, CancelledMemory);
            return 0;
        }, ct);
    }

    // ------------------------------------------------------------------ trainers

    public async Task<List<StudioTrainerDto>> GetTrainersAsync(CancellationToken ct)
    {
        var staff = await PageAllAsync<Staff>(async offset =>
        {
            var response = await _gateway.CallAsync(token => _gateway.Client.StaffApi.GetStaff(
                new GetStaffOperationRequest
                {
                    SiteId = _gateway.SiteId,
                    Authorization = token,
                    RequestLimit = PageSize,
                    RequestOffset = offset
                }, null, ct), ct);
            return (response.StaffMembers, response.PaginationResponse?.TotalResults);
        });

        // Mindbody keeps system entries (negative ids, such as the auto-email sender) among the staff.
        return staff
            .Where(s => s.Id is > 0 && (s.AppointmentInstructor == true || s.ClassTeacher == true))
            .GroupBy(s => s.Id)
            .Select(g => g.First())
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ThenBy(s => s.Id)
            .Select(s => new StudioTrainerDto
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
                Country = s.Country
            })
            .ToList();
    }

    // ------------------------------------------------------------------ clients

    /// <summary>The studio client id of an eShop identity; stable, so the client is created only once.</summary>
    public static string ClientIdFor(string userName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userName.Trim().ToLowerInvariant()));
        return "eshop-" + Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    }

    private async Task<string> EnsureClientAsync(string userName, NewClientDetails? details, bool mayCreate, CancellationToken ct)
    {
        var found = await FindExistingClientAsync(userName, ct);
        if (found != null)
            return found;

        var clientId = ClientIdFor(userName);
        if (!mayCreate)
            throw StudioException.NotFound($"'{userName}' is not a client of the studio yet; they must make a first booking themselves.");

        var request = await ValidateNewClientAsync(userName, clientId, details, ct);
        try
        {
            var response = await _gateway.CallAsync(token => _gateway.Client.ClientApi.AddClient(
                new AddClientOperationRequest { SiteId = _gateway.SiteId, Authorization = token, Body = request },
                null, CancellationToken.None), CancellationToken.None);
            return response.Client?.Id ?? clientId;
        }
        catch (MindbodyUnreadableResponseException)
        {
            return await FindExistingClientAsync(userName, ct)
                   ?? throw new StudioException(502, "The studio system did not confirm the new client.");
        }
    }

    private async Task<string?> FindExistingClientAsync(string userName, CancellationToken ct)
    {
        var found = await FindClientAsync(ClientIdFor(userName), null, ct);
        if (found != null)
            return found;

        // A client may already exist for this shopper's e-mail without the eShop id.
        return userName.Contains('@') ? await FindClientAsync(null, userName, ct) : null;
    }

    private async Task<string?> FindClientAsync(string? clientId, string? email, CancellationToken ct)
    {
        var response = await _gateway.CallAsync(token => _gateway.Client.ClientApi.GetClients(
            new GetClientsOperationRequest
            {
                SiteId = _gateway.SiteId,
                Authorization = token,
                RequestClientIDs = clientId != null ? new[] { clientId } : null,
                RequestSearchText = email,
                RequestIncludeInactive = true,
                RequestLimit = PageSize
            }, null, ct), ct);

        var clients = response.Clients ?? Array.Empty<ClientWithSuspensionInfo>();
        var match = clientId != null
            ? clients.FirstOrDefault(c => string.Equals(c.Id, clientId, StringComparison.OrdinalIgnoreCase))
            : clients.FirstOrDefault(c => string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase));
        return match?.Id;
    }

    private async Task<AddClientRequest> ValidateNewClientAsync(string userName, string clientId, NewClientDetails? d, CancellationToken ct)
    {
        var missing = new List<string>();
        if (d?.HomeLocationId is null or <= 0) missing.Add("homeLocationId");
        if (string.IsNullOrWhiteSpace(d?.StreetAddress)) missing.Add("streetAddress");
        if (string.IsNullOrWhiteSpace(d?.City)) missing.Add("city");
        if (string.IsNullOrWhiteSpace(d?.State)) missing.Add("state");
        if (string.IsNullOrWhiteSpace(d?.PostalCode)) missing.Add("postalCode");
        if (string.IsNullOrWhiteSpace(d?.DateOfBirth)) missing.Add("dateOfBirth");
        if (string.IsNullOrWhiteSpace(d?.MobilePhone)) missing.Add("mobilePhone");
        if (string.IsNullOrWhiteSpace(d?.ReferredBy)) missing.Add("referredBy");
        if (missing.Count > 0)
            throw StudioException.BadRequest(
                "This is your first studio booking, so the studio needs more details about you. Missing: " + string.Join(", ", missing) + ".");

        var dob = ParseDate(d!.DateOfBirth, "dateOfBirth")!.Value;
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        if (dob >= todayUtc || dob.Year < 1900)
            throw StudioException.BadRequest("'dateOfBirth' is not a valid date of birth.");

        var phoneDigits = Regex.Replace(d.MobilePhone!, @"[\s\-\(\)\.\+]", "");
        if (!Regex.IsMatch(phoneDigits, @"^\d{7,15}$"))
            throw StudioException.BadRequest("'mobilePhone' is not a valid phone number.");

        var locations = await PageAllAsync<Location>(async offset =>
        {
            var response = await _gateway.CallAsync(token => _gateway.Client.SiteApi.GetLocations(
                new GetLocationsOperationRequest { SiteId = _gateway.SiteId, Authorization = token, RequestLimit = PageSize, RequestOffset = offset },
                null, ct), ct);
            return (response.Locations, response.PaginationResponse?.TotalResults);
        });
        var home = locations.FirstOrDefault(l => l.Id == d.HomeLocationId)
                   ?? throw StudioException.BadRequest(
                       $"'homeLocationId' {d.HomeLocationId} is not a studio location. Choose one of: "
                       + string.Join(", ", locations.Select(l => $"{l.Id} ({l.Name})")) + ".");

        var referral = await _gateway.CallAsync(token => _gateway.Client.ClientApi.GetClientReferralTypes(
            new GetClientReferralTypesOperationRequest { SiteId = _gateway.SiteId, Authorization = token }, null, ct), ct);
        var referralTypes = referral.ReferralTypes ?? Array.Empty<string>();
        var referredBy = referralTypes.FirstOrDefault(r => string.Equals(r, d.ReferredBy!.Trim(), StringComparison.OrdinalIgnoreCase))
                         ?? throw StudioException.BadRequest(
                             $"'referredBy' must be one of: {string.Join(", ", referralTypes)}.");

        string first = d.FirstName?.Trim() ?? "";
        string last = d.LastName?.Trim() ?? "";
        if (first.Length == 0 || last.Length == 0)
        {
            // The eShop identity is an e-mail address; use it for names the shopper did not give.
            var local = userName.Contains('@') ? userName[..userName.IndexOf('@')] : userName;
            if (first.Length == 0) first = local;
            if (last.Length == 0) last = "Shopper";
        }

        return new AddClientRequest
        {
            NewId = clientId,
            FirstName = first,
            LastName = last,
            Email = userName.Contains('@') ? userName : null,
            AddressLine1 = d.StreetAddress!.Trim(),
            City = d.City!.Trim(),
            State = d.State!.Trim(),
            PostalCode = d.PostalCode!.Trim(),
            // Noon UTC falls on the same calendar day in every time zone the studio could be in.
            BirthDate = new DateTimeOffset(dob.Year, dob.Month, dob.Day, 12, 0, 0, TimeSpan.Zero),
            MobilePhone = d.MobilePhone!.Trim(),
            ReferredBy = referredBy,
            HomeLocation = new Location { Id = home.Id }
        };
    }

    private async Task<List<Visit>> GetClientVisitsAsync(string clientId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var visits = await PageAllAsync<Visit>(async offset =>
        {
            var response = await _gateway.CallAsync(token => _gateway.Client.ClientApi.GetClientVisits(
                new GetClientVisitsOperationRequest
                {
                    SiteId = _gateway.SiteId,
                    Authorization = token,
                    RequestClientId = clientId,
                    RequestStartDate = start,
                    RequestEndDate = end,
                    RequestLimit = PageSize,
                    RequestOffset = offset
                }, null, ct), ct);
            return (response.Visits, response.PaginationResponse?.TotalResults);
        });
        return visits;
    }

    private static bool IsActive(Visit v) =>
        v.AppointmentStatus != AppointmentStatus.Cancelled
        && v.AppointmentStatus != AppointmentStatus.LateCancelled;

    // ------------------------------------------------------------------ helpers

    private static async Task<T> WithClientLockAsync<T>(string userName, Func<Task<T>> work, CancellationToken ct)
    {
        var index = (int)((uint)ClientIdFor(userName).GetHashCode() % (uint)ClientLocks.Length);
        var gate = ClientLocks[index];
        await gate.WaitAsync(ct);
        try
        {
            return await work();
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<List<T>> PageAllAsync<T>(Func<int, Task<(IReadOnlyList<T>? Items, int? Total)>> fetch)
    {
        var all = new List<T>();
        for (var page = 0; page < 500; page++)
        {
            var (items, total) = await fetch(all.Count);
            if (items is null || items.Count == 0)
                break;
            all.AddRange(items);
            if (total.HasValue ? all.Count >= total.Value : items.Count < PageSize)
                break;
        }
        return all;
    }

    private static DateOnly Today(TimeZoneInfo tz) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);

    private static DateOnly? ParseDate(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        if (value.Contains('T') && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            return DateOnly.FromDateTime(dto.DateTime);
        throw StudioException.BadRequest($"'{name}' must be a date in the form yyyy-MM-dd.");
    }

    /// <summary>
    /// Mindbody reports date-times as the studio's local wall-clock time without an offset, and the SDK
    /// reads such text as UTC. The wall-clock reading is kept and given the studio's real offset.
    /// </summary>
    private static DateTimeOffset ToSiteTime(DateTimeOffset reportedByMindbody, TimeZoneInfo tz)
    {
        var wall = DateTime.SpecifyKind(reportedByMindbody.DateTime, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(wall))
            wall = wall.AddHours(1);
        return new DateTimeOffset(wall, tz.GetUtcOffset(wall));
    }

    private static DateTimeOffset LocalMidnight(DateOnly day, TimeZoneInfo tz)
    {
        var wall = day.ToDateTime(TimeOnly.MinValue);
        if (tz.IsInvalidTime(wall))
            wall = wall.AddHours(1);
        return new DateTimeOffset(wall, tz.GetUtcOffset(wall));
    }
}
