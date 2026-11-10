using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// The studio features, carried out against Mindbody through the SDK.
///
/// Times: Mindbody reports every date-time as the studio's local wall-clock time with no offset (the SDK
/// parses them as if they were UTC). They are converted here, using the studio's time zone as reported by
/// Mindbody, to date-times with the correct UTC offset (including daylight saving changes).
/// </summary>
public class StudioService
{
    private const int PageSize = 200;
    private const int MaxPages = 200;
    private const int MaxRangeDays = 366;

    private static readonly TimeSpan SiteCacheTime = TimeSpan.FromHours(1);
    private static readonly TimeSpan LookupCacheTime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CancelledMemory = TimeSpan.FromDays(14);

    private readonly MindbodyConnection _mb;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IMemoryCache _cache;
    private readonly KeyedLock _locks;

    public StudioService(MindbodyConnection mb, UserManager<ApplicationUser> users, IMemoryCache cache, KeyedLock locks)
    {
        _mb = mb;
        _users = users;
        _cache = cache;
        _locks = locks;
    }

    // ---------------------------------------------------------------- time handling

    private async Task<TimeZoneInfo> GetTimeZoneAsync()
    {
        var cached = await _cache.GetOrCreateAsync("studio:timezone", async entry =>
        {
            var siteId = _mb.NumericSiteId;
            var response = await _mb.CallAsync((c, site, token) => c.SiteApi.GetSites(
                new GetSitesOperationRequest { RequestSiteIds = new[] { siteId }, Authorization = token },
                // The SDK's GetSites operation has no site id of its own, but Mindbody needs one with the token.
                new MindbodyPublicApi.Core.RequestOptions
                {
                    Headers = new[] { new MindbodyPublicApi.Core.Models.HeaderParam("siteId", site) }
                })).ConfigureAwait(false);
            var id = response.Sites?.FirstOrDefault(s => s.Id == siteId)?.TimeZone ?? response.Sites?.FirstOrDefault()?.TimeZone;
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not report its time zone.");
            }
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(id);
                entry.AbsoluteExpirationRelativeToNow = SiteCacheTime;
                return tz;
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw new StudioException(HttpStatusCode.BadGateway, $"The studio time zone '{id}' is not known to this server.", ex);
            }
        }).ConfigureAwait(false);
        return cached!;
    }

    /// <summary>Mindbody wall-clock time (parsed by the SDK as UTC) to a date-time with the studio's offset.</summary>
    private static DateTimeOffset ToStudioTime(DateTimeOffset mindbody, TimeZoneInfo tz) =>
        FromWallClock(mindbody.DateTime, tz);

    private static DateTimeOffset FromWallClock(DateTime wall, TimeZoneInfo tz)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(wall)) wall = wall.AddHours(1); // falls in a spring-forward gap
        var offset = tz.IsAmbiguousTime(wall) ? tz.GetAmbiguousTimeOffsets(wall).Max() : tz.GetUtcOffset(wall);
        return new DateTimeOffset(wall, offset);
    }

    private static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo tz) =>
        FromWallClock(day.ToDateTime(TimeOnly.MinValue), tz);

    private static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo tz) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, tz).DateTime);

    private static DateOnly Today(TimeZoneInfo tz) => LocalDate(DateTimeOffset.UtcNow, tz);

    public static DateOnly ParseDate(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text)) throw StudioException.BadRequest($"'{name}' is required.");
        text = text.Trim();
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)) return DateOnly.FromDateTime(dto.DateTime);
        throw StudioException.BadRequest($"'{name}' must be a date such as 2026-11-12.");
    }

    // ---------------------------------------------------------------- 1. classes

    public async Task<IReadOnlyList<StudioClassDto>> ListClassesAsync(DateOnly? from, DateOnly? to)
    {
        var tz = await GetTimeZoneAsync().ConfigureAwait(false);
        var first = from ?? Today(tz);
        var last = to ?? first.AddDays(7);
        if (last < first) throw StudioException.BadRequest("'to' must not be before 'from'.");
        if (last.DayNumber - first.DayNumber >= MaxRangeDays)
        {
            throw StudioException.BadRequest($"The range can cover at most {MaxRangeDays} days.");
        }

        var windowStart = StartOfDay(first, tz);
        var windowEnd = StartOfDay(last.AddDays(1), tz);
        var now = DateTimeOffset.UtcNow;

        var classes = await FetchClassesAsync(windowStart, windowEnd, null, hideCancelled: true).ConfigureAwait(false);
        return classes
            .Where(c => c.StartDateTime.HasValue && c.EndDateTime.HasValue)
            .Select(c => (Class: c, Start: ToStudioTime(c.StartDateTime!.Value, tz), End: ToStudioTime(c.EndDateTime!.Value, tz)))
            .Where(x => x.Start >= windowStart && x.Start < windowEnd)
            .Where(x => x.Class.IsCanceled != true && x.Class.Active != false && x.Class.IsAvailable != false)
            .Where(x => x.End > now)
            .OrderBy(x => x.Start).ThenBy(x => x.Class.Id)
            .Select(x => ToClassDto(x.Class, x.Start, x.End))
            .ToList();
    }

    private static StudioClassDto ToClassDto(Class c, DateTimeOffset start, DateTimeOffset end) => new()
    {
        Id = c.Id ?? 0,
        Name = c.ClassDescription?.Name,
        Description = c.ClassDescription?.Description,
        StartTime = start,
        EndTime = end,
        LocationId = c.Location?.Id,
        LocationName = c.Location?.Name,
        TrainerName = c.Staff?.DisplayName ?? c.Staff?.Name
    };

    private async Task<List<Class>> FetchClassesAsync(DateTimeOffset start, DateTimeOffset end, int? classId, bool hideCancelled)
    {
        var result = new List<Class>();
        var offset = 0;
        for (var page = 0; page < MaxPages; page++)
        {
            var o = offset;
            var response = await _mb.CallAsync((c, site, token) => c.ClassApi.GetClasses(new GetClassesOperationRequest
            {
                RequestStartDateTime = start.AddSeconds(0),
                RequestEndDateTime = end.AddSeconds(-1),
                RequestClassIds = classId.HasValue ? new[] { classId.Value } : null,
                RequestHideCanceledClasses = hideCancelled ? true : null,
                RequestLimit = PageSize,
                RequestOffset = o,
                SiteId = site,
                Authorization = token
            })).ConfigureAwait(false);
            var items = response.Classes ?? Array.Empty<Class>();
            result.AddRange(items);
            offset += items.Count;
            var total = response.PaginationResponse?.TotalResults ?? 0;
            if (items.Count == 0 || offset >= total) break;
        }
        return result;
    }

    // ---------------------------------------------------------------- 2. session times

    public async Task<IReadOnlyList<StudioSlotDto>> ListSlotsAsync(int sessionTypeId, DateOnly from, DateOnly to)
    {
        if (to < from) throw StudioException.BadRequest("'to' must not be before 'from'.");
        if (to.DayNumber - from.DayNumber >= MaxRangeDays)
        {
            throw StudioException.BadRequest($"The range can cover at most {MaxRangeDays} days.");
        }

        var tz = await GetTimeZoneAsync().ConfigureAwait(false);
        var sessionType = await GetSessionTypeAsync(sessionTypeId).ConfigureAwait(false);
        var length = TimeSpan.FromMinutes(sessionType.DefaultTimeLength is > 0 ? sessionType.DefaultTimeLength.Value : 60);

        var response = await _mb.CallAsync((c, site, token) => c.AppointmentApi.GetActiveSessionTimes(
            new GetActiveSessionTimesOperationRequest
            {
                RequestSessionTypeIds = new[] { sessionTypeId },
                RequestLimit = PageSize,
                SiteId = site,
                Authorization = token
            })).ConfigureAwait(false);

        var times = response.SessionTypeActiveSessionTimes?.FirstOrDefault(s => s.SessionTypeId == sessionTypeId)?.ActiveSessionTimes
                    ?? response.ActiveSessionTimes ?? Array.Empty<string>();
        var grid = times
            .Select(t => TimeOnly.TryParse(t, CultureInfo.InvariantCulture, out var parsed) ? (TimeOnly?)parsed : null)
            .Where(t => t.HasValue).Select(t => t!.Value).Distinct().OrderBy(t => t).ToList();

        var slots = new List<StudioSlotDto>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            foreach (var time in grid)
            {
                var wall = day.ToDateTime(time);
                if (tz.IsInvalidTime(wall)) continue; // that wall-clock time does not exist on a daylight-saving change
                var start = FromWallClock(wall, tz);
                var end = TimeZoneInfo.ConvertTime(start + length, tz);
                slots.Add(new StudioSlotDto { StartTime = start, EndTime = end });
            }
        }
        return slots;
    }

    private async Task<SessionType> GetSessionTypeAsync(int sessionTypeId)
    {
        var types = await _cache.GetOrCreateAsync("studio:sessiontypes", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = LookupCacheTime;
            var all = new List<SessionType>();
            var offset = 0;
            for (var page = 0; page < MaxPages; page++)
            {
                var o = offset;
                var response = await _mb.CallAsync((c, site, token) => c.SiteApi.GetSessionTypes(new GetSessionTypesOperationRequest
                {
                    RequestOnlineOnly = false,
                    RequestLimit = PageSize,
                    RequestOffset = o,
                    SiteId = site,
                    Authorization = token
                })).ConfigureAwait(false);
                var items = response.SessionTypes ?? Array.Empty<SessionType>();
                all.AddRange(items);
                offset += items.Count;
                if (items.Count == 0 || offset >= (response.PaginationResponse?.TotalResults ?? 0)) break;
            }
            return all;
        }).ConfigureAwait(false);

        var found = types!.FirstOrDefault(t => t.Id == sessionTypeId)
                    ?? throw StudioException.NotFound($"There is no session type {sessionTypeId}.");
        var kind = found.Type?.Value;
        if (kind is "Class" or "Enrollment")
        {
            throw StudioException.BadRequest($"Session type {sessionTypeId} is a class, not a one-to-one session.");
        }
        return found;
    }

    // ---------------------------------------------------------------- 5. trainers

    public async Task<IReadOnlyList<StudioTrainerDto>> ListTrainersAsync()
    {
        var all = new List<Staff>();
        var offset = 0;
        for (var page = 0; page < MaxPages; page++)
        {
            var o = offset;
            var response = await _mb.CallAsync((c, site, token) => c.StaffApi.GetStaff(new GetStaffOperationRequest
            {
                RequestLimit = PageSize,
                RequestOffset = o,
                SiteId = site,
                Authorization = token
            })).ConfigureAwait(false);
            var items = response.StaffMembers ?? Array.Empty<Staff>();
            all.AddRange(items);
            offset += items.Count;
            if (items.Count == 0 || offset >= (response.PaginationResponse?.TotalResults ?? 0)) break;
        }

        return all
            .Where(s => s.Id > 0 && s.Active != false && (s.AppointmentInstructor == true || s.ClassTeacher == true))
            .GroupBy(s => s.Id).Select(g => g.First())
            .OrderBy(s => s.LastName, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.FirstName, StringComparer.OrdinalIgnoreCase)
            .Select(s => new StudioTrainerDto
            {
                Id = s.Id ?? 0,
                FirstName = s.FirstName,
                LastName = s.LastName,
                Name = s.DisplayName ?? s.Name,
                Email = s.Email,
                HomePhone = s.HomePhone,
                MobilePhone = s.MobilePhone,
                WorkPhone = s.WorkPhone,
                Address = s.Address,
                City = s.City,
                State = s.State,
                PostalCode = s.PostalCode,
                Country = s.Country,
                Bio = s.Bio,
                ImageUrl = s.ImageUrl,
                Active = s.Active ?? true,
                ClassTeacher = s.ClassTeacher ?? false,
                AppointmentInstructor = s.AppointmentInstructor ?? false
            }).ToList();
    }

    // ---------------------------------------------------------------- clients

    private record Shopper(string UserName, string Email, string FirstName, string LastName);

    private async Task<Shopper> ResolveShopperAsync(string? userNameOrEmail, bool callerIdentity)
    {
        if (string.IsNullOrWhiteSpace(userNameOrEmail))
        {
            throw callerIdentity
                ? new StudioException(HttpStatusCode.Unauthorized, "The token does not identify a user.")
                : StudioException.BadRequest("'shopper' is required.");
        }
        var user = await _users.FindByNameAsync(userNameOrEmail).ConfigureAwait(false)
                   ?? await _users.FindByEmailAsync(userNameOrEmail).ConfigureAwait(false);
        if (user == null)
        {
            throw callerIdentity
                ? new StudioException(HttpStatusCode.Unauthorized, "The signed-in user does not exist.")
                : StudioException.NotFound($"There is no shopper '{userNameOrEmail}'.");
        }
        var email = (user.Email ?? user.UserName)?.Trim();
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
        {
            throw StudioException.BadRequest("The shopper has no e-mail address to register with the studio.");
        }

        // The eShop identity has no name; derive one from the e-mail address (the details may override it).
        var local = email.Substring(0, email.IndexOf('@'));
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Capitalise(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? Capitalise(string.Join(" ", parts.Skip(1))) : "eShop";
        return new Shopper(user.UserName ?? email, email, first, last);
    }

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    private async Task<string?> FindClientIdAsync(string email)
    {
        var response = await _mb.CallAsync((c, site, token) => c.ClientApi.GetClients(new GetClientsOperationRequest
        {
            RequestSearchText = email,
            RequestIncludeInactive = true,
            RequestLimit = PageSize,
            SiteId = site,
            Authorization = token
        })).ConfigureAwait(false);
        return (response.Clients ?? Array.Empty<ClientWithSuspensionInfo>())
            .Where(c => !string.IsNullOrEmpty(c.Id) && string.Equals(c.Email?.Trim(), email, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Active != false)
            .Select(c => c.Id)
            .FirstOrDefault();
    }

    private async Task<string> EnsureClientIdAsync(Shopper shopper, NewClientDetails? details)
    {
        var existing = await FindClientIdAsync(shopper.Email).ConfigureAwait(false);
        if (existing != null) return existing;

        // A new client: the studio asks for these details once, with the first booking.
        var v = await ValidateNewClientAsync(details).ConfigureAwait(false);
        try
        {
            var response = await _mb.CallAsync((c, site, token) => c.ClientApi.AddClient(new AddClientOperationRequest
            {
                SiteId = site,
                Authorization = token,
                Body = new AddClientRequest
                {
                    FirstName = string.IsNullOrWhiteSpace(details!.FirstName) ? shopper.FirstName : details.FirstName.Trim(),
                    LastName = string.IsNullOrWhiteSpace(details.LastName) ? shopper.LastName : details.LastName.Trim(),
                    Email = shopper.Email,
                    AddressLine1 = v.Street,
                    City = v.City,
                    State = v.State,
                    PostalCode = v.Postal,
                    // Mindbody reads dates as UTC instants; noon keeps the calendar day in any studio time zone.
                    BirthDate = new DateTimeOffset(v.BirthDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
                    MobilePhone = v.Phone,
                    ReferredBy = v.Referral,
                    HomeLocation = new Location { Id = v.HomeLocationId }
                }
            })).ConfigureAwait(false);

            if (response.Client?.Id == null)
            {
                var reason = response.Errors?.FirstOrDefault()?.Message;
                throw StudioException.BadRequest(reason ?? "The studio could not register the shopper as a client.");
            }
            return response.Client.Id;
        }
        catch (StudioException ex) when (ex.Code == "InvalidClientCreation")
        {
            // Registered meanwhile (e.g. by a retried request): use that client.
            return await FindClientIdAsync(shopper.Email).ConfigureAwait(false) ?? throw ex;
        }
    }

    private record ValidDetails(int HomeLocationId, string Street, string City, string State, string Postal,
        DateOnly BirthDate, string Phone, string Referral);

    private async Task<ValidDetails> ValidateNewClientAsync(NewClientDetails? d)
    {
        var missing = new List<string>();
        void Need(bool ok, string name) { if (!ok) missing.Add(name); }
        Need(d?.HomeLocationId != null, "homeLocationId");
        Need(!string.IsNullOrWhiteSpace(d?.StreetAddress), "streetAddress");
        Need(!string.IsNullOrWhiteSpace(d?.City), "city");
        Need(!string.IsNullOrWhiteSpace(d?.State), "state");
        Need(!string.IsNullOrWhiteSpace(d?.PostalCode), "postalCode");
        Need(!string.IsNullOrWhiteSpace(d?.DateOfBirth), "dateOfBirth");
        Need(!string.IsNullOrWhiteSpace(d?.MobilePhone), "mobilePhone");
        Need(!string.IsNullOrWhiteSpace(d?.ReferralSource), "referralSource");
        if (missing.Count > 0)
        {
            throw StudioException.BadRequest(
                "This shopper is not yet a client of the studio; the first booking must also give: " + string.Join(", ", missing) + ".");
        }

        var dob = ParseDate(d!.DateOfBirth, "dateOfBirth");
        if (dob.Year < 1900 || dob >= DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)))
        {
            throw StudioException.BadRequest("'dateOfBirth' is not a valid date of birth.");
        }
        var phone = d.MobilePhone!.Trim();
        if (phone.Count(char.IsDigit) < 7)
        {
            throw StudioException.BadRequest("'mobilePhone' is not a valid phone number.");
        }

        var locations = await GetLocationIdsAsync().ConfigureAwait(false);
        if (!locations.Contains(d.HomeLocationId!.Value))
        {
            throw StudioException.BadRequest($"'homeLocationId' {d.HomeLocationId} is not one of the studio's locations.");
        }

        var referrals = await GetReferralTypesAsync().ConfigureAwait(false);
        var referral = referrals.FirstOrDefault(r => string.Equals(r, d.ReferralSource!.Trim(), StringComparison.OrdinalIgnoreCase));
        if (referral == null)
        {
            throw StudioException.BadRequest(
                $"'referralSource' must be one of: {string.Join(", ", referrals)}.");
        }

        return new ValidDetails(d.HomeLocationId.Value, d.StreetAddress!.Trim(), d.City!.Trim(), d.State!.Trim(),
            d.PostalCode!.Trim(), dob, phone, referral);
    }

    private async Task<HashSet<int>> GetLocationIdsAsync()
    {
        var ids = await _cache.GetOrCreateAsync("studio:locations", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = LookupCacheTime;
            var set = new HashSet<int>();
            var offset = 0;
            for (var page = 0; page < MaxPages; page++)
            {
                var o = offset;
                var response = await _mb.CallAsync((c, site, token) => c.SiteApi.GetLocations(new GetLocationsOperationRequest
                {
                    RequestLimit = PageSize,
                    RequestOffset = o,
                    SiteId = site,
                    Authorization = token
                })).ConfigureAwait(false);
                var items = response.Locations ?? Array.Empty<Location>();
                foreach (var l in items) if (l.Id.HasValue) set.Add(l.Id.Value);
                offset += items.Count;
                if (items.Count == 0 || offset >= (response.PaginationResponse?.TotalResults ?? 0)) break;
            }
            return set;
        }).ConfigureAwait(false);
        return ids!;
    }

    private async Task<IReadOnlyList<string>> GetReferralTypesAsync()
    {
        var list = await _cache.GetOrCreateAsync("studio:referrals", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = LookupCacheTime;
            var response = await _mb.CallAsync((c, site, token) => c.ClientApi.GetClientReferralTypes(
                new GetClientReferralTypesOperationRequest { SiteId = site, Authorization = token })).ConfigureAwait(false);
            return (IReadOnlyList<string>)(response.ReferralTypes ?? Array.Empty<string>()).ToList();
        }).ConfigureAwait(false);
        return list!;
    }

    // ---------------------------------------------------------------- 3 + 6. book a class

    public Task<StudioBookingDto> BookClassAsync(string? callerUserName, BookClassRequest request) =>
        BookClassCoreAsync(callerUserName, callerIdentity: true, request.ClassId, request, adminOverride: false);

    public Task<StudioBookingDto> BookClassForShopperAsync(AdminBookClassRequest request) =>
        BookClassCoreAsync(request.Shopper, callerIdentity: false, request.ClassId, request, adminOverride: true);

    private async Task<StudioBookingDto> BookClassCoreAsync(string? userName, bool callerIdentity, int classId,
        NewClientDetails details, bool adminOverride)
    {
        if (classId <= 0) throw StudioException.BadRequest("'classId' is required.");
        var shopper = await ResolveShopperAsync(userName, callerIdentity).ConfigureAwait(false);
        var tz = await GetTimeZoneAsync().ConfigureAwait(false);

        // Everything for one shopper happens one request at a time, so a re-sent request cannot book twice.
        using var shopperLock = await _locks.AcquireAsync(shopper.Email).ConfigureAwait(false);

        var today = Today(tz);
        var windowStart = StartOfDay(today.AddDays(adminOverride ? -1100 : -1), tz);
        var windowEnd = StartOfDay(today.AddDays(800), tz);
        var found = (await FetchClassesAsync(windowStart, windowEnd, classId, hideCancelled: false).ConfigureAwait(false))
            .FirstOrDefault(c => c.Id == classId && c.StartDateTime.HasValue && c.EndDateTime.HasValue)
            ?? throw StudioException.NotFound($"There is no class {classId} in the timetable.");

        var start = ToStudioTime(found.StartDateTime!.Value, tz);
        var end = ToStudioTime(found.EndDateTime!.Value, tz);
        var now = DateTimeOffset.UtcNow;

        if (found.IsCanceled == true) throw StudioException.Conflict("That class has been cancelled.");
        if (!adminOverride)
        {
            if (found.Active == false) throw StudioException.Conflict("That class is not open for booking.");
            if (start <= now) throw StudioException.Conflict("That class has already started.");
        }

        var clientId = await EnsureClientIdAsync(shopper, details).ConfigureAwait(false);

        // A re-sent request must not double-book: if the shopper already holds a place, that is the answer.
        var visits = await FetchVisitsAsync(clientId, start.AddDays(-1), start.AddDays(1)).ConfigureAwait(false);
        var existing = visits.FirstOrDefault(v => v.ClassId == classId && v.Id.HasValue && IsLive(v.AppointmentStatus?.Value));
        long bookingId;
        if (existing != null)
        {
            bookingId = existing.Id!.Value;
        }
        else
        {
            if (!adminOverride && found.IsAvailable == false)
            {
                throw StudioException.Conflict("That class cannot be booked (it is full or outside its booking window).");
            }
            try
            {
                var added = await _mb.CallAsync((c, site, token) => c.ClassApi.AddClientToClass(new AddClientToClassOperationRequest
                {
                    SiteId = site,
                    Authorization = token,
                    Body = new AddClientToClassRequest { ClientId = clientId, ClassId = classId, SendEmail = false }
                })).ConfigureAwait(false);
                bookingId = added.Visit?.Id
                            ?? throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not confirm the booking.");
            }
            catch (StudioException ex) when (ex.InnerException is MindbodyPublicApi.Core.Exceptions.ResponseDeserializationException { StatusCode: >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices })
            {
                // Booked, but the confirmation could not be read: find the booking instead.
                var after = await FetchVisitsAsync(clientId, start.AddDays(-1), start.AddDays(1)).ConfigureAwait(false);
                bookingId = after.FirstOrDefault(v => v.ClassId == classId && v.Id.HasValue && IsLive(v.AppointmentStatus?.Value))?.Id
                            ?? throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not confirm the booking.", ex);
            }
        }

        return new StudioBookingDto
        {
            BookingId = bookingId,
            Kind = "class",
            ClassId = classId,
            Name = found.ClassDescription?.Name,
            StartTime = start,
            EndTime = end,
            LocationId = found.Location?.Id,
            StaffId = found.Staff?.Id,
            ClientId = clientId
        };
    }

    private static bool IsLive(string? status) =>
        status is not ("Cancelled" or "LateCancelled" or "NoShow");

    private async Task<List<Visit>> FetchVisitsAsync(string clientId, DateTimeOffset start, DateTimeOffset end)
    {
        var result = new List<Visit>();
        var offset = 0;
        for (var page = 0; page < MaxPages; page++)
        {
            var o = offset;
            var response = await _mb.CallAsync((c, site, token) => c.ClientApi.GetClientVisits(new GetClientVisitsOperationRequest
            {
                RequestClientId = clientId,
                RequestStartDate = start,
                RequestEndDate = end,
                RequestLimit = PageSize,
                RequestOffset = o,
                SiteId = site,
                Authorization = token
            })).ConfigureAwait(false);
            var items = response.Visits ?? Array.Empty<Visit>();
            result.AddRange(items);
            offset += items.Count;
            if (items.Count == 0 || offset >= (response.PaginationResponse?.TotalResults ?? 0)) break;
        }
        return result;
    }

    // ---------------------------------------------------------------- 3. book a one-to-one session

    public async Task<StudioBookingDto> BookAppointmentAsync(string? callerUserName, BookAppointmentRequest request)
    {
        if (request.SessionTypeId <= 0) throw StudioException.BadRequest("'sessionTypeId' is required.");
        var start = ParseInstant(request.StartTime, "startTime");
        if (start <= DateTimeOffset.UtcNow) throw StudioException.Conflict("That session time has already passed.");

        var shopper = await ResolveShopperAsync(callerUserName, callerIdentity: true).ConfigureAwait(false);
        var tz = await GetTimeZoneAsync().ConfigureAwait(false);
        var sessionType = await GetSessionTypeAsync(request.SessionTypeId).ConfigureAwait(false);
        var localStart = TimeZoneInfo.ConvertTime(start, tz);

        using var shopperLock = await _locks.AcquireAsync(shopper.Email).ConfigureAwait(false);

        var dayStart = StartOfDay(LocalDate(start, tz), tz);
        var dayEnd = StartOfDay(LocalDate(start, tz).AddDays(1), tz);

        // Find who could take the session before registering anyone, so a request that cannot succeed
        // does not leave a new client behind.
        var offers = await FetchAvailabilitiesAsync(request.SessionTypeId, dayStart, dayEnd, request).ConfigureAwait(false);
        var candidates = offers
            .Where(a => a.Staff?.Id != null && a.Location?.Id != null && a.StartDateTime.HasValue)
            .Where(a =>
            {
                var from = ToStudioTime(a.StartDateTime!.Value, tz);
                var lastStart = a.BookableEndDateTime.HasValue
                    ? ToStudioTime(a.BookableEndDateTime.Value, tz)
                    : ToStudioTime(a.EndDateTime ?? a.StartDateTime.Value, tz) - TimeSpan.FromMinutes(sessionType.DefaultTimeLength ?? 0);
                return from <= start && start <= lastStart;
            })
            .Select(a => (Staff: a.Staff!.Id!.Value, Location: a.Location!.Id!.Value))
            .Distinct().ToList();

        var clientId = await EnsureClientIdAsync(shopper, request).ConfigureAwait(false);

        // A re-sent request must not double-book: if the shopper already holds that session, return it.
        var held = await FindAppointmentsAsync(clientId, dayStart, dayEnd).ConfigureAwait(false);
        var same = held.FirstOrDefault(a => a.Id.HasValue && IsLive(a.Status?.Value) && a.SessionTypeId == request.SessionTypeId
            && a.StartDateTime.HasValue && ToStudioTime(a.StartDateTime.Value, tz) == start);
        if (same != null) return ToAppointmentDto(same, clientId, tz);

        if (candidates.Count == 0)
        {
            throw StudioException.Conflict("No trainer can take that session at that time.");
        }

        StudioException? last = null;
        foreach (var (staff, location) in candidates)
        {
            try
            {
                var added = await _mb.CallAsync((c, site, token) => c.AppointmentApi.AddAppointment(new AddAppointmentOperationRequest
                {
                    SiteId = site,
                    Authorization = token,
                    Body = new AddAppointmentRequest
                    {
                        ClientId = clientId,
                        LocationId = location,
                        SessionTypeId = request.SessionTypeId,
                        StaffId = staff,
                        // Mindbody reads body date-times as the studio's wall-clock time and ignores any offset,
                        // so send the studio-local wall-clock time (the SDK writes it as if it were UTC).
                        StartDateTime = new DateTimeOffset(localStart.DateTime, TimeSpan.Zero),
                        SendEmail = false
                    }
                })).ConfigureAwait(false);
                if (added.Appointment?.Id == null)
                {
                    throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not confirm the booking.");
                }
                // The booking response expresses its times differently from every other call, so report the
                // time that was asked for and the session length rather than echoing it.
                var minutes = added.Appointment.Duration is > 0 ? added.Appointment.Duration.Value : sessionType.DefaultTimeLength ?? 60;
                return new StudioBookingDto
                {
                    BookingId = added.Appointment.Id!.Value,
                    Kind = "appointment",
                    SessionTypeId = request.SessionTypeId,
                    StartTime = localStart,
                    EndTime = TimeZoneInfo.ConvertTime(start + TimeSpan.FromMinutes(minutes), tz),
                    LocationId = location,
                    StaffId = staff,
                    ClientId = clientId
                };
            }
            catch (StudioException ex) when (ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest)
            {
                last = ex; // this trainer cannot take it (e.g. already busy): try the next one
            }
        }
        throw StudioException.Conflict(last?.Message ?? "No trainer can take that session at that time.");
    }

    private static DateTimeOffset ParseInstant(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text)) throw StudioException.BadRequest($"'{name}' is required.");
        text = text.Trim();
        var hasOffset = text.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
                        || System.Text.RegularExpressions.Regex.IsMatch(text, @"[+-]\d{2}:?\d{2}$");
        if (!hasOffset || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            throw StudioException.BadRequest($"'{name}' must be an ISO-8601 date-time with its UTC offset, such as 2026-11-12T07:00:00-08:00.");
        }
        return value;
    }

    private async Task<List<Availability>> FetchAvailabilitiesAsync(int sessionTypeId, DateTimeOffset start, DateTimeOffset end,
        BookAppointmentRequest request)
    {
        var result = new List<Availability>();
        var offset = 0;
        for (var page = 0; page < MaxPages; page++)
        {
            var o = offset;
            var response = await _mb.CallAsync((c, site, token) => c.AppointmentApi.GetBookableItems(new GetBookableItemsOperationRequest
            {
                RequestSessionTypeIds = new[] { sessionTypeId },
                RequestStartDate = start,
                RequestEndDate = end.AddSeconds(-1),
                RequestStaffIds = request.StaffId.HasValue ? new[] { request.StaffId.Value } : null,
                RequestLocationIds = request.SessionLocationId.HasValue ? new[] { request.SessionLocationId.Value } : null,
                RequestLimit = PageSize,
                RequestOffset = o,
                SiteId = site,
                Authorization = token
            })).ConfigureAwait(false);
            var items = response.Availabilities ?? Array.Empty<Availability>();
            result.AddRange(items);
            offset += items.Count;
            if (items.Count == 0 || offset >= (response.PaginationResponse?.TotalResults ?? 0)) break;
        }
        return result;
    }

    private async Task<List<Appointment>> FindAppointmentsAsync(string clientId, DateTimeOffset start, DateTimeOffset end)
    {
        var result = new List<Appointment>();
        var offset = 0;
        for (var page = 0; page < MaxPages; page++)
        {
            var o = offset;
            var response = await _mb.CallAsync((c, site, token) => c.AppointmentApi.GetStaffAppointments(new GetStaffAppointmentsOperationRequest
            {
                RequestClientId = clientId,
                RequestStartDate = start,
                RequestEndDate = end.AddSeconds(-1),
                RequestLimit = PageSize,
                RequestOffset = o,
                SiteId = site,
                Authorization = token
            })).ConfigureAwait(false);
            var items = response.Appointments ?? Array.Empty<Appointment>();
            result.AddRange(items);
            offset += items.Count;
            if (items.Count == 0 || offset >= (response.PaginationResponse?.TotalResults ?? 0)) break;
        }
        // The client filter is applied by Mindbody; keep this defensive so one shopper never sees another's.
        return result.Where(a => a.ClientId == null || a.ClientId == clientId).ToList();
    }

    private StudioBookingDto ToAppointmentDto(Appointment a, string clientId, TimeZoneInfo tz) => new()
    {
        BookingId = a.Id!.Value,
        Kind = "appointment",
        SessionTypeId = a.SessionTypeId,
        StartTime = ToStudioTime(a.StartDateTime!.Value, tz),
        EndTime = ToStudioTime(a.EndDateTime ?? a.StartDateTime.Value, tz),
        LocationId = a.LocationId,
        StaffId = a.StaffId,
        ClientId = clientId
    };

    // ---------------------------------------------------------------- 4. cancel

    private static string CancelledKey(string kind, string clientId, long bookingId) => $"studio:cancelled:{kind}:{clientId}:{bookingId}";

    public async Task<CancelResult> CancelClassBookingAsync(string? callerUserName, long bookingId)
    {
        var shopper = await ResolveShopperAsync(callerUserName, callerIdentity: true).ConfigureAwait(false);
        var tz = await GetTimeZoneAsync().ConfigureAwait(false);
        using var shopperLock = await _locks.AcquireAsync(shopper.Email).ConfigureAwait(false);

        var clientId = await FindClientIdAsync(shopper.Email).ConfigureAwait(false)
                       ?? throw StudioException.NotFound($"There is no booking {bookingId}.");

        var today = Today(tz);
        var visits = await FetchVisitsAsync(clientId, StartOfDay(today.AddDays(-1), tz), StartOfDay(today.AddDays(800), tz)).ConfigureAwait(false);
        var visit = visits.FirstOrDefault(v => v.Id == bookingId && v.ClassId > 0);
        if (visit == null)
        {
            // Cancelled bookings disappear from Mindbody; a re-sent cancel of one we just cancelled is a success.
            if (_cache.TryGetValue(CancelledKey("class", clientId, bookingId), out _)) return new CancelResult { BookingId = bookingId };
            throw StudioException.NotFound($"There is no booking {bookingId} for you.");
        }
        if (!IsLive(visit.AppointmentStatus?.Value))
        {
            return new CancelResult { BookingId = bookingId };
        }
        if (visit.StartDateTime.HasValue && ToStudioTime(visit.StartDateTime.Value, tz) <= DateTimeOffset.UtcNow)
        {
            throw StudioException.Conflict("That class has already started and can no longer be cancelled.");
        }

        await _mb.CallWriteAsync((c, site, token) => c.ClassApi.RemoveClientFromClass(new RemoveClientFromClassOperationRequest
        {
            SiteId = site,
            Authorization = token,
            Body = new RemoveClientFromClassRequest { ClientId = clientId, ClassId = visit.ClassId!.Value, VisitId = (int)bookingId, SendEmail = false }
        })).ConfigureAwait(false);

        _cache.Set(CancelledKey("class", clientId, bookingId), true, CancelledMemory);
        return new CancelResult { BookingId = bookingId };
    }

    public async Task<CancelResult> CancelAppointmentBookingAsync(string? callerUserName, long bookingId)
    {
        var shopper = await ResolveShopperAsync(callerUserName, callerIdentity: true).ConfigureAwait(false);
        var tz = await GetTimeZoneAsync().ConfigureAwait(false);
        using var shopperLock = await _locks.AcquireAsync(shopper.Email).ConfigureAwait(false);

        var clientId = await FindClientIdAsync(shopper.Email).ConfigureAwait(false)
                       ?? throw StudioException.NotFound($"There is no booking {bookingId}.");

        var today = Today(tz);
        var held = await FindAppointmentsAsync(clientId, StartOfDay(today.AddDays(-1), tz), StartOfDay(today.AddDays(800), tz)).ConfigureAwait(false);
        var appointment = held.FirstOrDefault(a => a.Id == bookingId);
        if (appointment == null)
        {
            if (_cache.TryGetValue(CancelledKey("appointment", clientId, bookingId), out _)) return new CancelResult { BookingId = bookingId };
            throw StudioException.NotFound($"There is no booking {bookingId} for you.");
        }
        if (!IsLive(appointment.Status?.Value))
        {
            return new CancelResult { BookingId = bookingId };
        }
        if (appointment.StartDateTime.HasValue && ToStudioTime(appointment.StartDateTime.Value, tz) <= DateTimeOffset.UtcNow)
        {
            throw StudioException.Conflict("That session has already started and can no longer be cancelled.");
        }

        await _mb.CallWriteAsync((c, site, token) => c.AppointmentApi.UpdateAppointment(new UpdateAppointmentOperationRequest
        {
            SiteId = site,
            Authorization = token,
            Body = new UpdateAppointmentRequest { AppointmentId = bookingId, Execute = "cancel", SendEmail = false }
        })).ConfigureAwait(false);

        _cache.Set(CancelledKey("appointment", clientId, bookingId), true, CancelledMemory);
        return new CancelResult { BookingId = bookingId };
    }
}
