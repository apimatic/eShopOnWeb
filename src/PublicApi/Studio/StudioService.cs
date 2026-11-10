using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using MindbodyPublicApi;
using MindbodyPublicApi.Core;
using MindbodyPublicApi.Core.Models;
using MindbodyPublicApi.Models;
using MindbodyPublicApi.Requests.AppointmentApi;
using MindbodyPublicApi.Requests.ClassApi;
using MindbodyPublicApi.Requests.ClientApi;
using MindbodyPublicApi.Requests.SiteApi;
using MindbodyPublicApi.Requests.StaffApi;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// The studio's classes, session times and bookings, all held in Mindbody.
/// </summary>
/// <remarks>
/// Mindbody sends and expects times as the studio's wall clock without an offset, while the SDK parses
/// them into <see cref="DateTimeOffset"/> values carrying the machine's offset. So only the clock part
/// of a Mindbody time is used here, and the studio's real offset is applied for that very date.
/// </remarks>
public sealed class StudioService
{
    private const int MaxRangeDays = 92;
    private const int MaxPages = 200;
    private const int PageSize = 100;
    // Dates sent to Mindbody are padded and filtered again here: the SDK sends them as UTC instants,
    // which Mindbody does not read as the studio's local days.
    private const int QueryPaddingDays = 3;
    private static readonly TimeSpan CancelledMemory = TimeSpan.FromDays(7);

    private readonly MindbodyConnection _mb;
    private readonly TimeProvider _clock;
    private readonly IMemoryCache _memory;
    private readonly KeyedLock _clientLocks = new();
    private readonly TtlCache<TimeZoneInfo> _timeZone;
    private readonly TtlCache<IReadOnlyDictionary<int, SessionType>> _sessionTypes;
    private readonly TtlCache<IReadOnlyDictionary<int, Location>> _locations;
    private readonly TtlCache<IReadOnlyList<string>> _referralTypes;

    public StudioService(MindbodyConnection mb, TimeProvider clock, IMemoryCache memory)
    {
        _mb = mb;
        _clock = clock;
        _memory = memory;
        _timeZone = new TtlCache<TimeZoneInfo>(TimeSpan.FromHours(1), clock);
        _sessionTypes = new TtlCache<IReadOnlyDictionary<int, SessionType>>(TimeSpan.FromMinutes(10), clock);
        _locations = new TtlCache<IReadOnlyDictionary<int, Location>>(TimeSpan.FromMinutes(10), clock);
        _referralTypes = new TtlCache<IReadOnlyList<string>>(TimeSpan.FromHours(1), clock);
    }

    // ---------------------------------------------------------------------------------------------
    // Time
    // ---------------------------------------------------------------------------------------------

