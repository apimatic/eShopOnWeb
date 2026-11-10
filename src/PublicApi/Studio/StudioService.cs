using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MindbodyPublicApi.Models;
using MindbodyPublicApi.Requests.AppointmentApi;
using MindbodyPublicApi.Requests.ClassApi;
using MindbodyPublicApi.Requests.ClientApi;
using MindbodyPublicApi.Requests.SiteApi;
using MindbodyPublicApi.Requests.StaffApi;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// The studio features, implemented on top of Mindbody (via <see cref="MindbodyGateway"/>).
/// Mindbody reports every date-time as a wall-clock time in the studio's time zone, without an offset, so all
/// times handed back to the caller are given the studio's UTC offset (daylight-saving aware) here.
/// </summary>
public class StudioService
{
    public const int MaxRangeDays = 366;
    private const int PageSize = 200;
    private const int MaxPages = 200;
    private const int DefaultWindowDays = 7;
    private const int FallbackSessionMinutes = 60;

    // Striped locks: one shopper's first-booking / retried requests are handled one at a time, without a lock
    // table that would grow for as long as the app runs.
    private static readonly SemaphoreSlim[] ShopperLocks = Enumerable.Range(0, 128).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly MindbodyGateway _mindbody;
    private readonly UserManager<ApplicationUser> _userManager;

    public StudioService(MindbodyGateway mindbody, UserManager<ApplicationUser> userManager)
    {
        _mindbody = mindbody;
        _userManager = userManager;
    }

    // ------------------------------------------------------------------ time helpers

    private static DateTimeOffset AtStudioOffset(TimeZoneInfo zone, DateTimeOffset wallClock)
    {
        // The SDK parses Mindbody's offset-less times into a DateTimeOffset whose clock reading is the wall-clock time.
        var local = DateTime.SpecifyKind(wallClock.DateTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static DateTimeOffset NoonInstant(TimeZoneInfo zone, DateOnly day)
    {
        var local = DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private DateOnly Today(TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_mindbody.UtcNow, zone).DateTime);

    private static void CheckRange(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw StudioException.BadRequest("'from' must not be after 'to'.");
        }
        if (to.DayNumber - from.DayNumber >= MaxRangeDays)
        {
            throw StudioException.BadRequest($"The range may span at most {MaxRangeDays} days.");
        }
    }

    // ------------------------------------------------------------------ paging

    private static async Task<List<T>> PageAllAsync<T>(Func<int, int, Task<(IReadOnlyList<T>? Items, int? Total)>> fetch)
    {
        var all = new List<T>();
        var offset = 0;
        for (var page = 0; page < MaxPages; page++)
        {
            var (items, total) = await fetch(offset, PageSize).ConfigureAwait(false);
            if (items is null || items.Count == 0)
            {
                break;
            }
            all.AddRange(items);
            offset += items.Count;
            if (total is { } t ? offset >= t : items.Count < PageSize)
            {
                break;
            }
        }
        return all;
    }

    // ------------------------------------------------------------------ 1. classes

    public async Task<List<StudioClassDto>> ListClassesAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var zone = await _mindbody.GetSiteTimeZoneAsync(ct);
        var today = Today(zone);
        var start = from ?? today;
        var end = to ?? (from.HasValue ? start.AddDays(DefaultWindowDays) : today.AddDays(DefaultWindowDays));
        CheckRange(start, end);

        var classes = await FetchClassesAsync(zone, start, end, null, ct);
        var nowLocal = TimeZoneInfo.ConvertTime(_mindbody.UtcNow, zone).DateTime;

        return classes
            .Where(c => c.IsCanceled != true && c.Active != false && c.IsAvailable != false)
            .Where(c => c.StartDateTime.HasValue && c.EndDateTime.HasValue)
            .Where(c =>
            {
                var day = DateOnly.FromDateTime(c.StartDateTime!.Value.DateTime);
                return day >= start && day <= end && c.EndDateTime!.Value.DateTime > nowLocal;
            })
            .OrderBy(c => c.StartDateTime!.Value.DateTime)
            .ThenBy(c => c.Id)
            .Select(c => ToDto(zone, c))
            .ToList();
    }

