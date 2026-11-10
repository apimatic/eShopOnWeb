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
using MindbodyPublicApi.Core.Exceptions;
using MindbodyPublicApi.Requests.AppointmentApi;
using MindbodyPublicApi.Requests.ClassApi;
using MindbodyPublicApi.Requests.ClientApi;
using MindbodyPublicApi.Requests.SiteApi;
using MindbodyPublicApi.Requests.StaffApi;
using Mb = MindbodyPublicApi.Models;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// The studio features, implemented on top of the Mindbody SDK. Nothing here is stubbed: every answer comes from Mindbody.
/// </summary>
public class StudioService
{
    private const int PageSize = 200;
    private const int MaxPages = 100;
    private const int MaxSlotRangeDays = 93;
    private const int DefaultClassRangeDays = 7;
    /// <summary>How far ahead a booking can be looked up.</summary>
    private const int BookingHorizonDays = 400;

    private static readonly TimeSpan CancelledMemory = TimeSpan.FromDays(2);
    private static readonly TimeSpan CatalogLifetime = TimeSpan.FromMinutes(10);

    private readonly MindbodyGateway _mb;
    private readonly KeyedLocks _locks;
    private readonly IMemoryCache _cache;
    private readonly UserManager<ApplicationUser> _users;

    public StudioService(MindbodyGateway mb, KeyedLocks locks, IMemoryCache cache, UserManager<ApplicationUser> users)
    {
        _mb = mb;
        _locks = locks;
        _cache = cache;
        _users = users;
    }

    // ------------------------------------------------------------------------------------------------ classes

    public async Task<List<StudioClassDto>> ListClassesAsync(string? from, string? to, CancellationToken ct)
    {
        var zone = await _mb.GetTimeZoneAsync(ct);
        var now = StudioTime.StudioNow(zone);
        var today = DateOnly.FromDateTime(now);

        var (first, last) = ParseRange(from, to, today, today.AddDays(DefaultClassRangeDays), allowOpenEnd: true);

        var classes = await GetClassesAsync(zone, first, last, null, ct);
        return classes
            .Where(c => c.StartDateTime != null && c.EndDateTime != null)
            .Where(c => { var d = DateOnly.FromDateTime(StudioTime.Wall(c.StartDateTime!.Value)); return d >= first && d <= last; })
            .Where(c => AttendabilityProblem(c, now) == null)
            .OrderBy(c => StudioTime.Wall(c.StartDateTime!.Value))
            .ThenBy(c => c.Id)
            .Select(c => ToDto(c, zone))
            .ToList();
    }