    private Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken ct) => _timeZone.GetAsync(async token =>
    {
        var response = await _mb.CallAsync((client, siteId, auth) => client.SiteApi.GetSites(new GetSitesOperationRequest
        {
            RequestSiteIds = new[] { int.Parse(siteId, CultureInfo.InvariantCulture) },
            Authorization = auth,
            // The sites call has no site id of its own, and the staff token only works for the site it was issued for.
        }, new RequestOptions { Headers = new[] { new HeaderParam("siteId", siteId) } }, token), token).ConfigureAwait(false);

        var id = response.Sites?.FirstOrDefault()?.TimeZone;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not say which time zone the studio is in.");
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new StudioException(HttpStatusCode.BadGateway, $"The studio's time zone '{id}' is not known to this server.", ex);
        }
    }, ct);

    /// <summary>The studio's wall clock now.</summary>
    private DateTime LocalNow(TimeZoneInfo tz) => TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), tz).DateTime;

    /// <summary>The first instant of a studio wall-clock time, with the studio's UTC offset for that date.</summary>
    internal static DateTimeOffset ToStudioTime(TimeZoneInfo tz, DateTime wallClock)
    {
        wallClock = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(wallClock))
        {
            // The clock skipped this time (start of daylight saving): it means the same as an hour later.
            wallClock = wallClock.AddHours(1);
        }

        var offset = tz.IsAmbiguousTime(wallClock)
            ? tz.GetAmbiguousTimeOffsets(wallClock).Max()
            : tz.GetUtcOffset(wallClock);
        return new DateTimeOffset(wallClock, offset);
    }

    /// <summary>The wall-clock part of a time received from Mindbody.</summary>
    private static DateTime Wall(DateTimeOffset? value) =>
        DateTime.SpecifyKind(value!.Value.DateTime, DateTimeKind.Unspecified);

    /// <summary>A studio wall-clock time in the form the SDK sends to Mindbody (see remarks).</summary>
    private static DateTimeOffset ForMindbody(DateTime wallClock) =>
        new(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified), TimeSpan.Zero);

    private static DateTimeOffset QueryDate(DateOnly date) => ForMindbody(date.ToDateTime(TimeOnly.MinValue));

    // ---------------------------------------------------------------------------------------------
    // Reference data
    // ---------------------------------------------------------------------------------------------

    private Task<IReadOnlyDictionary<int, SessionType>> GetSessionTypesAsync(CancellationToken ct) =>
        _sessionTypes.GetAsync(async token =>
        {
            var all = await FetchAllAsync(async (offset, limit) =>
            {
                var r = await _mb.CallAsync((c, site, auth) => c.SiteApi.GetSessionTypes(new GetSessionTypesOperationRequest
                {
                    RequestLimit = limit,
                    RequestOffset = offset,
                    SiteId = site,
                    Authorization = auth,
                }, cancellationToken: token), token).ConfigureAwait(false);
                return (r.SessionTypes, r.PaginationResponse?.TotalResults);
            }).ConfigureAwait(false);
            return (IReadOnlyDictionary<int, SessionType>)all.Where(s => s.Id.HasValue)
                .GroupBy(s => s.Id!.Value).ToDictionary(g => g.Key, g => g.First());
        }, ct);

    private Task<IReadOnlyDictionary<int, Location>> GetLocationsAsync(CancellationToken ct) =>
        _locations.GetAsync(async token =>
        {
            var all = await FetchAllAsync(async (offset, limit) =>
            {
                var r = await _mb.CallAsync((c, site, auth) => c.SiteApi.GetLocations(new GetLocationsOperationRequest
                {
                    RequestLimit = limit,
                    RequestOffset = offset,
                    SiteId = site,
                    Authorization = auth,
                }, cancellationToken: token), token).ConfigureAwait(false);
                return (r.Locations, r.PaginationResponse?.TotalResults);
            }).ConfigureAwait(false);
            return (IReadOnlyDictionary<int, Location>)all.Where(l => l.Id.HasValue)
                .GroupBy(l => l.Id!.Value).ToDictionary(g => g.Key, g => g.First());
        }, ct);

    private Task<IReadOnlyList<string>> GetReferralTypesAsync(CancellationToken ct) =>
        _referralTypes.GetAsync(async token =>
        {
            var r = await _mb.CallAsync((c, site, auth) => c.ClientApi.GetClientReferralTypes(
                new GetClientReferralTypesOperationRequest { SiteId = site, Authorization = auth },
                cancellationToken: token), token).ConfigureAwait(false);
            return (IReadOnlyList<string>)(r.ReferralTypes ?? Array.Empty<string>()).ToList();
        }, ct);

    /// <summary>Reads every page of a Mindbody list.</summary>
    private static async Task<List<T>> FetchAllAsync<T>(
        Func<int, int, Task<(IReadOnlyList<T>? Items, int? Total)>> fetchPage)
    {
        var result = new List<T>();
        for (var page = 0; page < MaxPages; page++)
        {
            var (items, total) = await fetchPage(result.Count, PageSize).ConfigureAwait(false);
            if (items is null || items.Count == 0)
            {
                break;
            }

            result.AddRange(items);
            if (total.HasValue && result.Count >= total.Value)
            {
                break;
            }
        }

        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // 1. Classes
    // ---------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<StudioClassDto>> ListClassesAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var tz = await GetTimeZoneAsync(ct).ConfigureAwait(false);
        var nowWall = LocalNow(tz);
        var today = DateOnly.FromDateTime(nowWall);

        DateOnly start, end;
        if (from is null && to is null)
        {
            start = today;
            end = today.AddDays(7);
        }
        else
        {
            start = from ?? today;
            end = to ?? start.AddDays(7);
        }

        ValidateRange(start, end);

        var classes = await FetchClassesAsync(
            start.AddDays(-QueryPaddingDays), end.AddDays(QueryPaddingDays + 1), null, ct).ConfigureAwait(false);

        return classes
            .Where(c => c.StartDateTime.HasValue && c.EndDateTime.HasValue)
            .Where(c =>
            {
                var day = DateOnly.FromDateTime(Wall(c.StartDateTime));
                return day >= start && day <= end;
            })
            .Where(c => IsAttendable(c, nowWall))
            .OrderBy(c => Wall(c.StartDateTime))
            .ThenBy(c => c.Id)
            .Select(c => ToDto(c, tz))
            .ToList();
    }

    private static void ValidateRange(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw StudioException.BadRequest("'to' must not be before 'from'.");
        }

        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
        {
            throw StudioException.BadRequest($"The range may cover at most {MaxRangeDays} days.");
        }
    }

    private async Task<List<Class>> FetchClassesAsync(
        DateOnly from, DateOnly to, IReadOnlyList<int>? classIds, CancellationToken ct)
    {
        var all = await FetchAllAsync(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, auth) => c.ClassApi.GetClasses(new GetClassesOperationRequest
            {
                RequestStartDateTime = QueryDate(from),
                RequestEndDateTime = QueryDate(to),
                RequestClassIds = classIds,
                RequestHideCanceledClasses = false,
                RequestLimit = limit,
                RequestOffset = offset,
                SiteId = site,
                Authorization = auth,
            }, cancellationToken: ct), ct).ConfigureAwait(false);
            return (r.Classes, r.PaginationResponse?.TotalResults);
        }).ConfigureAwait(false);
        return all.GroupBy(c => c.Id).Select(g => g.First()).ToList();
    }

    /// <summary>A class a shopper can attend: it runs, has not started, can still be booked, and is a class (not a course).</summary>
    private static bool IsAttendable(Class c, DateTime nowWall)
    {
        if (c.IsCanceled == true || c.Active == false || c.IsAvailable == false)
        {
            return false;
        }

        var scheduleType = c.ClassDescription?.Program?.ScheduleType?.Value;
        if (scheduleType is not null && !string.Equals(scheduleType, "Class", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Wall(c.StartDateTime) > nowWall;
    }

    private static StudioClassDto ToDto(Class c, TimeZoneInfo tz) => new()
    {
        Id = c.Id ?? 0,
        Name = c.ClassDescription?.Name,
        Description = string.IsNullOrWhiteSpace(c.ClassDescription?.Description) ? null : c.ClassDescription!.Description,
        StartTime = ToStudioTime(tz, Wall(c.StartDateTime)),
        EndTime = ToStudioTime(tz, Wall(c.EndDateTime)),
        LocationId = c.Location?.Id,
        LocationName = c.Location?.Name,
        Trainer = c.Staff?.DisplayName ?? c.Staff?.Name,
        SpotsLeft = c.MaxCapacity.HasValue ? Math.Max(0, c.MaxCapacity.Value - (c.TotalBooked ?? 0)) : null,
    };

    // ---------------------------------------------------------------------------------------------
    // 2. Session times
    // ---------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<StudioSlotDto>> ListSlotsAsync(int sessionTypeId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        ValidateRange(from, to);
        var tz = await GetTimeZoneAsync(ct).ConfigureAwait(false);
        var sessionType = await GetAppointmentSessionTypeAsync(sessionTypeId, ct).ConfigureAwait(false);
        var length = RequireLength(sessionType);
        var times = await GetActiveSessionTimesAsync(sessionTypeId, ct).ConfigureAwait(false);
        var now = _clock.GetUtcNow();

        var slots = new List<StudioSlotDto>();
        // Days are counted as calendar dates, never as 24-hour steps, so daylight saving changes cannot skip or repeat a day.
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            foreach (var time in times)
            {
                var wall = day.ToDateTime(time);
                if (tz.IsInvalidTime(wall))
                {
                    continue;
                }

                var starts = tz.IsAmbiguousTime(wall)
                    ? tz.GetAmbiguousTimeOffsets(wall).OrderByDescending(o => o).Select(o => new DateTimeOffset(wall, o))
                    : new[] { new DateTimeOffset(wall, tz.GetUtcOffset(wall)) };
                foreach (var start in starts)
                {
                    if (start <= now)
                    {
                        continue;
                    }

                    slots.Add(new StudioSlotDto
                    {
                        SessionTypeId = sessionTypeId,
                        StartTime = start,
                        // The end is the length of the session later in real time, which is what a shopper experiences.
                        EndTime = TimeZoneInfo.ConvertTime(start.AddMinutes(length), tz),
                    });
                }
            }
        }

        return slots.OrderBy(s => s.StartTime).ToList();
    }

    private async Task<SessionType> GetAppointmentSessionTypeAsync(int sessionTypeId, CancellationToken ct)
    {
        var types = await GetSessionTypesAsync(ct).ConfigureAwait(false);
        if (!types.TryGetValue(sessionTypeId, out var type))
        {
            throw StudioException.NotFound($"There is no session type {sessionTypeId}.");
        }

        if (!string.Equals(type.Type?.Value, "Appointment", StringComparison.OrdinalIgnoreCase))
        {
            throw StudioException.BadRequest($"Session type {sessionTypeId} is not a one-to-one session type.");
        }

        return type;
    }

    private static int RequireLength(SessionType type)
    {
        var length = type.DefaultTimeLength ?? type.StaffTimeLength;
        if (length is null or <= 0)
        {
            throw new StudioException(HttpStatusCode.UnprocessableEntity,
                $"Session type {type.Id} has no length set in the studio, so its times cannot be listed.");
        }

        return length.Value;
    }

    private async Task<IReadOnlyList<TimeOnly>> GetActiveSessionTimesAsync(int sessionTypeId, CancellationToken ct)
    {
        var all = await FetchAllAsync(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, auth) => c.AppointmentApi.GetActiveSessionTimes(
                new GetActiveSessionTimesOperationRequest
                {
                    RequestSessionTypeIds = new[] { sessionTypeId },
                    RequestLimit = limit,
                    RequestOffset = offset,
                    SiteId = site,
                    Authorization = auth,
                }, cancellationToken: ct), ct).ConfigureAwait(false);
            return (r.ActiveSessionTimes, r.PaginationResponse?.TotalResults);
        }).ConfigureAwait(false);

        var times = new SortedSet<TimeOnly>();
        foreach (var text in all)
        {
            if (TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                times.Add(time);
            }
        }

        return times.ToList();
    }

    // ---------------------------------------------------------------------------------------------
    // Clients
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The studio client id of an eShop user. It is fixed for a user name (which is unique and stable in eShop), so the
    /// client is created once and found again by every later request, even after the identity store was rebuilt.
    /// </summary>
    internal static string ClientIdOf(Shopper shopper)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(shopper.UserName.Trim().ToLowerInvariant()));
        return "eshop" + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    private async Task<ClientWithSuspensionInfo?> FindClientAsync(string clientId, CancellationToken ct)
    {
        var r = await _mb.CallAsync((c, site, auth) => c.ClientApi.GetClients(new GetClientsOperationRequest
        {
            RequestClientIDs = new[] { clientId },
            SiteId = site,
            Authorization = auth,
        }, cancellationToken: ct), ct).ConfigureAwait(false);
        return r.Clients?.FirstOrDefault(c => string.Equals(c.Id, clientId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds the shopper's studio client, creating it from their eShop identity and the details given when
    /// they are new. The caller holds the shopper's lock.
    /// </summary>
    private async Task<string> EnsureClientAsync(Shopper shopper, NewClientDetails? details, CancellationToken ct)
    {
        var clientId = ClientIdOf(shopper);
        if (await FindClientAsync(clientId, ct).ConfigureAwait(false) is not null)
        {
            return clientId;
        }

        var request = await ValidateNewClientAsync(shopper, details, clientId, ct).ConfigureAwait(false);
        try
        {
            await _mb.CallAsync((c, site, auth) => c.ClientApi.AddClient(new AddClientOperationRequest
            {
                SiteId = site,
                Authorization = auth,
                Body = request,
            }, cancellationToken: ct), ct).ConfigureAwait(false);
        }
        catch (StudioException ex) when (ex.ReplyUnreadable)
        {
            // The client was most likely created; make sure.
            if (await FindClientAsync(clientId, ct).ConfigureAwait(false) is not null)
            {
                return clientId;
            }

            throw;
        }
        catch (StudioException ex) when (ex.StatusCode == 422)
        {
            // Another request may have created the client a moment ago.
            if (await FindClientAsync(clientId, ct).ConfigureAwait(false) is not null)
            {
                return clientId;
            }

            throw new StudioException(HttpStatusCode.Conflict,
                "The studio could not register you as a client: " + ex.Message, ex);
        }

        return clientId;
    }

    private async Task<AddClientRequest> ValidateNewClientAsync(Shopper shopper, NewClientDetails? d, string clientId, CancellationToken ct)
    {
        d ??= new NewClientDetails();
        var missing = new List<string>();
        if (d.HomeLocationId is null) { missing.Add("homeLocationId"); }
        if (string.IsNullOrWhiteSpace(d.StreetAddress)) { missing.Add("streetAddress"); }
        if (string.IsNullOrWhiteSpace(d.City)) { missing.Add("city"); }
        if (string.IsNullOrWhiteSpace(d.State)) { missing.Add("state"); }
        if (string.IsNullOrWhiteSpace(d.PostalCode)) { missing.Add("postalCode"); }
        if (d.DateOfBirth is null) { missing.Add("dateOfBirth"); }
        if (string.IsNullOrWhiteSpace(d.MobilePhone)) { missing.Add("mobilePhone"); }
        if (string.IsNullOrWhiteSpace(d.ReferralSource)) { missing.Add("referralSource"); }
        if (missing.Count > 0)
        {
            throw StudioException.BadRequest(
                "This is your first studio booking, so the studio needs more about you. Missing: " + string.Join(", ", missing) + ".");
        }

        var dob = d.DateOfBirth!.Value;
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        if (dob >= today || dob.Year < 1900)
        {
            throw StudioException.BadRequest("The date of birth is not valid.");
        }

        var digits = d.MobilePhone!.Count(char.IsDigit);
        if (digits < 7 || digits > 15)
        {
            throw StudioException.BadRequest("The mobile number is not valid.");
        }

        var locations = await GetLocationsAsync(ct).ConfigureAwait(false);
        if (!locations.TryGetValue(d.HomeLocationId!.Value, out var home))
        {
            throw StudioException.BadRequest($"There is no studio location {d.HomeLocationId}.");
        }

        var referralTypes = await GetReferralTypesAsync(ct).ConfigureAwait(false);
        var referral = d.ReferralSource!.Trim();
        if (referralTypes.Count > 0)
        {
            var match = referralTypes.FirstOrDefault(r => string.Equals(r, referral, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw StudioException.BadRequest(
                    $"'{referral}' is not one of the ways the studio records: {string.Join(", ", referralTypes)}.");
            }

            referral = match;
        }

        var (first, last) = NamesOf(shopper, d);
        return new AddClientRequest
        {
            NewId = clientId,
            FirstName = first,
            LastName = last,
            Email = shopper.Email,
            AddressLine1 = d.StreetAddress!.Trim(),
            City = d.City!.Trim(),
            State = d.State!.Trim(),
            PostalCode = d.PostalCode!.Trim(),
            BirthDate = ForMindbody(dob.ToDateTime(TimeOnly.MinValue)),
            MobilePhone = d.MobilePhone!.Trim(),
            ReferredBy = referral,
            HomeLocation = new Location { Id = home.Id },
        };
    }

    private static (string First, string Last) NamesOf(Shopper shopper, NewClientDetails d)
    {
        var local = shopper.Email.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        static string Title(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
        var first = !string.IsNullOrWhiteSpace(d.FirstName) ? d.FirstName!.Trim() : Title(parts.FirstOrDefault() ?? "Shopper");
        var last = !string.IsNullOrWhiteSpace(d.LastName) ? d.LastName!.Trim() : parts.Length > 1 ? Title(parts[1]) : "eShop";
        return (first, last);
    }

    private async Task<List<VisitWithWaitlistInfo>> GetScheduleAsync(string clientId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        return await FetchAllAsync(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, auth) => c.ClientApi.GetClientSchedule(new GetClientScheduleOperationRequest
            {
                RequestClientId = clientId,
                RequestStartDate = QueryDate(from.AddDays(-QueryPaddingDays)),
                RequestEndDate = QueryDate(to.AddDays(QueryPaddingDays + 1)),
                RequestLimit = limit,
                RequestOffset = offset,
                SiteId = site,
                Authorization = auth,
            }, cancellationToken: ct), ct).ConfigureAwait(false);
            return (r.Visits, r.PaginationResponse?.TotalResults);
        }).ConfigureAwait(false);
    }

    private static bool IsLive(VisitWithWaitlistInfo v) =>
        v.AppointmentStatus is null
        || !(string.Equals(v.AppointmentStatus.Value, "Cancelled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v.AppointmentStatus.Value, "LateCancelled", StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------------------------------------
    // 3. Book
    // ---------------------------------------------------------------------------------------------

    public async Task<StudioBookingDto> BookClassAsync(Shopper shopper, int classId, NewClientDetails? details, CancellationToken ct)
    {
        var tz = await GetTimeZoneAsync(ct).ConfigureAwait(false);
        var cls = await LoadClassAsync(classId, tz, ct).ConfigureAwait(false);
        var nowWall = LocalNow(tz);
        EnsureBookable(cls, nowWall, forShopper: true);

        using var guard = await _clientLocks.AcquireAsync(ClientIdOf(shopper), ct).ConfigureAwait(false);
        var clientId = await EnsureClientAsync(shopper, details, ct).ConfigureAwait(false);
        return await PlaceInClassAsync(clientId, cls, tz, ct).ConfigureAwait(false);
    }

    public async Task<StudioBookingDto> AdminBookClassAsync(Shopper shopper, int classId, CancellationToken ct)
    {
        var tz = await GetTimeZoneAsync(ct).ConfigureAwait(false);
        var cls = await LoadClassAsync(classId, tz, ct).ConfigureAwait(false);
        EnsureBookable(cls, LocalNow(tz), forShopper: false);

        using var guard = await _clientLocks.AcquireAsync(ClientIdOf(shopper), ct).ConfigureAwait(false);
        var clientId = ClientIdOf(shopper);
        if (await FindClientAsync(clientId, ct).ConfigureAwait(false) is null)
        {
            throw StudioException.Conflict(
                $"{shopper.Email} is not a studio client yet. The shopper has to make a first booking themselves, giving their details, before they can be booked in.");
        }

        return await PlaceInClassAsync(clientId, cls, tz, ct).ConfigureAwait(false);
    }

    private async Task<Class> LoadClassAsync(int classId, TimeZoneInfo tz, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(LocalNow(tz));
        var found = await FetchClassesAsync(today.AddDays(-QueryPaddingDays), today.AddDays(800), new[] { classId }, ct)
            .ConfigureAwait(false);
        var cls = found.FirstOrDefault(c => c.Id == classId);
        if (cls is null || !cls.StartDateTime.HasValue || !cls.EndDateTime.HasValue)
        {
            throw StudioException.NotFound($"There is no class {classId} in the timetable.");
        }

        return cls;
    }

    private static void EnsureBookable(Class cls, DateTime nowWall, bool forShopper)
    {
        if (cls.IsCanceled == true)
        {
            throw StudioException.Conflict("This class has been cancelled.");
        }

        if (Wall(cls.EndDateTime) <= nowWall || (forShopper && Wall(cls.StartDateTime) <= nowWall))
        {
            throw StudioException.Conflict("This class has already started or finished.");
        }

        if (!forShopper)
        {
            return;
        }

        if (cls.Active == false)
        {
            throw StudioException.Conflict("This class is not open for booking.");
        }

        var scheduleType = cls.ClassDescription?.Program?.ScheduleType?.Value;
        if (scheduleType is not null && !string.Equals(scheduleType, "Class", StringComparison.OrdinalIgnoreCase))
        {
            throw StudioException.Conflict("This is a course, not a single class, and cannot be booked here.");
        }

        if (cls.IsAvailable == false)
        {
            throw StudioException.Conflict("This class is full or not open for booking.");
        }
    }

    private async Task<StudioBookingDto> PlaceInClassAsync(string clientId, Class cls, TimeZoneInfo tz, CancellationToken ct)
    {
        var classId = cls.Id!.Value;
        var day = DateOnly.FromDateTime(Wall(cls.StartDateTime));

        // The app asks again when it gets no answer: a place already held is answered as a success.
        var schedule = await GetScheduleAsync(clientId, day, day, ct).ConfigureAwait(false);
        var held = schedule.FirstOrDefault(v => v.ClassId == classId && v.Id is > 0 && IsLive(v));
        if (held is not null)
        {
            return ClassBooking(cls, held.Id!.Value, tz, created: false);
        }

        long? visitId;
        try
        {
            var response = await _mb.CallAsync((c, site, auth) => c.ClassApi.AddClientToClass(new AddClientToClassOperationRequest
            {
                SiteId = site,
                Authorization = auth,
                Body = new AddClientToClassRequest
                {
                    ClientId = clientId,
                    ClassId = classId,
                    SendEmail = false,
                },
            }, cancellationToken: ct), ct).ConfigureAwait(false);
            visitId = response.Visit?.Id;
        }
        catch (StudioException ex) when (ex.ReplyUnreadable)
        {
            // The booking was most likely made: read it back.
            var after = await GetScheduleAsync(clientId, day, day, ct).ConfigureAwait(false);
            visitId = after.FirstOrDefault(v => v.ClassId == classId && v.Id is > 0 && IsLive(v))?.Id;
            if (visitId is null)
            {
                throw;
            }
        }

        if (visitId is null or <= 0)
        {
            throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not confirm the booking.");
        }

        _memory.Remove(CancelledKey(clientId, "class", visitId.Value));
        return ClassBooking(cls, visitId.Value, tz, created: true);
    }

    private static StudioBookingDto ClassBooking(Class cls, long bookingId, TimeZoneInfo tz, bool created) => new()
    {
        Kind = "class",
        BookingId = bookingId,
        Name = cls.ClassDescription?.Name,
        ClassId = cls.Id,
        StartTime = ToStudioTime(tz, Wall(cls.StartDateTime)),
        EndTime = ToStudioTime(tz, Wall(cls.EndDateTime)),
        LocationId = cls.Location?.Id,
        Trainer = cls.Staff?.DisplayName ?? cls.Staff?.Name,
        Created = created,
    };

    public async Task<StudioBookingDto> BookAppointmentAsync(Shopper shopper, AppointmentBookingBody body, CancellationToken ct)
    {
        if (body.StartTime is null)
        {
            throw StudioException.BadRequest("startTime is required.");
        }

        var tz = await GetTimeZoneAsync(ct).ConfigureAwait(false);
        var sessionType = await GetAppointmentSessionTypeAsync(body.SessionTypeId, ct).ConfigureAwait(false);
        var length = RequireLength(sessionType);

        if (body.StartTime.Value <= _clock.GetUtcNow())
        {
            throw StudioException.Conflict("That time has already passed.");
        }

        // What the studio's clock reads at that moment.
        var wall = DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(body.StartTime.Value, tz).DateTime, DateTimeKind.Unspecified);
        var times = await GetActiveSessionTimesAsync(body.SessionTypeId, ct).ConfigureAwait(false);
        if (!times.Contains(TimeOnly.FromDateTime(wall)) || wall.Second != 0)
        {
            throw StudioException.BadRequest("A session of this type cannot start at that time. See /api/studio/slots for the times that can.");
        }

        var start = ToStudioTime(tz, wall);
        var end = TimeZoneInfo.ConvertTime(start.AddMinutes(length), tz);
        var day = DateOnly.FromDateTime(wall);

        using var guard = await _clientLocks.AcquireAsync(ClientIdOf(shopper), ct).ConfigureAwait(false);
        var clientId = await EnsureClientAsync(shopper, body, ct).ConfigureAwait(false);

        // The app asks again when it gets no answer: an appointment already held is answered as a success.
        var schedule = await GetScheduleAsync(clientId, day, day, ct).ConfigureAwait(false);
        var held = schedule.FirstOrDefault(v => v.AppointmentId is > 0 && IsLive(v)
            && v.VisitType == body.SessionTypeId && v.StartDateTime.HasValue && Wall(v.StartDateTime) == wall);
        if (held is not null)
        {
            return new StudioBookingDto
            {
                Kind = "appointment",
                BookingId = held.AppointmentId!.Value,
                Name = sessionType.Name,
                SessionTypeId = body.SessionTypeId,
                StartTime = start,
                EndTime = end,
                LocationId = held.LocationId,
                Created = false,
            };
        }

        var candidates = await FindFreeTrainersAsync(body, wall, length, day, ct).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            throw StudioException.Conflict(body.StaffId is null
                ? "No trainer is free at that time."
                : "That trainer is not free at that time.");
        }

        StudioException? first = null;
        foreach (var (staffId, locationId, trainerName) in candidates)
        {
            try
            {
                long? id;
                try
                {
                    var response = await _mb.CallAsync((c, site, auth) => c.AppointmentApi.AddAppointment(new AddAppointmentOperationRequest
                    {
                        SiteId = site,
                        Authorization = auth,
                        Body = new AddAppointmentRequest
                        {
                            ClientId = clientId,
                            LocationId = locationId,
                            SessionTypeId = body.SessionTypeId,
                            StaffId = staffId,
                            StartDateTime = ForMindbody(wall),
                            StaffRequested = body.StaffId.HasValue,
                            SendEmail = false,
                        },
                    }, cancellationToken: ct), ct).ConfigureAwait(false);
                    id = response.Appointment?.Id;
                }
                catch (StudioException ex) when (ex.ReplyUnreadable)
                {
                    // The appointment was most likely made: read it back.
                    var after = await GetScheduleAsync(clientId, day, day, ct).ConfigureAwait(false);
                    id = after.FirstOrDefault(v => v.AppointmentId is > 0 && IsLive(v) && v.VisitType == body.SessionTypeId
                        && v.StartDateTime.HasValue && Wall(v.StartDateTime) == wall)?.AppointmentId;
                    if (id is null)
                    {
                        throw;
                    }
                }

                if (id is null or <= 0)
                {
                    throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not confirm the booking.");
                }

                _memory.Remove(CancelledKey(clientId, "appointment", id.Value));
                return new StudioBookingDto
                {
                    Kind = "appointment",
                    BookingId = id.Value,
                    Name = sessionType.Name,
                    SessionTypeId = body.SessionTypeId,
                    StartTime = start,
                    EndTime = end,
                    LocationId = locationId,
                    Trainer = trainerName,
                };
            }
            catch (StudioException ex) when (ex.StatusCode is 422 or 409)
            {
                // A problem with the client (for instance already booked at that time) is the same for every trainer.
                if (ex.MindbodyCode == "ClientIsAlreadyBooked"
                    || ex.Message.StartsWith("The client", StringComparison.OrdinalIgnoreCase))
                {
                    throw StudioException.Conflict(ex.Message);
                }

                // That trainer was taken in the meantime, or refused: try the next one.
                first ??= ex;
            }
        }

        throw StudioException.Conflict("The session could not be booked: " + first!.Message);
    }

    private async Task<List<(long StaffId, int LocationId, string? Name)>> FindFreeTrainersAsync(
        AppointmentBookingBody body, DateTime wall, int length, DateOnly day, CancellationToken ct)
    {
        var all = await FetchAllAsync(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, auth) => c.AppointmentApi.GetBookableItems(new GetBookableItemsOperationRequest
            {
                RequestSessionTypeIds = new[] { body.SessionTypeId },
                RequestStartDate = QueryDate(day.AddDays(-QueryPaddingDays)),
                RequestEndDate = QueryDate(day.AddDays(QueryPaddingDays + 1)),
                RequestStaffIds = body.StaffId.HasValue ? new[] { body.StaffId.Value } : null,
                RequestLocationIds = body.SessionLocationId.HasValue ? new[] { body.SessionLocationId.Value } : null,
                RequestLimit = limit,
                RequestOffset = offset,
                SiteId = site,
                Authorization = auth,
            }, cancellationToken: ct), ct).ConfigureAwait(false);
            return (r.Availabilities, r.PaginationResponse?.TotalResults);
        }).ConfigureAwait(false);

        var finish = wall.AddMinutes(length);
        return all
            .Where(a => a.Staff?.Id is not null && a.Location?.Id is not null
                && a.StartDateTime.HasValue && a.EndDateTime.HasValue
                && Wall(a.StartDateTime) <= wall && finish <= Wall(a.EndDateTime))
            // Prefer the location the shopper named, then the trainer who has been free longest.
            .OrderByDescending(a => body.HomeLocationId.HasValue && a.Location!.Id == body.HomeLocationId)
            .ThenBy(a => Wall(a.StartDateTime))
            .Select(a => (a.Staff!.Id!.Value, a.Location!.Id!.Value, a.Staff.DisplayName ?? a.Staff.Name))
            .Distinct()
            .ToList();
    }

    // ---------------------------------------------------------------------------------------------
    // 4. Cancel
    // ---------------------------------------------------------------------------------------------

    private static string CancelledKey(string clientId, string kind, long bookingId) => $"studio-cancelled:{clientId}:{kind}:{bookingId}";

    public async Task<StudioCancellationDto> CancelClassBookingAsync(Shopper shopper, long bookingId, CancellationToken ct)
    {
        return await CancelAsync(shopper, "class", bookingId,
            v => v.Id == bookingId && v.ClassId is > 0,
            async (clientId, visit, token) =>
            {
                try
                {
                    await _mb.CallAsync((c, site, auth) => c.ClassApi.RemoveClientFromClass(new RemoveClientFromClassOperationRequest
                    {
                        SiteId = site,
                        Authorization = auth,
                        Body = new RemoveClientFromClassRequest { ClientId = clientId, ClassId = visit.ClassId!.Value, SendEmail = false },
                    }, cancellationToken: token), token).ConfigureAwait(false);
                }
                catch (StudioException ex) when (ex.ReplyUnreadable
                    || (ex.MindbodyCode == "InvalidParameter"
                        && ex.Message.Contains("No class visit found", StringComparison.OrdinalIgnoreCase)))
                {
                    // Done, or already gone: the goal of the cancellation is met.
                }
            }, ct).ConfigureAwait(false);
    }

    public async Task<StudioCancellationDto> CancelAppointmentBookingAsync(Shopper shopper, long bookingId, CancellationToken ct)
    {
        return await CancelAsync(shopper, "appointment", bookingId,
            v => v.AppointmentId == bookingId && bookingId > 0,
            async (clientId, visit, token) =>
            {
                try
                {
                    await _mb.CallAsync((c, site, auth) => c.AppointmentApi.UpdateAppointment(new UpdateAppointmentOperationRequest
                    {
                        SiteId = site,
                        Authorization = auth,
                        Body = new UpdateAppointmentRequest { AppointmentId = bookingId, Execute = "Cancel", SendEmail = false },
                    }, cancellationToken: token), token).ConfigureAwait(false);
                }
                catch (StudioException ex) when (ex.ReplyUnreadable
                    || (ex.MindbodyCode == "InvalidParameter"
                        && ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)))
                {
                    // Done, or already gone: the goal of the cancellation is met.
                }
            }, ct).ConfigureAwait(false);
    }

    private async Task<StudioCancellationDto> CancelAsync(
        Shopper shopper, string kind, long bookingId, Func<VisitWithWaitlistInfo, bool> isIt,
        Func<string, VisitWithWaitlistInfo, CancellationToken, Task> cancel, CancellationToken ct)
    {
        var tz = await GetTimeZoneAsync(ct).ConfigureAwait(false);
        var clientId = ClientIdOf(shopper);
        var result = new StudioCancellationDto { Kind = kind, BookingId = bookingId };

        using var guard = await _clientLocks.AcquireAsync(clientId, ct).ConfigureAwait(false);

        // Only the shopper's own bookings are ever looked at, so someone else's booking id never matches.
        var today = DateOnly.FromDateTime(LocalNow(tz));
        var schedule = await GetScheduleAsync(clientId, today.AddDays(-1), today.AddDays(800), ct).ConfigureAwait(false);
        var visit = schedule.FirstOrDefault(v => isIt(v) && IsLive(v));
        if (visit is null)
        {
            // The app asks again when it gets no answer: a booking this shopper cancelled a moment ago is a success.
            if (_memory.TryGetValue(CancelledKey(clientId, kind, bookingId), out _))
            {
                return result;
            }

            throw StudioException.NotFound($"You have no {kind} booking {bookingId}.");
        }

        if (!visit.StartDateTime.HasValue || Wall(visit.StartDateTime) <= LocalNow(tz))
        {
            throw StudioException.Conflict("This booking has already started or finished and can no longer be cancelled.");
        }

        await cancel(clientId, visit, ct).ConfigureAwait(false);
        _memory.Set(CancelledKey(clientId, kind, bookingId), true, CancelledMemory);
        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // 5. Trainers
    // ---------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<StudioTrainerDto>> ListTrainersAsync(CancellationToken ct)
    {
        var staff = await FetchAllAsync(async (offset, limit) =>
        {
            var r = await _mb.CallAsync((c, site, auth) => c.StaffApi.GetStaff(new GetStaffOperationRequest
            {
                RequestLimit = limit,
                RequestOffset = offset,
                SiteId = site,
                Authorization = auth,
            }, cancellationToken: ct), ct).ConfigureAwait(false);
            return (r.StaffMembers, r.PaginationResponse?.TotalResults);
        }).ConfigureAwait(false);

        return staff
            // Ids below zero are the studio system's own accounts, not people.
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
                TeachesClasses = s.ClassTeacher ?? false,
                TeachesAppointments = s.AppointmentInstructor ?? false,
                Email = s.Email,
                MobilePhone = s.MobilePhone,
                HomePhone = s.HomePhone,
                WorkPhone = s.WorkPhone,
                Address = s.Address,
                City = s.City,
                State = s.State,
                PostalCode = s.PostalCode,
                Country = s.Country,
            })
            .ToList();
    }
}