    private async Task<List<Class>> FetchClassesAsync(TimeZoneInfo zone, DateOnly start, DateOnly end, int? classId, CancellationToken ct)
    {
        var startInstant = NoonInstant(zone, start);
        var endInstant = NoonInstant(zone, end);
        return await PageAllAsync<Class>(async (offset, limit) =>
        {
            var response = await _mindbody.CallAsync((client, token) => client.ClassApi.GetClasses(new GetClassesOperationRequest
            {
                SiteId = _mindbody.SiteId,
                Authorization = token,
                RequestStartDateTime = startInstant,
                RequestEndDateTime = endInstant,
                RequestClassIds = classId.HasValue ? [classId.Value] : null,
                RequestLimit = limit,
                RequestOffset = offset,
            }, cancellationToken: ct), ct).ConfigureAwait(false);
            return (response.Classes, response.PaginationResponse?.TotalResults);
        }).ConfigureAwait(false);
    }

    private static StudioClassDto ToDto(TimeZoneInfo zone, Class c) => new()
    {
        Id = c.Id ?? 0,
        Name = c.ClassDescription?.Name,
        Description = c.ClassDescription?.Description,
        StartTime = AtStudioOffset(zone, c.StartDateTime!.Value),
        EndTime = AtStudioOffset(zone, c.EndDateTime!.Value),
        LocationId = c.Location?.Id,
        LocationName = c.Location?.Name,
        // Only the trainer's name: contact details are for the front desk.
        TrainerName = c.Staff?.DisplayName ?? c.Staff?.Name,
        MaxCapacity = c.MaxCapacity,
        SpotsAvailable = c.MaxCapacity is { } max ? Math.Max(0, max - (c.TotalBooked ?? 0)) : null,
    };

    private async Task<Class?> FindClassAsync(int classId, CancellationToken ct)
    {
        var zone = await _mindbody.GetSiteTimeZoneAsync(ct);
        var today = Today(zone);
        var found = await FetchClassesAsync(zone, today.AddDays(-2), today.AddDays(MaxRangeDays - 1), classId, ct);
        return found.FirstOrDefault(c => c.Id == classId);
    }

    // ------------------------------------------------------------------ 2. session times