    private async Task<List<Mb.Class>> GetClassesAsync(TimeZoneInfo zone, DateOnly first, DateOnly last, int? classId, CancellationToken ct)
    {
        var start = StudioTime.QueryInstant(first.ToDateTime(TimeOnly.MinValue), zone);
        var end = StudioTime.QueryInstant(last.ToDateTime(new TimeOnly(23, 59, 59)), zone);

        return await PageAsync(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, token) => c.ClassApi.GetClasses(new GetClassesOperationRequest
            {
                SiteId = site,
                Authorization = token,
                RequestStartDateTime = start,
                RequestEndDateTime = end,
                RequestClassIds = classId == null ? null : new[] { classId.Value },
                RequestLimit = limit,
                RequestOffset = offset,
            }), ct);
            return (r.Classes, r.PaginationResponse?.TotalResults);
        });
    }

    /// <summary>Why a shopper cannot attend the class, or null when they can.</summary>
    private static string? AttendabilityProblem(Mb.Class c, DateTime studioNow)
    {
        if (c.IsCanceled == true) return "This class has been cancelled.";
        if (c.Active == false) return "This class is no longer scheduled.";
        if (c.IsAvailable == false) return "This class is not available.";
        if (c.StartDateTime == null || StudioTime.Wall(c.StartDateTime.Value) <= studioNow) return "This class has already started.";
        if ((c.WebCapacity ?? 0) <= 0) return "This class is not open for booking through the app.";
        if ((c.WebBooked ?? 0) >= c.WebCapacity) return "This class is full.";
        return null;
    }

    private static StudioClassDto ToDto(Mb.Class c, TimeZoneInfo zone) => new()
    {
        Id = c.Id ?? 0,
        Name = c.ClassDescription?.Name?.Trim(),
        Description = string.IsNullOrWhiteSpace(c.ClassDescription?.Description) ? null : c.ClassDescription!.Description,
        StartTime = StudioTime.FromMindbody(c.StartDateTime!.Value, zone),
        EndTime = StudioTime.FromMindbody(c.EndDateTime!.Value, zone),
        LocationId = c.Location?.Id,
        LocationName = c.Location?.Name,
        TrainerName = c.Staff?.DisplayName ?? c.Staff?.Name,
        SpotsLeft = c.WebCapacity != null ? Math.Max(0, c.WebCapacity.Value - (c.WebBooked ?? 0)) : null,
    };

    // ------------------------------------------------------------------------------------------------ session times

    public async Task<List<StudioSlotDto>> ListSlotsAsync(int sessionTypeId, string? from, string? to, CancellationToken ct)
    {
        if (sessionTypeId <= 0) throw StudioException.BadRequest("sessionTypeId is required.");
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            throw StudioException.BadRequest("Both 'from' and 'to' dates are required (yyyy-MM-dd).");

        var zone = await _mb.GetTimeZoneAsync(ct);
        var today = DateOnly.FromDateTime(StudioTime.StudioNow(zone));
        var (first, last) = ParseRange(from, to, today, today, allowOpenEnd: false);
        if (last.DayNumber - first.DayNumber + 1 > MaxSlotRangeDays)
            throw StudioException.BadRequest($"The date range can span at most {MaxSlotRangeDays} days.");

        var type = await GetAppointmentTypeAsync(sessionTypeId, ct);
        var length = TimeSpan.FromMinutes(type.DefaultTimeLength is > 0 ? type.DefaultTimeLength.Value : 60);

        var days = Enumerable.Range(0, last.DayNumber - first.DayNumber + 1).Select(i => first.AddDays(i)).ToList();
        using var gate = new SemaphoreSlim(4);
        var perDay = await Task.WhenAll(days.Select(async day =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return await ActiveTimesAsync(sessionTypeId, day, zone, ct);
            }
            finally
            {
                gate.Release();
            }
        }));

        var slots = new List<StudioSlotDto>();
        for (var i = 0; i < days.Count; i++)
        {
            foreach (var time in perDay[i].Distinct().OrderBy(t => t))
            {
                var wall = days[i].ToDateTime(TimeOnly.FromTimeSpan(time));
                if (zone.IsInvalidTime(wall)) continue;
                var start = StudioTime.AtStudio(wall, zone);
                slots.Add(new StudioSlotDto
                {
                    SessionTypeId = sessionTypeId,
                    SessionTypeName = type.Name,
                    StartTime = start,
                    EndTime = start.Add(length),
                });
            }
        }
        return slots;
    }

    private async Task<List<TimeSpan>> ActiveTimesAsync(int sessionTypeId, DateOnly day, TimeZoneInfo zone, CancellationToken ct)
    {
        var start = StudioTime.QueryInstant(day.ToDateTime(TimeOnly.MinValue), zone);
        var end = StudioTime.QueryInstant(day.ToDateTime(new TimeOnly(23, 59, 59)), zone);

        var all = await PageAsync(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, token) => c.AppointmentApi.GetActiveSessionTimes(new GetActiveSessionTimesOperationRequest
            {
                SiteId = site,
                Authorization = token,
                RequestStartTime = start,
                RequestEndTime = end,
                RequestSessionTypeIds = new[] { sessionTypeId },
                RequestLimit = limit,
                RequestOffset = offset,
            }), ct);
            return (r.ActiveSessionTimes, r.PaginationResponse?.TotalResults);
        });

        var times = new List<TimeSpan>();
        foreach (var text in all)
        {
            if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
                times.Add(t);
        }
        return times;
    }

    private async Task<Mb.SessionType> GetAppointmentTypeAsync(int sessionTypeId, CancellationToken ct)
    {
        var types = await GetSessionTypesAsync(ct);
        var type = types.FirstOrDefault(t => t.Id == sessionTypeId)
                   ?? throw StudioException.NotFound($"There is no session type {sessionTypeId}.");
        var kind = type.Type?.Value;
        if (kind != null && !string.Equals(kind, "Appointment", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(kind, "All", StringComparison.OrdinalIgnoreCase))
        {
            throw StudioException.BadRequest($"Session type {sessionTypeId} ('{type.Name}') is not a one-to-one session.");
        }
        return type;
    }

    private async Task<IReadOnlyList<Mb.SessionType>> GetSessionTypesAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue("studio:sessiontypes", out IReadOnlyList<Mb.SessionType>? cached) && cached != null) return cached;

        var list = await PageAsync(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, token) => c.SiteApi.GetSessionTypes(new GetSessionTypesOperationRequest
            {
                SiteId = site,
                Authorization = token,
                RequestLimit = limit,
                RequestOffset = offset,
            }), ct);
            return (r.SessionTypes, r.PaginationResponse?.TotalResults);
        });
        _cache.Set("studio:sessiontypes", (IReadOnlyList<Mb.SessionType>)list, CatalogLifetime);
        return list;
    }

    private async Task EnsureLocationExistsAsync(int locationId, CancellationToken ct)
    {
        if (!_cache.TryGetValue("studio:locations", out IReadOnlyList<int>? ids) || ids == null)
        {
            var list = await PageAsync(async (offset, limit) =>
            {
                var r = await _mb.CallAsync((c, site, token) => c.SiteApi.GetLocations(new GetLocationsOperationRequest
                {
                    SiteId = site,
                    Authorization = token,
                    RequestLimit = limit,
                    RequestOffset = offset,
                }), ct);
                return (r.Locations, r.PaginationResponse?.TotalResults);
            });
            ids = list.Where(l => l.Id != null).Select(l => l.Id!.Value).ToList();
            _cache.Set("studio:locations", ids, CatalogLifetime);
        }
        if (!ids.Contains(locationId))
            throw StudioException.BadRequest($"There is no studio location {locationId}.");
    }

    // ------------------------------------------------------------------------------------------------ booking

    public async Task<StudioBookingDto> BookClassAsync(string userName, BookClassRequest request, CancellationToken ct)
    {
        if (request.ClassId <= 0) throw StudioException.BadRequest("classId is required.");
        var email = await ResolveEmailAsync(userName);
        return await BookClassForAsync(email, request.ClassId, request, requireCreateDetails: true, staffOverride: false, ct);
    }

    public async Task<StudioBookingDto> AdminBookClassAsync(AdminBookClassRequest request, CancellationToken ct)
    {
        var shopper = request.ResolvedShopper ?? throw StudioException.BadRequest("The shopper is required.");
        if (request.ClassId <= 0) throw StudioException.BadRequest("classId is required.");

        var user = shopper.Contains('@')
            ? (await _users.FindByEmailAsync(shopper) ?? await _users.FindByNameAsync(shopper))
            : await _users.FindByNameAsync(shopper);
        if (user == null) throw StudioException.NotFound($"There is no shopper '{shopper}'.");
        var email = EmailOf(user);

        return await BookClassForAsync(email, request.ClassId, request, requireCreateDetails: false, staffOverride: true, ct);
    }

    private async Task<StudioBookingDto> BookClassForAsync(string email, int classId, StudioClientDetails details,
        bool requireCreateDetails, bool staffOverride, CancellationToken ct)
    {
        var zone = await _mb.GetTimeZoneAsync(ct);
        var now = StudioTime.StudioNow(zone);

        using var _ = await _locks.AcquireAsync("shopper:" + email, ct);

        var cls = await FindClassAsync(classId, zone, now, ct);
        if (cls.IsCanceled == true) throw StudioException.Conflict("This class has been cancelled.");
        if (!staffOverride)
        {
            var problem = AttendabilityProblem(cls, now);
            if (problem != null) throw StudioException.Conflict(problem);
        }

        var clientId = await EnsureClientAsync(email, details, requireCreateDetails, zone, ct);

        var classDay = DateOnly.FromDateTime(StudioTime.Wall(cls.StartDateTime!.Value));
        var existing = (await GetVisitsAsync(clientId, classDay, classDay, zone, ct))
            .FirstOrDefault(v => v.ClassId == classId && IsActive(v));
        if (existing?.Id != null) return ClassBooking(cls, existing.Id.Value, clientId, zone, alreadyBooked: true);

        long visitId;
        try
        {
            var added = await _mb.CallAsync((c, site, token) => c.ClassApi.AddClientToClass(new AddClientToClassOperationRequest
            {
                SiteId = site,
                Authorization = token,
                Body = new Mb.AddClientToClassRequest { ClientId = clientId, ClassId = classId, RequirePayment = false },
            }), ct, passUnreadableResponse: true);
            visitId = added.Visit?.Id ?? 0;
        }
        catch (ResponseDeserializationException)
        {
            visitId = 0;
        }

        if (visitId == 0)
        {
            // The class was booked but Mindbody's answer could not be read (or carried no id): find the booking.
            var found = (await GetVisitsAsync(clientId, classDay, classDay, zone, ct))
                .Where(v => v.ClassId == classId && IsActive(v) && v.Id != null)
                .OrderByDescending(v => v.Id)
                .FirstOrDefault();
            if (found == null) throw new StudioException(502, "The studio system did not confirm the booking. Please check your bookings before trying again.");
            visitId = found.Id!.Value;
        }

        return ClassBooking(cls, visitId, clientId, zone, alreadyBooked: false);
    }

    private static StudioBookingDto ClassBooking(Mb.Class cls, long visitId, string clientId, TimeZoneInfo zone, bool alreadyBooked) => new()
    {
        Kind = "class",
        BookingId = visitId,
        ClassId = cls.Id,
        Name = cls.ClassDescription?.Name?.Trim(),
        StartTime = StudioTime.FromMindbody(cls.StartDateTime!.Value, zone),
        EndTime = StudioTime.FromMindbody(cls.EndDateTime!.Value, zone),
        LocationId = cls.Location?.Id,
        TrainerName = cls.Staff?.DisplayName ?? cls.Staff?.Name,
        TrainerId = cls.Staff?.Id,
        ClientId = clientId,
        AlreadyBooked = alreadyBooked,
    };

    private async Task<Mb.Class> FindClassAsync(int classId, TimeZoneInfo zone, DateTime now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        var classes = await GetClassesAsync(zone, today.AddDays(-1), today.AddDays(BookingHorizonDays), classId, ct);
        var cls = classes.FirstOrDefault(c => c.Id == classId && c.StartDateTime != null && c.EndDateTime != null);
        return cls ?? throw StudioException.NotFound($"There is no class {classId} in the timetable.");
    }

    private static bool IsActive(Mb.Visit v)
    {
        var status = v.AppointmentStatus?.Value;
        return !string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(status, "LateCancelled", StringComparison.OrdinalIgnoreCase) &&
               v.LateCancelled != true;
    }

    public async Task<StudioBookingDto> BookAppointmentAsync(string userName, BookAppointmentRequest request, CancellationToken ct)
    {
        if (request.SessionTypeId <= 0) throw StudioException.BadRequest("sessionTypeId is required.");
        var zone = await _mb.GetTimeZoneAsync(ct);
        var now = StudioTime.StudioNow(zone);

        var startText = request.StartTime ?? request.StartDateTime;
        if (string.IsNullOrWhiteSpace(startText)) throw StudioException.BadRequest("startTime is required (ISO-8601 date-time).");
        var startWall = ParseStudioTime(startText!, zone) ?? throw StudioException.BadRequest("startTime is not a valid ISO-8601 date-time.");
        if (startWall <= now) throw StudioException.Conflict("That time has already passed.");
        var staffId = request.StaffId ?? request.TrainerId;

        var type = await GetAppointmentTypeAsync(request.SessionTypeId, ct);
        var email = await ResolveEmailAsync(userName);

        using var _ = await _locks.AcquireAsync("shopper:" + email, ct);

        var clientId = await EnsureClientAsync(email, request, requireCreateDetails: true, zone, ct);

        var day = DateOnly.FromDateTime(startWall);
        var typeLength = TimeSpan.FromMinutes(type.DefaultTimeLength is > 0 ? type.DefaultTimeLength.Value : 60);

        // The app may repeat a request it got no answer to: the same appointment is returned, not booked twice.
        var existing = (await GetAppointmentsAsync(clientId, null, day, day, zone, ct))
            .FirstOrDefault(a => a.SessionTypeId == request.SessionTypeId && a.Id != null && IsActive(a) &&
                                 a.StartDateTime != null && StudioTime.Wall(a.StartDateTime.Value) == startWall &&
                                 (staffId == null || a.StaffId == staffId));
        if (existing != null) return AppointmentBooking(existing, startWall, typeLength, type, zone, clientId, alreadyBooked: true);

        var startInstant = StudioTime.AtStudio(startWall, zone);
        var offers = await GetBookableItemsAsync(request.SessionTypeId, day, staffId, request.SessionLocationId, zone, ct);
        var offer = offers
            .Where(a => a.StartDateTime != null && a.Staff?.Id != null && a.Location?.Id != null)
            .Where(a => a.StartDateTime!.Value <= startInstant && startInstant <= (a.BookableEndDateTime ?? a.EndDateTime ?? a.StartDateTime!.Value))
            .OrderBy(a => a.Staff!.Id)
            .FirstOrDefault();
        if (offer == null)
            throw StudioException.Conflict("No trainer is available at that time for this session type.");

        long appointmentId;
        int? duration = null;
        try
        {
            var added = await _mb.CallAsync((c, site, token) => c.AppointmentApi.AddAppointment(new AddAppointmentOperationRequest
            {
                SiteId = site,
                Authorization = token,
                Body = new Mb.AddAppointmentRequest
                {
                    ClientId = clientId,
                    LocationId = offer.Location!.Id!.Value,
                    StaffId = offer.Staff!.Id,
                    SessionTypeId = request.SessionTypeId,
                    StartDateTime = StudioTime.BodyValue(startWall),
                    StaffRequested = staffId != null,
                },
            }), ct, passUnreadableResponse: true);
            appointmentId = added.Appointment?.Id ?? 0;
            duration = added.Appointment?.Duration;
        }
        catch (ResponseDeserializationException)
        {
            appointmentId = 0;
        }

        if (appointmentId == 0)
        {
            var found = (await GetAppointmentsAsync(clientId, null, day, day, zone, ct))
                .FirstOrDefault(a => a.SessionTypeId == request.SessionTypeId && a.Id != null && IsActive(a) &&
                                     a.StartDateTime != null && StudioTime.Wall(a.StartDateTime.Value) == startWall);
            if (found == null) throw new StudioException(502, "The studio system did not confirm the booking. Please check your bookings before trying again.");
            appointmentId = found.Id!.Value;
        }

        var length = duration is > 0 ? TimeSpan.FromMinutes(duration.Value) : typeLength;
        return new StudioBookingDto
        {
            Kind = "appointment",
            BookingId = appointmentId,
            SessionTypeId = request.SessionTypeId,
            Name = type.Name,
            StartTime = startInstant,
            EndTime = startInstant.Add(length),
            LocationId = offer.Location!.Id,
            TrainerId = offer.Staff!.Id,
            TrainerName = offer.Staff.DisplayName ?? offer.Staff.Name,
            ClientId = clientId,
        };
    }

    private static StudioBookingDto AppointmentBooking(Mb.Appointment a, DateTime startWall, TimeSpan typeLength, Mb.SessionType type,
        TimeZoneInfo zone, string clientId, bool alreadyBooked)
    {
        var start = StudioTime.AtStudio(startWall, zone);
        var length = a.Duration is > 0 ? TimeSpan.FromMinutes(a.Duration.Value) : typeLength;
        return new StudioBookingDto
        {
            Kind = "appointment",
            BookingId = a.Id ?? 0,
            SessionTypeId = a.SessionTypeId,
            Name = type.Name,
            StartTime = start,
            EndTime = start.Add(length),
            LocationId = a.LocationId,
            TrainerId = a.StaffId,
            TrainerName = a.Staff?.DisplayName,
            ClientId = clientId,
            AlreadyBooked = alreadyBooked,
        };
    }

    private static bool IsActive(Mb.Appointment a)
    {
        var status = a.Status?.Value;
        return !string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(status, "LateCancelled", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<List<Mb.Availability>> GetBookableItemsAsync(int sessionTypeId, DateOnly day, long? staffId, int? locationId,
        TimeZoneInfo zone, CancellationToken ct)
    {
        var start = StudioTime.QueryInstant(day.ToDateTime(TimeOnly.MinValue), zone);
        var end = StudioTime.QueryInstant(day.ToDateTime(new TimeOnly(23, 59, 59)), zone);
        return await PageAsync(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, token) => c.AppointmentApi.GetBookableItems(new GetBookableItemsOperationRequest
            {
                SiteId = site,
                Authorization = token,
                RequestSessionTypeIds = new[] { sessionTypeId },
                RequestStaffIds = staffId == null ? null : new[] { staffId.Value },
                RequestLocationIds = locationId == null ? null : new[] { locationId.Value },
                RequestStartDate = start,
                RequestEndDate = end,
                RequestLimit = limit,
                RequestOffset = offset,
            }), ct);
            return (r.Availabilities, r.PaginationResponse?.TotalResults);
        });
    }

    // ------------------------------------------------------------------------------------------------ cancelling

    public async Task<StudioCancellationDto> CancelClassBookingAsync(string userName, long bookingId, CancellationToken ct)
    {
        var email = await ResolveEmailAsync(userName);
        var zone = await _mb.GetTimeZoneAsync(ct);
        var today = DateOnly.FromDateTime(StudioTime.StudioNow(zone));

        using var _ = await _locks.AcquireAsync("shopper:" + email, ct);

        var clientId = await FindClientIdAsync(email, ct);
        var result = new StudioCancellationDto { Kind = "class", BookingId = bookingId };
        if (clientId == null) return AlreadyCancelledOrMissing(email, "class", bookingId, result);

        async Task<Mb.Visit?> Find() => (await GetVisitsAsync(clientId, today.AddDays(-30), today.AddDays(BookingHorizonDays), zone, ct))
            .FirstOrDefault(v => v.Id == bookingId && v.ClassId is > 0 && IsActive(v));

        var visit = await Find();
        if (visit == null) return AlreadyCancelledOrMissing(email, "class", bookingId, result);

        try
        {
            await _mb.CallAsync((c, site, token) => c.ClassApi.RemoveClientFromClass(new RemoveClientFromClassOperationRequest
            {
                SiteId = site,
                Authorization = token,
                Body = new Mb.RemoveClientFromClassRequest { ClientId = clientId, ClassId = visit.ClassId!.Value, VisitId = (int)bookingId },
            }), ct, passUnreadableResponse: true);
        }
        catch (ResponseDeserializationException)
        {
            // Mindbody answered 200 but the SDK could not read the answer: confirm the outcome instead of guessing.
            if (await Find() != null) throw new StudioException(502, "The studio system did not confirm the cancellation. Please try again.");
        }

        RememberCancelled(email, "class", bookingId);
        return result;
    }

    public async Task<StudioCancellationDto> CancelAppointmentBookingAsync(string userName, long bookingId, CancellationToken ct)
    {
        var email = await ResolveEmailAsync(userName);
        var zone = await _mb.GetTimeZoneAsync(ct);
        var today = DateOnly.FromDateTime(StudioTime.StudioNow(zone));

        using var _ = await _locks.AcquireAsync("shopper:" + email, ct);

        var clientId = await FindClientIdAsync(email, ct);
        var result = new StudioCancellationDto { Kind = "appointment", BookingId = bookingId };
        if (clientId == null || bookingId is <= 0 or > int.MaxValue) return AlreadyCancelledOrMissing(email, "appointment", bookingId, result);

        async Task<Mb.Appointment?> Find() => (await GetAppointmentsAsync(clientId, (int)bookingId, today.AddDays(-30), today.AddDays(BookingHorizonDays), zone, ct))
            .FirstOrDefault(a => a.Id == bookingId && a.ClientId == clientId && IsActive(a));

        var appointment = await Find();
        if (appointment == null) return AlreadyCancelledOrMissing(email, "appointment", bookingId, result);

        try
        {
            await _mb.CallAsync((c, site, token) => c.AppointmentApi.UpdateAppointment(new UpdateAppointmentOperationRequest
            {
                SiteId = site,
                Authorization = token,
                Body = new Mb.UpdateAppointmentRequest { AppointmentId = bookingId, Execute = "Cancel" },
            }), ct, passUnreadableResponse: true);
        }
        catch (ResponseDeserializationException)
        {
            if (await Find() != null) throw new StudioException(502, "The studio system did not confirm the cancellation. Please try again.");
        }

        RememberCancelled(email, "appointment", bookingId);
        return result;
    }

    /// <summary>
    /// A booking that is no longer among the shopper's bookings. If the shopper cancelled it a moment ago (the app is repeating
    /// a request it got no answer to) that is a success; otherwise it is not one of this shopper's bookings.
    /// </summary>
    private StudioCancellationDto AlreadyCancelledOrMissing(string email, string kind, long bookingId, StudioCancellationDto result)
    {
        if (_cache.TryGetValue(CancelledKey(email, kind, bookingId), out _)) return result;
        throw StudioException.NotFound($"You have no {kind} booking {bookingId}.");
    }

    private void RememberCancelled(string email, string kind, long bookingId) =>
        _cache.Set(CancelledKey(email, kind, bookingId), true, CancelledMemory);

    private static string CancelledKey(string email, string kind, long bookingId) =>
        $"studio:cancelled:{kind}:{email.ToLowerInvariant()}:{bookingId}";

    // ------------------------------------------------------------------------------------------------ trainers

    public async Task<List<StudioTrainerDto>> ListTrainersAsync(CancellationToken ct)
    {
        var staff = await PageAsync(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, token) => c.StaffApi.GetStaff(new GetStaffOperationRequest
            {
                SiteId = site,
                Authorization = token,
                RequestLimit = limit,
                RequestOffset = offset,
            }), ct);
            return (r.StaffMembers, r.PaginationResponse?.TotalResults);
        });

        return staff
            .Where(s => s.Id is > 0 && (s.ClassTeacher == true || s.AppointmentInstructor == true))
            .GroupBy(s => s.Id)
            .Select(g => g.First())
            .OrderBy(s => s.LastName, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.FirstName, StringComparer.OrdinalIgnoreCase)
            .Select(s => new StudioTrainerDto
            {
                Id = s.Id!.Value,
                FirstName = s.FirstName,
                LastName = s.LastName,
                DisplayName = s.DisplayName ?? s.Name,
                Active = s.Active ?? true,
                ClassTeacher = s.ClassTeacher ?? false,
                AppointmentInstructor = s.AppointmentInstructor ?? false,
                Email = s.Email,
                MobilePhone = s.MobilePhone,
                HomePhone = s.HomePhone,
                WorkPhone = s.WorkPhone,
                Address = s.Address,
                City = s.City,
                State = s.State,
                PostalCode = s.PostalCode,
                Country = s.Country,
                Bio = string.IsNullOrWhiteSpace(s.Bio) ? null : s.Bio,
                ImageUrl = s.ImageUrl,
            })
            .ToList();
    }

    // ------------------------------------------------------------------------------------------------ clients

    private async Task<string> ResolveEmailAsync(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName)) throw new StudioException(401, "The token does not identify a user.");
        var user = await _users.FindByNameAsync(userName)
                   ?? throw new StudioException(401, "The signed-in user no longer exists.");
        return EmailOf(user);
    }

    private static string EmailOf(ApplicationUser user)
    {
        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email : user.UserName;
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw StudioException.BadRequest("The shopper's eShop identity has no e-mail address.");
        return email.Trim();
    }

    private async Task<string?> FindClientIdAsync(string email, CancellationToken ct)
    {
        var clients = await PageAsync(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, token) => c.ClientApi.GetClients(new GetClientsOperationRequest
            {
                SiteId = site,
                Authorization = token,
                RequestSearchText = email,
                RequestLimit = limit,
                RequestOffset = offset,
            }), ct);
            return (r.Clients, r.PaginationResponse?.TotalResults);
        });

        var match = clients
            .Where(c => c.Id != null && string.Equals(c.Email?.Trim(), email, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Active ?? true)
            .FirstOrDefault();
        if (match?.SuspensionInfo?.BookingSuspended == true)
            throw StudioException.Conflict("Bookings are suspended for this client. Please contact the studio.");
        return match?.Id;
    }

    /// <summary>
    /// The studio client for an eShop identity. The client is created once, with the first booking, from the details given;
    /// afterwards the existing client is used and the details are not needed again.
    /// </summary>
    private async Task<string> EnsureClientAsync(string email, StudioClientDetails details, bool requireCreateDetails, TimeZoneInfo zone, CancellationToken ct)
    {
        var existing = await FindClientIdAsync(email, ct);
        if (existing != null) return existing;

        var problems = details.Validate(DateOnly.FromDateTime(StudioTime.StudioNow(zone)), out var dob);
        if (problems.Count > 0)
        {
            var who = requireCreateDetails ? "This is your first booking with the studio" : "This shopper has no studio client yet";
            throw StudioException.BadRequest(
                $"{who}, so the studio needs these details: {string.Join(", ", problems)}.");
        }

        await EnsureLocationExistsAsync(details.ResolvedHomeLocationId!.Value, ct);
        var (first, last) = details.NamesFor(email);

        try
        {
            var created = await _mb.CallAsync((c, site, token) => c.ClientApi.AddClient(new AddClientOperationRequest
            {
                SiteId = site,
                Authorization = token,
                Body = new Mb.AddClientRequest
                {
                    FirstName = first,
                    LastName = last,
                    Email = email,
                    AddressLine1 = details.ResolvedStreet,
                    City = details.ResolvedCity,
                    State = details.ResolvedState,
                    PostalCode = details.ResolvedPostalCode,
                    BirthDate = StudioTime.BodyValue(dob.ToDateTime(TimeOnly.MinValue)),
                    MobilePhone = details.ResolvedMobile,
                    ReferredBy = details.ResolvedReferral,
                    HomeLocation = new Mb.Location { Id = details.ResolvedHomeLocationId },
                },
            }), ct, passUnreadableResponse: true);

            if (!string.IsNullOrEmpty(created.Client?.Id)) return created.Client!.Id!;
            var reason = created.Errors?.FirstOrDefault()?.Message;
            if (reason != null) throw StudioException.Conflict(reason);
        }
        catch (ResponseDeserializationException)
        {
        }
        catch (StudioException)
        {
            // A concurrent request (for example from another instance of this app) may have created the client meanwhile.
            var raced = await FindClientIdAsync(email, ct);
            if (raced != null) return raced;
            throw;
        }

        return await FindClientIdAsync(email, ct)
               ?? throw new StudioException(502, "The studio system did not confirm the new client. Please try again.");
    }

    private async Task<List<Mb.Visit>> GetVisitsAsync(string clientId, DateOnly first, DateOnly last, TimeZoneInfo zone, CancellationToken ct)
    {
        var start = StudioTime.QueryInstant(first.ToDateTime(TimeOnly.MinValue), zone);
        var end = StudioTime.QueryInstant(last.ToDateTime(new TimeOnly(23, 59, 59)), zone);
        return await PageAsync(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, token) => c.ClientApi.GetClientVisits(new GetClientVisitsOperationRequest
            {
                SiteId = site,
                Authorization = token,
                RequestClientId = clientId,
                RequestStartDate = start,
                RequestEndDate = end,
                RequestLimit = limit,
                RequestOffset = offset,
            }), ct);
            return (r.Visits, r.PaginationResponse?.TotalResults);
        });
    }

    private async Task<List<Mb.Appointment>> GetAppointmentsAsync(string clientId, int? appointmentId, DateOnly first, DateOnly last,
        TimeZoneInfo zone, CancellationToken ct)
    {
        var start = StudioTime.QueryInstant(first.ToDateTime(TimeOnly.MinValue), zone);
        var end = StudioTime.QueryInstant(last.ToDateTime(new TimeOnly(23, 59, 59)), zone);
        return await PageAsync(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, token) => c.AppointmentApi.GetStaffAppointments(new GetStaffAppointmentsOperationRequest
            {
                SiteId = site,
                Authorization = token,
                RequestClientId = clientId,
                RequestAppointmentIds = appointmentId == null ? null : new[] { appointmentId.Value },
                RequestStartDate = start,
                RequestEndDate = end,
                RequestLimit = limit,
                RequestOffset = offset,
            }), ct);
            return (r.Appointments, r.PaginationResponse?.TotalResults);
        });
    }

    // ------------------------------------------------------------------------------------------------ helpers

    /// <summary>Reads every page of a Mindbody list.</summary>
    private static async Task<List<T>> PageAsync<T>(Func<int, int, Task<(IReadOnlyList<T>? Items, int? Total)>> fetch)
    {
        var all = new List<T>();
        for (var page = 0; page < MaxPages; page++)
        {
            var (items, total) = await fetch(all.Count, PageSize);
            if (items == null || items.Count == 0) break;
            all.AddRange(items);
            if (total != null && all.Count >= total) break;
            if (items.Count < PageSize && total == null) break;
        }
        return all;
    }

    private static readonly Regex HasOffset = new(@"(Z|[+-]\d{2}(:?\d{2})?)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>The studio wall-clock time of an ISO-8601 date-time. Without an offset the text is taken as studio local time.</summary>
    private static DateTime? ParseStudioTime(string text, TimeZoneInfo zone)
    {
        text = text.Trim();
        if (HasOffset.IsMatch(text.Substring(Math.Min(text.Length, 10))))
        {
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto)) return null;
            return DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(dto, zone).DateTime, DateTimeKind.Unspecified);
        }
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return null;
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }

    private static (DateOnly First, DateOnly Last) ParseRange(string? from, string? to, DateOnly defaultFrom, DateOnly defaultTo, bool allowOpenEnd)
    {
        DateOnly first = defaultFrom, last = defaultTo;
        if (!string.IsNullOrWhiteSpace(from))
        {
            if (!StudioClientDetails.TryParseDate(from, out first)) throw StudioException.BadRequest("'from' is not a valid date (use yyyy-MM-dd).");
            if (string.IsNullOrWhiteSpace(to) && allowOpenEnd) last = first.AddDays(DefaultClassRangeDays);
        }
        if (!string.IsNullOrWhiteSpace(to))
        {
            if (!StudioClientDetails.TryParseDate(to, out last)) throw StudioException.BadRequest("'to' is not a valid date (use yyyy-MM-dd).");
        }
        if (last < first) throw StudioException.BadRequest("'to' must not be before 'from'.");
        if (last.DayNumber - first.DayNumber > 366) throw StudioException.BadRequest("The date range is too long.");
        return (first, last);
    }
}