    public async Task<List<StudioSlotDto>> ListSlotsAsync(int sessionTypeId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        CheckRange(from, to);
        var zone = await _mindbody.GetSiteTimeZoneAsync(ct);
        var sessionType = await GetAppointmentSessionTypeAsync(sessionTypeId, ct);
        var minutes = sessionType.DefaultTimeLength ?? sessionType.StaffTimeLength ?? FallbackSessionMinutes;

        var activeTimes = await PageAllAsync<string>(async (offset, limit) =>
        {
            var response = await _mindbody.CallAsync((client, token) => client.AppointmentApi.GetActiveSessionTimes(new GetActiveSessionTimesOperationRequest
            {
                SiteId = _mindbody.SiteId,
                Authorization = token,
                RequestSessionTypeIds = [sessionTypeId],
                RequestLimit = limit,
                RequestOffset = offset,
            }, cancellationToken: ct), ct).ConfigureAwait(false);
            var perType = response.SessionTypeActiveSessionTimes?.FirstOrDefault(s => s.SessionTypeId == sessionTypeId)?.ActiveSessionTimes;
            return (perType ?? response.ActiveSessionTimes, response.PaginationResponse?.TotalResults);
        }).ConfigureAwait(false);

        var times = activeTimes
            .Select(t => TimeOnly.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? (TimeOnly?)parsed : null)
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var slots = new List<StudioSlotDto>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            foreach (var time in times)
            {
                var local = DateTime.SpecifyKind(day.ToDateTime(time), DateTimeKind.Unspecified);
                if (zone.IsInvalidTime(local))
                {
                    continue; // does not exist on the day the clocks go forward
                }
                var localEnd = local.AddMinutes(minutes);
                var endOffset = zone.IsInvalidTime(localEnd) ? zone.GetUtcOffset(local) : zone.GetUtcOffset(localEnd);
                slots.Add(new StudioSlotDto
                {
                    SessionTypeId = sessionTypeId,
                    StartTime = new DateTimeOffset(local, zone.GetUtcOffset(local)),
                    EndTime = new DateTimeOffset(localEnd, endOffset),
                });
            }
        }
        return slots;
    }

    private async Task<SessionType> GetAppointmentSessionTypeAsync(int sessionTypeId, CancellationToken ct)
    {
        var types = await PageAllAsync<SessionType>(async (offset, limit) =>
        {
            var response = await _mindbody.CallAsync((client, token) => client.SiteApi.GetSessionTypes(new GetSessionTypesOperationRequest
            {
                SiteId = _mindbody.SiteId,
                Authorization = token,
                RequestLimit = limit,
                RequestOffset = offset,
            }, cancellationToken: ct), ct).ConfigureAwait(false);
            return (response.SessionTypes, response.PaginationResponse?.TotalResults);
        }).ConfigureAwait(false);

        var type = types.FirstOrDefault(t => t.Id == sessionTypeId)
            ?? throw StudioException.NotFound($"There is no session type {sessionTypeId}.");
        if (type.Type?.Value != "Appointment")
        {
            throw StudioException.BadRequest($"Session type {sessionTypeId} ({type.Name}) is not a one-to-one session.");
        }
        return type;
    }

    // ------------------------------------------------------------------ clients

    private static SemaphoreSlim LockFor(string key) =>
        ShopperLocks[(int)((uint)StringComparer.OrdinalIgnoreCase.GetHashCode(key) % (uint)ShopperLocks.Length)];

    private async Task<T> WithShopperLockAsync<T>(string key, Func<Task<T>> work, CancellationToken ct)
    {
        var gate = LockFor(key);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await work().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<(ApplicationUser? User, string Email)> ResolveShopperAsync(string userName)
    {
        var user = await _userManager.FindByNameAsync(userName) ?? await _userManager.FindByEmailAsync(userName);
        var email = user?.Email;
        if (string.IsNullOrWhiteSpace(email) && userName.Contains('@'))
        {
            email = userName;
        }
        if (string.IsNullOrWhiteSpace(email))
        {
            throw StudioException.BadRequest("The shopper's account has no e-mail address, which the studio needs to hold a client record.");
        }
        return (user, email.Trim());
    }

    private async Task<Client?> FindClientAsync(string email, CancellationToken ct)
    {
        var matches = await PageAllAsync<ClientWithSuspensionInfo>(async (offset, limit) =>
        {
            var response = await _mindbody.CallAsync((client, token) => client.ClientApi.GetClients(new GetClientsOperationRequest
            {
                SiteId = _mindbody.SiteId,
                Authorization = token,
                RequestSearchText = email,
                RequestLimit = limit,
                RequestOffset = offset,
            }, cancellationToken: ct), ct).ConfigureAwait(false);
            return (response.Clients, response.PaginationResponse?.TotalResults);
        }).ConfigureAwait(false);

        var exact = matches.Where(c => string.Equals(c.Email?.Trim(), email, StringComparison.OrdinalIgnoreCase)).ToList();
        var chosen = exact.FirstOrDefault(c => c.Active != false) ?? exact.FirstOrDefault();
        return chosen is null ? null : new Client { Id = chosen.Id, Email = chosen.Email };
    }

    /// <summary>The shopper's studio client id; the client is created (once) from the eShop identity when missing.</summary>
    private async Task<string> EnsureClientAsync(string email, ApplicationUser? user, NewClientDetails? details, CancellationToken ct)
    {
        var existing = await FindClientAsync(email, ct);
        if (existing?.Id is { Length: > 0 } existingId)
        {
            return existingId;
        }

        var validated = await ValidateNewClientAsync(details, ct);
        var (first, last) = SplitName(details, email);
        var body = new AddClientRequest
        {
            FirstName = first,
            LastName = last,
            Email = email,
            AddressLine1 = validated.Street,
            City = validated.City,
            State = validated.State,
            PostalCode = validated.PostalCode,
            BirthDate = new DateTimeOffset(validated.BirthDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            MobilePhone = validated.Phone,
            ReferredBy = validated.Referral,
            HomeLocation = new MindbodyPublicApi.Models.Location { Id = validated.LocationId },
        };

        try
        {
            // Writes are not tied to the caller's connection: once started they run to completion.
            var created = await _mindbody.CallAsync((client, token) => client.ClientApi.AddClient(new AddClientOperationRequest
            {
                SiteId = _mindbody.SiteId,
                Authorization = token,
                Body = body,
            }), CancellationToken.None).ConfigureAwait(false);

            if (created.Client?.Id is { Length: > 0 } id)
            {
                return id;
            }
            var detail = created.Errors?.FirstOrDefault()?.Message;
            throw StudioException.BadRequest(detail ?? "The studio could not create a client record.");
        }
        catch (StudioException ex) when (ex.StatusCode == HttpStatusCode.BadRequest || ex is UnreadableReplyException)
        {
            // Another request (or the front desk) may have created the client in the meantime.
            var again = await FindClientAsync(email, ct);
            if (again?.Id is { Length: > 0 } againId)
            {
                return againId;
            }
            throw;
        }
    }

    private static (string First, string Last) SplitName(NewClientDetails? details, string email)
    {
        var first = details?.FirstName?.Trim();
        var last = details?.LastName?.Trim();
        var local = email.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        if (string.IsNullOrEmpty(first))
        {
            first = Capitalize(parts.Length > 0 ? parts[0] : local);
        }
        if (string.IsNullOrEmpty(last))
        {
            last = parts.Length > 1 ? Capitalize(parts[^1]) : "Shopper";
        }
        return (first, last);

        static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }

    private record ValidatedDetails(int LocationId, string Street, string City, string State, string PostalCode, DateOnly BirthDate, string Phone, string Referral);

    private async Task<ValidatedDetails> ValidateNewClientAsync(NewClientDetails? d, CancellationToken ct)
    {
        var missing = new List<string>();
        if (d?.HomeLocationId is null) missing.Add("homeLocationId");
        if (string.IsNullOrWhiteSpace(d?.StreetAddress)) missing.Add("streetAddress");
        if (string.IsNullOrWhiteSpace(d?.City)) missing.Add("city");
        if (string.IsNullOrWhiteSpace(d?.State)) missing.Add("state");
        if (string.IsNullOrWhiteSpace(d?.PostalCode)) missing.Add("postalCode");
        if (d?.DateOfBirth is null) missing.Add("dateOfBirth");
        if (string.IsNullOrWhiteSpace(d?.MobilePhone)) missing.Add("mobilePhone");
        if (string.IsNullOrWhiteSpace(d?.ReferredBy)) missing.Add("referredBy");
        if (missing.Count > 0)
        {
            throw StudioException.BadRequest(
                "This is the shopper's first booking, so the studio needs their details: missing " + string.Join(", ", missing) + ".");
        }

        var dob = d!.DateOfBirth!.Value;
        var todayUtc = DateOnly.FromDateTime(_mindbody.UtcNow.UtcDateTime);
        if (dob > todayUtc || dob.Year < 1900)
        {
            throw StudioException.BadRequest("dateOfBirth is not a plausible date of birth.");
        }

        var phone = d.MobilePhone!.Trim();
        var digits = phone.Count(char.IsDigit);
        if (digits < 7 || digits > 15 || !Regex.IsMatch(phone, @"^[0-9+\-\s().]+$"))
        {
            throw StudioException.BadRequest("mobilePhone is not a valid phone number.");
        }

        var locations = await _mindbody.CallAsync((client, token) => client.SiteApi.GetLocations(new GetLocationsOperationRequest
        {
            SiteId = _mindbody.SiteId,
            Authorization = token,
        }, cancellationToken: ct), ct).ConfigureAwait(false);
        if (locations.Locations?.Any(l => l.Id == d.HomeLocationId) != true)
        {
            throw StudioException.BadRequest($"homeLocationId {d.HomeLocationId} is not one of the studio's locations.");
        }

        var referrals = await _mindbody.CallAsync((client, token) => client.ClientApi.GetClientReferralTypes(new GetClientReferralTypesOperationRequest
        {
            SiteId = _mindbody.SiteId,
            Authorization = token,
        }, cancellationToken: ct), ct).ConfigureAwait(false);
        var referral = referrals.ReferralTypes?.FirstOrDefault(r => string.Equals(r, d.ReferredBy!.Trim(), StringComparison.OrdinalIgnoreCase));
        if (referral is null)
        {
            var allowed = string.Join(", ", referrals.ReferralTypes ?? Array.Empty<string>());
            throw StudioException.BadRequest($"referredBy must be one of: {allowed}.");
        }

        return new ValidatedDetails(d.HomeLocationId!.Value, d.StreetAddress!.Trim(), d.City!.Trim(), d.State!.Trim(), d.PostalCode!.Trim(), dob, phone, referral);
    }

    private async Task<List<VisitWithWaitlistInfo>> GetScheduleAsync(string clientId, TimeZoneInfo zone, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var startInstant = NoonInstant(zone, from);
        var endInstant = NoonInstant(zone, to);
        var visits = await PageAllAsync<VisitWithWaitlistInfo>(async (offset, limit) =>
        {
            var response = await _mindbody.CallAsync((client, token) => client.ClientApi.GetClientSchedule(new GetClientScheduleOperationRequest
            {
                SiteId = _mindbody.SiteId,
                Authorization = token,
                RequestClientId = clientId,
                RequestStartDate = startInstant,
                RequestEndDate = endInstant,
                RequestLimit = limit,
                RequestOffset = offset,
            }, cancellationToken: ct), ct).ConfigureAwait(false);
            return (response.Visits, response.PaginationResponse?.TotalResults);
        }).ConfigureAwait(false);

        return visits.Where(v => !IsCancelled(v.AppointmentStatus?.Value)).ToList();
    }

    private static bool IsCancelled(string? status) => status is "Cancelled" or "LateCancelled";

    // ------------------------------------------------------------------ 3. book a class

    public Task<ClassBookingDto> BookClassAsync(string userName, ClassBookingRequest request, CancellationToken ct) =>
        BookClassForShopperAsync(userName, request.ClassId, request, shopperRules: true, ct);

    public async Task<ClassBookingDto> BookClassForShopperAsync(string shopperUserName, int classId, NewClientDetails? details, bool shopperRules, CancellationToken ct)
    {
        if (classId <= 0)
        {
            throw StudioException.BadRequest("classId is required.");
        }

        var (user, email) = await ResolveShopperAsync(shopperUserName);
        return await WithShopperLockAsync(email, async () =>
        {
            var zone = await _mindbody.GetSiteTimeZoneAsync(ct);
            var cls = await FindClassAsync(classId, ct)
                ?? throw StudioException.NotFound($"There is no class {classId} in the timetable.");
            if (cls.IsCanceled == true)
            {
                throw StudioException.Conflict("That class has been cancelled.");
            }
            if (cls.StartDateTime is null || cls.EndDateTime is null)
            {
                throw StudioException.BadRequest("That class has no schedule.");
            }
            if (shopperRules)
            {
                var nowLocal = TimeZoneInfo.ConvertTime(_mindbody.UtcNow, zone).DateTime;
                if (cls.EndDateTime.Value.DateTime <= nowLocal)
                {
                    throw StudioException.Conflict("That class is over.");
                }
                if (cls.Active == false || cls.IsAvailable == false)
                {
                    throw StudioException.Conflict("That class is not open for booking.");
                }
            }

            var clientId = await EnsureClientAsync(email, user, details, ct);

            // Mindbody does not de-duplicate: a repeated request must find the booking it already made.
            var classDay = DateOnly.FromDateTime(cls.StartDateTime.Value.DateTime);
            var schedule = await GetScheduleAsync(clientId, zone, classDay.AddDays(-1), classDay.AddDays(1), ct);
            var held = schedule.FirstOrDefault(v => v.ClassId == classId && v.Id is > 0);
            if (held is not null)
            {
                return ToClassBooking(zone, held.Id!.Value, cls, clientId, alreadyBooked: true);
            }

            long visitId;
            try
            {
                var added = await _mindbody.CallAsync((client, token) => client.ClassApi.AddClientToClass(new AddClientToClassOperationRequest
                {
                    SiteId = _mindbody.SiteId,
                    Authorization = token,
                    Body = new AddClientToClassRequest { ClientId = clientId, ClassId = classId },
                }), CancellationToken.None).ConfigureAwait(false);
                visitId = added.Visit?.Id ?? throw new StudioException(HttpStatusCode.BadGateway, "The studio did not confirm the booking.");
            }
            catch (UnreadableReplyException)
            {
                // The booking was made; find it again.
                var after = await GetScheduleAsync(clientId, zone, classDay.AddDays(-1), classDay.AddDays(1), ct);
                visitId = after.FirstOrDefault(v => v.ClassId == classId && v.Id is > 0)?.Id
                    ?? throw new StudioException(HttpStatusCode.BadGateway, "The studio did not confirm the booking.");
            }
            return ToClassBooking(zone, visitId, cls, clientId, alreadyBooked: false);
        }, ct);
    }

    private static ClassBookingDto ToClassBooking(TimeZoneInfo zone, long bookingId, Class cls, string clientId, bool alreadyBooked) => new()
    {
        BookingId = bookingId,
        ClassId = cls.Id ?? 0,
        ClassName = cls.ClassDescription?.Name,
        StartTime = cls.StartDateTime is { } s ? AtStudioOffset(zone, s) : null,
        EndTime = cls.EndDateTime is { } e ? AtStudioOffset(zone, e) : null,
        LocationId = cls.Location?.Id,
        ClientId = clientId,
        AlreadyBooked = alreadyBooked,
    };

    /// <summary>Resolves the eShop shopper named by a front-desk operator.</summary>
    public async Task<string> ResolveShopperUserNameAsync(string? shopper)
    {
        if (string.IsNullOrWhiteSpace(shopper))
        {
            throw StudioException.BadRequest("shopper is required.");
        }
        var user = await _userManager.FindByNameAsync(shopper.Trim()) ?? await _userManager.FindByEmailAsync(shopper.Trim());
        return user?.UserName ?? throw StudioException.NotFound($"There is no shopper '{shopper}'.");
    }

    // ------------------------------------------------------------------ 3. book a one-to-one session

    public async Task<AppointmentBookingDto> BookAppointmentAsync(string userName, AppointmentBookingRequest request, CancellationToken ct)
    {
        if (request.SessionTypeId <= 0)
        {
            throw StudioException.BadRequest("sessionTypeId is required.");
        }
        if (request.StartTime is not { } startTime)
        {
            throw StudioException.BadRequest("startTime is required (ISO-8601 with UTC offset).");
        }

        var zone = await _mindbody.GetSiteTimeZoneAsync(ct);
        if (startTime <= _mindbody.UtcNow)
        {
            throw StudioException.Conflict("That time has already passed.");
        }
        var sessionType = await GetAppointmentSessionTypeAsync(request.SessionTypeId, ct);
        var (user, email) = await ResolveShopperAsync(userName);
        var startLocal = DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(startTime, zone).DateTime, DateTimeKind.Unspecified);
        var day = DateOnly.FromDateTime(startLocal);

        return await WithShopperLockAsync(email, async () =>
        {
            var clientId = await EnsureClientAsync(email, user, request, ct);

            // A repeated request must find the appointment it already booked.
            var schedule = await GetScheduleAsync(clientId, zone, day, day, ct);
            _mindbody.DebugLog("DBG want " + startLocal.ToString("o") + " type " + sessionType.Name + " sched " + string.Join(";", schedule.Select(v => v.AppointmentId + "/" + v.StartDateTime?.DateTime.ToString("o") + "/" + v.Name)));
            var held = schedule.FirstOrDefault(v =>
                v.AppointmentId is > 0 &&
                v.StartDateTime is { } vs && vs.DateTime == startLocal &&
                (string.IsNullOrEmpty(v.Name) || string.IsNullOrEmpty(sessionType.Name) ||
                 string.Equals(v.Name, sessionType.Name, StringComparison.OrdinalIgnoreCase)));
            if (held is not null)
            {
                return new AppointmentBookingDto
                {
                    BookingId = held.AppointmentId!.Value,
                    SessionTypeId = sessionType.Id,
                    SessionTypeName = sessionType.Name,
                    TrainerId = held.StaffId,
                    LocationId = held.LocationId,
                    StartTime = AtStudioOffset(zone, held.StartDateTime!.Value),
                    EndTime = held.EndDateTime is { } he ? AtStudioOffset(zone, he) : AtStudioOffset(zone, held.StartDateTime!.Value).AddMinutes(sessionType.DefaultTimeLength ?? FallbackSessionMinutes),
                    ClientId = clientId,
                    AlreadyBooked = true,
                };
            }

            var candidates = await FindTrainersAsync(sessionType, request.TrainerId, day, startLocal, zone, ct);
            if (candidates.Count == 0)
            {
                throw StudioException.Conflict(request.TrainerId.HasValue
                    ? "That trainer is not free at that time."
                    : "No trainer is free at that time.");
            }

            StudioException? last = null;
            foreach (var (staffId, locationId) in candidates)
            {
                try
                {
                    var added = await _mindbody.CallAsync((client, token) => client.AppointmentApi.AddAppointment(new AddAppointmentOperationRequest
                    {
                        SiteId = _mindbody.SiteId,
                        Authorization = token,
                        Body = new AddAppointmentRequest
                        {
                            ClientId = clientId,
                            LocationId = locationId,
                            SessionTypeId = sessionType.Id,
                            StaffId = staffId,
                            StaffRequested = request.TrainerId.HasValue,
                            StartDateTime = startTime,
                        },
                    }), CancellationToken.None).ConfigureAwait(false);

                    var appt = added.Appointment ?? throw new StudioException(HttpStatusCode.BadGateway, "The studio did not confirm the booking.");
                    var start = appt.StartDateTime is { } s ? AtStudioOffset(zone, s) : startTime.ToOffset(zone.GetUtcOffset(startLocal));
                    return new AppointmentBookingDto
                    {
                        BookingId = appt.Id ?? throw new StudioException(HttpStatusCode.BadGateway, "The studio did not confirm the booking."),
                        SessionTypeId = sessionType.Id,
                        SessionTypeName = sessionType.Name,
                        TrainerId = appt.StaffId ?? staffId,
                        LocationId = appt.LocationId ?? locationId,
                        StartTime = start,
                        EndTime = appt.EndDateTime is { } e ? AtStudioOffset(zone, e) : start.AddMinutes(appt.Duration ?? sessionType.DefaultTimeLength ?? FallbackSessionMinutes),
                        ClientId = clientId,
                    };
                }
                catch (StudioException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
                {
                    last = ex; // this trainer cannot take it; try the next one
                }
            }

            throw last is null ? StudioException.Conflict("No trainer is free at that time.") : StudioException.Conflict(last.Message);
        }, ct);
    }

    private async Task<List<(long StaffId, int LocationId)>> FindTrainersAsync(SessionType sessionType, long? trainerId, DateOnly day, DateTime startLocal, TimeZoneInfo zone, CancellationToken ct)
    {
        var noon = NoonInstant(zone, day);
        var availabilities = await PageAllAsync<Availability>(async (offset, limit) =>
        {
            var response = await _mindbody.CallAsync((client, token) => client.AppointmentApi.GetBookableItems(new GetBookableItemsOperationRequest
            {
                SiteId = _mindbody.SiteId,
                Authorization = token,
                RequestSessionTypeIds = [sessionType.Id!.Value],
                RequestStartDate = noon,
                RequestEndDate = noon,
                RequestStaffIds = trainerId.HasValue ? [trainerId.Value] : null,
                RequestLimit = limit,
                RequestOffset = offset,
            }, cancellationToken: ct), ct).ConfigureAwait(false);
            return (response.Availabilities, response.PaginationResponse?.TotalResults);
        }).ConfigureAwait(false);

        return availabilities
            .Where(a => a.Staff?.Id is not null && a.Location?.Id is not null && a.StartDateTime.HasValue && a.EndDateTime.HasValue)
            .Where(a => a.StartDateTime!.Value.DateTime <= startLocal &&
                        startLocal <= (a.BookableEndDateTime ?? a.EndDateTime)!.Value.DateTime)
            .Select(a => (StaffId: a.Staff!.Id!.Value, LocationId: a.Location!.Id!.Value))
            .Distinct()
            .ToList();
    }

    // ------------------------------------------------------------------ 4. cancel

    public async Task<CancelBookingDto> CancelClassBookingAsync(string userName, long bookingId, CancellationToken ct)
    {
        var (_, email) = await ResolveShopperAsync(userName);
        return await WithShopperLockAsync(email, async () =>
        {
            var client = await FindClientAsync(email, ct);
            if (client?.Id is not { Length: > 0 } clientId)
            {
                return new CancelBookingDto { BookingId = bookingId };
            }

            var zone = await _mindbody.GetSiteTimeZoneAsync(ct);
            var today = Today(zone);
            var schedule = await GetScheduleAsync(clientId, zone, today.AddDays(-1), today.AddDays(MaxRangeDays), ct);
            var visit = schedule.FirstOrDefault(v => v.Id == bookingId && v.ClassId is > 0);
            if (visit is null)
            {
                // Not (or no longer) one of this shopper's bookings: a repeated cancellation is a success.
                return new CancelBookingDto { BookingId = bookingId };
            }

            try
            {
                await _mindbody.CallAsync((c, token) => c.ClassApi.RemoveClientFromClass(new RemoveClientFromClassOperationRequest
                {
                    SiteId = _mindbody.SiteId,
                    Authorization = token,
                    Body = new RemoveClientFromClassRequest { ClientId = clientId, ClassId = visit.ClassId!.Value, VisitId = (int)bookingId },
                }), CancellationToken.None).ConfigureAwait(false);
            }
            catch (UnreadableReplyException)
            {
                // Mindbody carried the cancellation out; only its reply was unreadable.
            }
            return new CancelBookingDto { BookingId = bookingId };
        }, ct);
    }

    public async Task<CancelBookingDto> CancelAppointmentBookingAsync(string userName, long bookingId, CancellationToken ct)
    {
        var (_, email) = await ResolveShopperAsync(userName);
        return await WithShopperLockAsync(email, async () =>
        {
            var client = await FindClientAsync(email, ct);
            if (client?.Id is not { Length: > 0 } clientId)
            {
                return new CancelBookingDto { BookingId = bookingId };
            }

            var zone = await _mindbody.GetSiteTimeZoneAsync(ct);
            var today = Today(zone);
            var schedule = await GetScheduleAsync(clientId, zone, today.AddDays(-1), today.AddDays(MaxRangeDays), ct);
            var appointment = schedule.FirstOrDefault(v => v.AppointmentId == bookingId && v.AppointmentId > 0);
            if (appointment is null)
            {
                return new CancelBookingDto { BookingId = bookingId };
            }

            try
            {
                await _mindbody.CallAsync((c, token) => c.AppointmentApi.UpdateAppointment(new UpdateAppointmentOperationRequest
                {
                    SiteId = _mindbody.SiteId,
                    Authorization = token,
                    Body = new UpdateAppointmentRequest { AppointmentId = bookingId, Execute = "Cancel" },
                }), CancellationToken.None).ConfigureAwait(false);
            }
            catch (UnreadableReplyException)
            {
                // Mindbody carried the cancellation out; only its reply was unreadable.
            }
            return new CancelBookingDto { BookingId = bookingId };
        }, ct);
    }

    // ------------------------------------------------------------------ 5. trainers

    public async Task<List<StudioTrainerDto>> ListTrainersAsync(CancellationToken ct)
    {
        var staff = await PageAllAsync<Staff>(async (offset, limit) =>
        {
            var response = await _mindbody.CallAsync((client, token) => client.StaffApi.GetStaff(new GetStaffOperationRequest
            {
                SiteId = _mindbody.SiteId,
                Authorization = token,
                RequestLimit = limit,
                RequestOffset = offset,
            }, cancellationToken: ct), ct).ConfigureAwait(false);
            return (response.StaffMembers, response.PaginationResponse?.TotalResults);
        }).ConfigureAwait(false);

        return staff
            .Where(s => s.Id is > 0 && (s.ClassTeacher == true || s.AppointmentInstructor == true))
            .OrderBy(s => s.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.FirstName, StringComparer.OrdinalIgnoreCase)
            .Select(s => new StudioTrainerDto
            {
                Id = s.Id!.Value,
                FirstName = s.FirstName,
                LastName = s.LastName,
                DisplayName = s.DisplayName ?? s.Name,
                Email = s.Email,
                MobilePhone = s.MobilePhone,
                HomePhone = s.HomePhone,
                WorkPhone = s.WorkPhone,
                Address = s.Address,
                City = s.City,
                State = s.State,
                PostalCode = s.PostalCode,
                Country = s.Country,
                Bio = s.Bio,
                ImageUrl = s.ImageUrl,
                Active = s.Active != false,
                TeachesClasses = s.ClassTeacher == true,
                TakesAppointments = s.AppointmentInstructor == true,
            })
            .ToList();
    }
}
