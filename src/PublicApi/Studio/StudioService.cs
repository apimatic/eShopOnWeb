using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.StudioEndpoints;
using Microsoft.Extensions.Caching.Memory;
using MindbodyPublicApi.Models;
using MindbodyPublicApi.Requests.AppointmentApi;
using MindbodyPublicApi.Requests.ClassApi;
using MindbodyPublicApi.Requests.ClientApi;
using MindbodyPublicApi.Requests.SiteApi;
using MindbodyPublicApi.Requests.StaffApi;
using MindbodyPublicApi.Models.Enums;
using MindbodyPublicApi.Requests.UserToken;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// The studio features, implemented on top of the Mindbody SDK. Registered as a singleton.
/// </summary>
public class StudioService
{
    private const int MaxRangeDays = 366;
    private const int DefaultFollowingDays = 7;

    private readonly MindbodyConnection _mindbody;
    private readonly IMemoryCache _cache;

    // Bookings and client creation of one shopper never run at the same time. A retried request therefore waits for
    // the one it repeats and then finds its result instead of doing the work twice.
    private readonly SemaphoreSlim[] _shopperLocks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public StudioService(MindbodyConnection mindbody, IMemoryCache cache)
    {
        _mindbody = mindbody;
        _cache = cache;
    }

    // ---------------------------------------------------------------- studio facts

    private async Task<StudioClock> GetClockAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue<StudioClock>("studio:clock", out var cached) && cached != null)
        {
            return cached;
        }

        var siteId = _mindbody.SiteId;
        // GetSites has no SiteId parameter in the SDK, but the staff token only works for the site it was issued for.
        var sites = await _mindbody.CallAsync((api, site, token) => api.SiteApi.GetSites(new GetSitesOperationRequest
        {
            Authorization = token,
            RequestSiteIds = int.TryParse(site, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? new[] { id } : null,
        }, new MindbodyPublicApi.Core.RequestOptions
        {
            Headers = new[] { new MindbodyPublicApi.Core.Models.HeaderParam("siteId", site) }
        }), ct);

        var match = sites.Sites?.FirstOrDefault(s => s.Id?.ToString(CultureInfo.InvariantCulture) == siteId) ?? sites.Sites?.FirstOrDefault();
        var clock = StudioClock.FromId(match?.TimeZone);
        _cache.Set("studio:clock", clock, TimeSpan.FromHours(6));
        return clock;
    }

    private async Task<List<MindbodyPublicApi.Models.Location>> GetLocationsAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue<List<MindbodyPublicApi.Models.Location>>("studio:locations", out var cached) && cached != null)
        {
            return cached;
        }

        var locations = await MindbodyConnection.PageAllAsync<MindbodyPublicApi.Models.Location>(async (limit, offset) =>
        {
            var r = await _mindbody.CallAsync((api, site, token) => api.SiteApi.GetLocations(new GetLocationsOperationRequest
            {
                Authorization = token,
                SiteId = site,
                RequestLimit = limit,
                RequestOffset = offset,
            }), ct);
            return (r.Locations, r.PaginationResponse?.TotalResults);
        });
        _cache.Set("studio:locations", locations, TimeSpan.FromMinutes(10));
        return locations;
    }

    private async Task<List<string>> GetReferralTypesAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue<List<string>>("studio:referrals", out var cached) && cached != null)
        {
            return cached;
        }

        var r = await _mindbody.CallAsync((api, site, token) => api.ClientApi.GetClientReferralTypes(
            new GetClientReferralTypesOperationRequest { Authorization = token, SiteId = site }), ct);
        var list = r.ReferralTypes?.ToList() ?? new List<string>();
        _cache.Set("studio:referrals", list, TimeSpan.FromMinutes(10));
        return list;
    }

    private async Task<SessionType> GetAppointmentSessionTypeAsync(int sessionTypeId, CancellationToken ct)
    {
        var all = await MindbodyConnection.PageAllAsync<SessionType>(async (limit, offset) =>
        {
            var r = await _mindbody.CallAsync((api, site, token) => api.SiteApi.GetSessionTypes(new GetSessionTypesOperationRequest
            {
                Authorization = token,
                SiteId = site,
                RequestLimit = limit,
                RequestOffset = offset,
            }), ct);
            return (r.SessionTypes, r.PaginationResponse?.TotalResults);
        });

        var type = all.FirstOrDefault(t => t.Id == sessionTypeId);
        if (type == null)
        {
            throw StudioException.NotFound($"There is no session type {sessionTypeId}.");
        }
        if (type.Type?.Value != "Appointment")
        {
            throw StudioException.Unprocessable($"Session type {sessionTypeId} ({type.Name}) is not a one-to-one session.");
        }
        return type;
    }

    // ---------------------------------------------------------------- 1. classes

    public async Task<IReadOnlyList<StudioClassDto>> GetClassesAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var clock = await GetClockAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var (first, last) = ResolveRange(clock, now, from, to);

        var start = clock.StartOfDay(first);
        var end = clock.StartOfDay(last.AddDays(1)).AddSeconds(-1);

        var classes = await MindbodyConnection.PageAllAsync<Class>(async (limit, offset) =>
        {
            var r = await _mindbody.CallAsync((api, site, token) => api.ClassApi.GetClasses(new GetClassesOperationRequest
            {
                Authorization = token,
                SiteId = site,
                RequestStartDateTime = start,
                RequestEndDateTime = end,
                RequestHideCanceledClasses = true,
                RequestLimit = limit,
                RequestOffset = offset,
            }), ct);
            return (r.Classes, r.PaginationResponse?.TotalResults);
        });

        return classes
            .Where(c => IsAttendable(c, clock, now))
            .Select(c => ToClassDto(c, clock))
            .Where(c => DateOnly.FromDateTime(c.StartTime.DateTime) >= first && DateOnly.FromDateTime(c.StartTime.DateTime) <= last)
            .GroupBy(c => c.Id).Select(g => g.First())
            .OrderBy(c => c.StartTime)
            .ThenBy(c => c.Id)
            .ToList();
    }

    private static bool IsAttendable(Class c, StudioClock clock, DateTimeOffset now) =>
        c.Id.HasValue && c.StartDateTime.HasValue && c.EndDateTime.HasValue &&
        c.IsCanceled != true &&
        c.Active != false &&
        c.IsAvailable != false &&
        IsPlainClass(c) &&
        clock.FromWall(c.EndDateTime.Value) > now;

    // Enrolments (courses) are booked differently from drop-in classes.
    private static bool IsPlainClass(Class c)
    {
        var type = c.ClassDescription?.Program?.ScheduleType?.Value;
        return type == null || type == "Class";
    }

    private static StudioClassDto ToClassDto(Class c, StudioClock clock) => new()
    {
        Id = c.Id!.Value,
        Name = c.ClassDescription?.Name,
        Description = c.ClassDescription?.Description,
        StartTime = clock.FromWall(c.StartDateTime!.Value),
        EndTime = clock.FromWall(c.EndDateTime!.Value),
        LocationId = c.Location?.Id,
        LocationName = c.Location?.Name,
        TrainerName = c.Staff?.DisplayName ?? c.Staff?.Name,
        Capacity = c.MaxCapacity,
        SpotsLeft = c.MaxCapacity.HasValue ? Math.Max(0, c.MaxCapacity.Value - (c.TotalBooked ?? 0)) : null,
    };

    private static (DateOnly First, DateOnly Last) ResolveRange(StudioClock clock, DateTimeOffset now, DateOnly? from, DateOnly? to)
    {
        var today = clock.Today(now);
        var first = from ?? today;
        var last = to ?? (from.HasValue ? first.AddDays(DefaultFollowingDays) : today.AddDays(DefaultFollowingDays));
        ValidateRange(first, last);
        return (first, last);
    }

    private static void ValidateRange(DateOnly first, DateOnly last)
    {
        if (last < first)
        {
            throw StudioException.BadRequest("'from' must not be after 'to'.");
        }
        if (last.DayNumber - first.DayNumber + 1 > MaxRangeDays)
        {
            throw StudioException.BadRequest($"The range may cover at most {MaxRangeDays} days.");
        }
    }

    // ---------------------------------------------------------------- 2. session times

    public async Task<IReadOnlyList<StudioSlotDto>> GetSlotsAsync(int sessionTypeId, DateOnly first, DateOnly last, CancellationToken ct)
    {
        ValidateRange(first, last);
        var clock = await GetClockAsync(ct);
        var type = await GetAppointmentSessionTypeAsync(sessionTypeId, ct);
        var minutes = type.DefaultTimeLength ?? type.StaffTimeLength;
        if (minutes is null or <= 0)
        {
            throw StudioException.Unprocessable($"Session type {sessionTypeId} ({type.Name}) has no length set in the studio.");
        }

        var times = await GetStartTimesOfDayAsync(sessionTypeId, ct);
        var slots = new List<StudioSlotDto>();
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            foreach (var time in times)
            {
                var start = clock.TryFromWall(day.ToDateTime(time));
                if (start == null)
                {
                    continue;
                }
                slots.Add(new StudioSlotDto
                {
                    SessionTypeId = sessionTypeId,
                    StartTime = start.Value,
                    EndTime = clock.AddMinutes(start.Value, minutes.Value),
                });
            }
        }
        return slots;
    }

    private async Task<List<TimeOnly>> GetStartTimesOfDayAsync(int sessionTypeId, CancellationToken ct)
    {
        var raw = await MindbodyConnection.PageAllAsync<string>(async (limit, offset) =>
        {
            var r = await _mindbody.CallAsync((api, site, token) => api.AppointmentApi.GetActiveSessionTimes(
                new GetActiveSessionTimesOperationRequest
                {
                    Authorization = token,
                    SiteId = site,
                    RequestScheduleType = RequestScheduleType.Appointment,
                    RequestSessionTypeIds = new[] { sessionTypeId },
                    RequestLimit = limit,
                    RequestOffset = offset,
                }), ct);
            return (r.ActiveSessionTimes, r.PaginationResponse?.TotalResults);
        });

        return raw
            .Select(t => TimeOnly.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? (TimeOnly?)parsed : null)
            .Where(t => t.HasValue).Select(t => t!.Value)
            .Distinct().OrderBy(t => t)
            .ToList();
    }

    // ---------------------------------------------------------------- studio clients

    private static string EmailOf(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName) || !userName.Contains('@'))
        {
            throw StudioException.Unprocessable("Your account has no e-mail address, which the studio needs to know you.");
        }
        return userName.Trim();
    }

    private async Task<string?> FindClientIdAsync(string email, CancellationToken ct)
    {
        var key = "studio:client:" + email.ToLowerInvariant();
        if (_cache.TryGetValue<string>(key, out var cached) && cached != null)
        {
            return cached;
        }

        var matches = await MindbodyConnection.PageAllAsync<ClientWithSuspensionInfo>(async (limit, offset) =>
        {
            var r = await _mindbody.CallAsync((api, site, token) => api.ClientApi.GetClients(new GetClientsOperationRequest
            {
                Authorization = token,
                SiteId = site,
                RequestSearchText = email,
                RequestLimit = limit,
                RequestOffset = offset,
            }), ct);
            return (r.Clients, r.PaginationResponse?.TotalResults);
        });

        var client = matches
            .Where(c => string.Equals(c.Email?.Trim(), email, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(c.Id))
            .OrderByDescending(c => c.Active != false)
            .FirstOrDefault();
        if (client?.Id == null)
        {
            return null;
        }

        _cache.Set(key, client.Id, TimeSpan.FromHours(12));
        return client.Id;
    }

    /// <summary>The shopper's studio client, created from their eShop identity when this is their first booking.</summary>
    private async Task<string> EnsureClientAsync(string userName, StudioClientProfile? profile, CancellationToken ct)
    {
        var email = EmailOf(userName);
        var existing = await FindClientIdAsync(email, ct);
        if (existing != null)
        {
            return existing;
        }

        var request = await BuildNewClientAsync(email, profile, ct);
        try
        {
            var created = await _mindbody.CallAsync((api, site, token) => api.ClientApi.AddClient(
                new AddClientOperationRequest { Authorization = token, SiteId = site, Body = request }), ct);
            var id = created.Client?.Id;
            if (string.IsNullOrEmpty(id))
            {
                throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not create your client record.");
            }
            _cache.Set("studio:client:" + email.ToLowerInvariant(), id, TimeSpan.FromHours(12));
            return id;
        }
        catch (StudioException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Created in the meantime, for example by a request that was repeated after its answer got lost.
            _cache.Remove("studio:client:" + email.ToLowerInvariant());
            var again = await FindClientIdAsync(email, ct);
            if (again != null)
            {
                return again;
            }
            throw;
        }
    }

    private async Task<AddClientRequest> BuildNewClientAsync(string email, StudioClientProfile? p, CancellationToken ct)
    {
        var missing = new List<string>();
        if (p?.HomeLocationId == null) missing.Add("homeLocationId");
        if (string.IsNullOrWhiteSpace(p?.StreetAddress)) missing.Add("streetAddress");
        if (string.IsNullOrWhiteSpace(p?.City)) missing.Add("city");
        if (string.IsNullOrWhiteSpace(p?.State)) missing.Add("state");
        if (string.IsNullOrWhiteSpace(p?.PostalCode)) missing.Add("postalCode");
        if (string.IsNullOrWhiteSpace(p?.DateOfBirth)) missing.Add("dateOfBirth");
        if (string.IsNullOrWhiteSpace(p?.MobilePhone)) missing.Add("mobilePhone");
        if (string.IsNullOrWhiteSpace(p?.ReferredBy)) missing.Add("referredBy");
        if (missing.Count > 0)
        {
            throw StudioException.BadRequest(
                "This is your first booking, so the studio needs some details to create your client record. Missing: " +
                string.Join(", ", missing) + ".");
        }

        var birthDate = ParseDate(p!.DateOfBirth!);
        if (birthDate == null || birthDate > DateOnly.FromDateTime(DateTime.UtcNow) || birthDate < new DateOnly(1900, 1, 1))
        {
            throw StudioException.BadRequest("dateOfBirth is not a valid date of birth.");
        }

        var mobile = p.MobilePhone!.Trim();
        if (mobile.Count(char.IsDigit) < 7)
        {
            throw StudioException.BadRequest("mobilePhone is not a valid phone number.");
        }

        var location = (await GetLocationsAsync(ct)).FirstOrDefault(l => l.Id == p.HomeLocationId);
        if (location == null)
        {
            throw StudioException.BadRequest($"homeLocationId {p.HomeLocationId} is not one of the studio's locations.");
        }

        var referral = p.ReferredBy!.Trim();
        var referralTypes = await GetReferralTypesAsync(ct);
        if (referralTypes.Count > 0)
        {
            var canonical = referralTypes.FirstOrDefault(r => string.Equals(r, referral, StringComparison.OrdinalIgnoreCase));
            if (canonical == null)
            {
                throw StudioException.BadRequest("referredBy must be one of: " + string.Join(", ", referralTypes) + ".");
            }
            referral = canonical;
        }

        var (first, last) = NameOf(email, p);
        return new AddClientRequest
        {
            FirstName = first,
            LastName = last,
            Email = email,
            AddressLine1 = p.StreetAddress!.Trim(),
            City = p.City!.Trim(),
            State = p.State!.Trim(),
            PostalCode = p.PostalCode!.Trim(),
            // Mindbody stores the birth date as given, without converting it to the studio time zone.
            BirthDate = new DateTimeOffset(birthDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            MobilePhone = mobile,
            ReferredBy = referral,
            HomeLocation = new MindbodyPublicApi.Models.Location { Id = location.Id },
        };
    }

    private static DateOnly? ParseDate(string value)
    {
        value = value.Trim();
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            return DateOnly.FromDateTime(dto.DateTime);
        }
        return null;
    }

    // The eShop identity holds no name: use what the shopper sent, else derive one from the e-mail address.
    private static (string First, string Last) NameOf(string email, StudioClientProfile p)
    {
        var tokens = email[..email.IndexOf('@')]
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => char.ToUpperInvariant(t[0]) + t[1..])
            .ToList();
        var first = string.IsNullOrWhiteSpace(p.FirstName) ? (tokens.FirstOrDefault() ?? "Shopper") : p.FirstName.Trim();
        var last = string.IsNullOrWhiteSpace(p.LastName)
            ? (tokens.Count > 1 ? string.Join(" ", tokens.Skip(1)) : "Shopper")
            : p.LastName.Trim();
        return (first, last);
    }

    private async Task<T> WithShopperLockAsync<T>(string key, Func<Task<T>> action, CancellationToken ct)
    {
        var gate = _shopperLocks[(uint)key.ToLowerInvariant().GetHashCode() % (uint)_shopperLocks.Length];
        await gate.WaitAsync(ct);
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }

    // ---------------------------------------------------------------- 3. book

    /// <summary>Books a place in a class. Returns the booking and whether it was made by this call.</summary>
    public Task<(StudioBookingDto Booking, bool Created)> BookClassAsync(string userName, ClassBookingRequest request, CancellationToken ct) =>
        WithShopperLockAsync(userName, async () =>
        {
            if (request.ClassId <= 0)
            {
                throw StudioException.BadRequest("'classId' is required.");
            }
            var clock = await GetClockAsync(ct);
            var klass = await GetClassAsync(request.ClassId, ct)
                ?? throw StudioException.NotFound($"There is no class {request.ClassId}.");

            var clientId = await EnsureClientAsync(userName, request, ct);
            var existing = await FindClassVisitAsync(klass, clientId, ct);
            if (existing != null)
            {
                return (ToClassBooking(klass, existing.Id!.Value, clientId, clock), false);
            }

            if (!IsAttendable(klass, clock, DateTimeOffset.UtcNow))
            {
                throw StudioException.Conflict("This class can no longer be booked: it is cancelled, over or not open to shoppers.");
            }
            if (klass.MaxCapacity.HasValue && (klass.TotalBooked ?? 0) >= klass.MaxCapacity.Value)
            {
                throw StudioException.Conflict("This class is full.");
            }

            var visit = await AddToClassAsync(klass.Id.Value, clientId, ct);
            return (ToClassBooking(klass, visit, clientId, clock), true);
        }, ct);

    /// <summary>Places a shopper in any class of the timetable (front desk).</summary>
    public async Task<(StudioBookingDto Booking, bool Created)> BookClassForShopperAsync(string shopperUserName, int classId, CancellationToken ct)
    {
        return await WithShopperLockAsync(shopperUserName, async () =>
        {
            var clock = await GetClockAsync(ct);
            var email = EmailOf(shopperUserName);
            var klass = await GetClassAsync(classId, ct)
                ?? throw StudioException.NotFound($"There is no class {classId} in the timetable.");

            var clientId = await FindClientIdAsync(email, ct)
                ?? throw StudioException.Conflict(
                    "This shopper is not a client of the studio yet; they become one with their first booking in the app.");

            var existing = await FindClassVisitAsync(klass, clientId, ct);
            if (existing != null)
            {
                return (ToClassBooking(klass, existing.Id!.Value, clientId, clock), false);
            }

            var visit = await AddToClassAsync(classId, clientId, ct);
            return (ToClassBooking(klass, visit, clientId, clock), true);
        }, ct);
    }

    private async Task<long> AddToClassAsync(int classId, string clientId, CancellationToken ct)
    {
        // Writes are not tied to the caller's connection: once started they run to the end.
        var response = await _mindbody.CallAsync((api, site, token) => api.ClassApi.AddClientToClass(
            new AddClientToClassOperationRequest
            {
                Authorization = token,
                SiteId = site,
                Body = new AddClientToClassRequest { ClientId = clientId, ClassId = classId, SendEmail = false },
            }));
        return response.Visit?.Id
            ?? throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not confirm the booking.");
    }

    private async Task<Class?> GetClassAsync(int classId, CancellationToken ct)
    {
        var r = await _mindbody.CallAsync((api, site, token) => api.ClassApi.GetClasses(new GetClassesOperationRequest
        {
            Authorization = token,
            SiteId = site,
            RequestClassIds = new[] { classId },
            // Mindbody only looks inside a date window (today when none is given), so ask for the whole timetable.
            RequestStartDateTime = new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero),
            RequestEndDateTime = new DateTimeOffset(2100, 1, 1, 12, 0, 0, TimeSpan.Zero),
            RequestLimit = 10,
        }), ct);
        return r.Classes?.FirstOrDefault(c => c.Id == classId);
    }

    // The client's schedule around the class lists the visit (GetClassVisits does not read back reliably).
    private async Task<VisitWithWaitlistInfo?> FindClassVisitAsync(Class klass, string clientId, CancellationToken ct)
    {
        var start = StudioClock.Wall(klass.StartDateTime!.Value);
        var schedule = await GetScheduleAsync(clientId, new DateTimeOffset(start.AddDays(-1), TimeSpan.Zero),
            new DateTimeOffset(start.AddDays(2), TimeSpan.Zero), ct);
        return schedule.FirstOrDefault(v => v.ClassId == klass.Id && (v.Id ?? 0) > 0);
    }

    private static StudioBookingDto ToClassBooking(Class klass, long visitId, string clientId, StudioClock clock) => new()
    {
        BookingId = visitId,
        Kind = "class",
        ClassId = klass.Id,
        Name = klass.ClassDescription?.Name,
        StartTime = clock.FromWall(klass.StartDateTime!.Value),
        EndTime = clock.FromWall(klass.EndDateTime!.Value),
        LocationId = klass.Location?.Id,
        TrainerId = klass.Staff?.Id,
        ClientId = clientId,
    };

    public Task<(StudioBookingDto Booking, bool Created)> BookAppointmentAsync(string userName, AppointmentBookingRequest request, CancellationToken ct) =>
        WithShopperLockAsync(userName, async () =>
        {
            if (request.SessionTypeId <= 0)
            {
                throw StudioException.BadRequest("'sessionTypeId' is required.");
            }
            if (request.StartTime == default)
            {
                throw StudioException.BadRequest("'startTime' is required: an ISO-8601 date-time.");
            }
            var clock = await GetClockAsync(ct);
            var type = await GetAppointmentSessionTypeAsync(request.SessionTypeId, ct);
            var minutes = type.DefaultTimeLength ?? type.StaffTimeLength;
            if (minutes is null or <= 0)
            {
                throw StudioException.Unprocessable($"Session type {type.Id} ({type.Name}) has no length set in the studio.");
            }

            var start = clock.ToStudio(request.StartTime);
            if (start <= DateTimeOffset.UtcNow)
            {
                throw StudioException.BadRequest("startTime must be in the future.");
            }
            var wall = start.DateTime;
            var end = clock.AddMinutes(start, minutes.Value);

            var clientId = await EnsureClientAsync(userName, request, ct);

            // A repeated request finds the session already booked.
            var existing = await FindAppointmentAsync(clientId, wall, type.Name, ct);
            if (existing != null)
            {
                return (ToAppointmentBooking(existing.Value.Id, type, start, end, existing.Value.LocationId, existing.Value.StaffId, clientId), false);
            }

            var onGrid = (await GetStartTimesOfDayAsync(type.Id!.Value, ct)).Contains(TimeOnly.FromDateTime(wall));
            if (!onGrid)
            {
                throw StudioException.Unprocessable("The studio does not start this kind of session at that time.");
            }

            var homeLocation = request.SessionLocationId ?? await GetHomeLocationAsync(clientId, ct);
            var slot = await FindFreeTrainerAsync(type.Id.Value, clock, wall, end, request.TrainerId, homeLocation, ct)
                ?? throw StudioException.Conflict("No trainer is free for this session at that time.");

            var body = new AddAppointmentRequest
            {
                ClientId = clientId,
                SessionTypeId = type.Id,
                StaffId = slot.StaffId,
                StaffRequested = request.TrainerId.HasValue,
                LocationId = slot.LocationId,
                SendEmail = false,
            };
            // Mindbody reads a start time with an explicit UTC offset as if it were the studio's own clock, which books the
            // wrong hour. It wants the studio's wall-clock time, so that is sent as given (instead of the typed property).
            body.AdditionalProperties.Set("StartDateTime", wall.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));

            var response = await _mindbody.CallAsync((api, site, token) => api.AppointmentApi.AddAppointment(
                new AddAppointmentOperationRequest { Authorization = token, SiteId = site, Body = body }));
            var appointmentId = response.Appointment?.Id
                ?? throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not confirm the booking.");
            return (ToAppointmentBooking(appointmentId, type, start, end, slot.LocationId, slot.StaffId, clientId), true);
        }, ct);

    private async Task<int?> GetHomeLocationAsync(string clientId, CancellationToken ct)
    {
        var r = await _mindbody.CallAsync((api, site, token) => api.ClientApi.GetClients(new GetClientsOperationRequest
        {
            Authorization = token,
            SiteId = site,
            RequestClientIDs = new[] { clientId },
        }), ct);
        return r.Clients?.FirstOrDefault(c => c.Id == clientId)?.HomeLocation?.Id;
    }

    private async Task<(long StaffId, int LocationId)?> FindFreeTrainerAsync(int sessionTypeId, StudioClock clock, DateTime wallStart,
        DateTimeOffset end, long? trainerId, int? locationId, CancellationToken ct)
    {
        var day = DateOnly.FromDateTime(wallStart);
        var from = clock.StartOfDay(day);
        var to = clock.StartOfDay(day.AddDays(1)).AddSeconds(-1);
        var wallEnd = end.DateTime;

        var items = await MindbodyConnection.PageAllAsync<Availability>(async (limit, offset) =>
        {
            var r = await _mindbody.CallAsync((api, site, token) => api.AppointmentApi.GetBookableItems(new GetBookableItemsOperationRequest
            {
                Authorization = token,
                SiteId = site,
                RequestSessionTypeIds = new[] { sessionTypeId },
                RequestStartDate = from,
                RequestEndDate = to,
                RequestLocationIds = locationId.HasValue ? new[] { locationId.Value } : null,
                RequestStaffIds = trainerId.HasValue ? new[] { trainerId.Value } : null,
                RequestLimit = limit,
                RequestOffset = offset,
            }), ct);
            return (r.Availabilities, r.PaginationResponse?.TotalResults);
        });

        var match = items.FirstOrDefault(a =>
            a.Staff?.Id != null && a.Location?.Id != null && a.StartDateTime.HasValue && a.EndDateTime.HasValue &&
            (!trainerId.HasValue || a.Staff.Id == trainerId) &&
            (!locationId.HasValue || a.Location.Id == locationId) &&
            StudioClock.Wall(a.StartDateTime.Value) <= wallStart &&
            StudioClock.Wall(a.EndDateTime.Value) >= wallEnd);
        return match == null ? null : (match.Staff!.Id!.Value, match.Location!.Id!.Value);
    }

    private static StudioBookingDto ToAppointmentBooking(long id, SessionType type, DateTimeOffset start, DateTimeOffset end,
        int? locationId, long? staffId, string clientId) => new()
    {
        BookingId = id,
        Kind = "appointment",
        SessionTypeId = type.Id,
        Name = type.Name,
        StartTime = start,
        EndTime = end,
        LocationId = locationId,
        TrainerId = staffId,
        ClientId = clientId,
    };

    // ---------------------------------------------------------------- the shopper's own bookings

    private Task<List<VisitWithWaitlistInfo>> GetScheduleAsync(string clientId, CancellationToken ct) =>
        GetScheduleAsync(clientId, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddYears(2), ct);

    private async Task<List<VisitWithWaitlistInfo>> GetScheduleAsync(string clientId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        return await MindbodyConnection.PageAllAsync<VisitWithWaitlistInfo>(async (limit, offset) =>
        {
            var r = await _mindbody.CallAsync((api, site, token) => api.ClientApi.GetClientSchedule(new GetClientScheduleOperationRequest
            {
                Authorization = token,
                SiteId = site,
                RequestClientId = clientId,
                RequestStartDate = from,
                RequestEndDate = to,
                RequestLimit = limit,
                RequestOffset = offset,
            }), ct);
            return (r.Visits, r.PaginationResponse?.TotalResults);
        });
    }

    private async Task<(long Id, int? LocationId, long? StaffId)?> FindAppointmentAsync(string clientId, DateTime wallStart, string? typeName, CancellationToken ct)
    {
        var schedule = await GetScheduleAsync(clientId, new DateTimeOffset(wallStart.Date.AddDays(-1), TimeSpan.Zero),
            new DateTimeOffset(wallStart.Date.AddDays(2), TimeSpan.Zero), ct);
        var hit = schedule.FirstOrDefault(v =>
            (v.AppointmentId ?? 0) > 0 && v.StartDateTime.HasValue &&
            StudioClock.Wall(v.StartDateTime.Value) == wallStart &&
            (typeName == null || v.Name == typeName) &&
            IsLive(v));
        return hit == null ? null : (hit.AppointmentId!.Value, hit.LocationId, hit.StaffId);
    }

    private static bool IsLive(VisitWithWaitlistInfo v)
    {
        var status = v.AppointmentStatus?.Value;
        return status is null or "None" or "Requested" or "Booked" or "Confirmed" or "Arrived" or "Completed";
    }

    // ---------------------------------------------------------------- 4. cancel

    public Task CancelClassBookingAsync(string userName, long bookingId, CancellationToken ct) =>
        CancelAsync(userName, "class", bookingId, async (clientId) =>
        {
            var visit = (await GetScheduleAsync(clientId, ct)).FirstOrDefault(v => v.Id == bookingId && (v.ClassId ?? 0) > 0);
            if (visit == null)
            {
                return false;
            }
            await _mindbody.CallAsync((api, site, token) => SucceededAsync(api.ClassApi.RemoveClientFromClass(
                new RemoveClientFromClassOperationRequest
                {
                    Authorization = token,
                    SiteId = site,
                    Body = new RemoveClientFromClassRequest
                    {
                        ClientId = clientId,
                        ClassId = visit.ClassId!.Value,
                        VisitId = (int)bookingId,
                        SendEmail = false,
                    },
                })));
            return true;
        }, ct);

    public Task CancelAppointmentBookingAsync(string userName, long bookingId, CancellationToken ct) =>
        CancelAsync(userName, "appointment", bookingId, async (clientId) =>
        {
            var visit = (await GetScheduleAsync(clientId, ct)).FirstOrDefault(v => v.AppointmentId == bookingId && IsLive(v));
            if (visit == null)
            {
                return false;
            }
            await _mindbody.CallAsync((api, site, token) => SucceededAsync(api.AppointmentApi.UpdateAppointment(
                new UpdateAppointmentOperationRequest
                {
                    Authorization = token,
                    SiteId = site,
                    Body = new UpdateAppointmentRequest { AppointmentId = bookingId, Execute = "Cancel", SendEmail = false },
                })));
            return true;
        }, ct);

    /// <summary>
    /// Cancels a booking of the caller. Mindbody forgets a booking once it is cancelled, so a cancellation that is
    /// repeated (the app asks again when it got no answer) is recognised from our own record of it and also succeeds.
    /// A booking that is not the caller's is answered as not found.
    /// </summary>
    private Task CancelAsync(string userName, string kind, long bookingId, Func<string, Task<bool>> cancel, CancellationToken ct) =>
        WithShopperLockAsync(userName, async () =>
        {
            var email = EmailOf(userName);
            var clientId = await FindClientIdAsync(email, ct);
            var memo = $"studio:cancelled:{kind}:{email.ToLowerInvariant()}:{bookingId}";
            if (clientId != null && await cancel(clientId))
            {
                _cache.Set(memo, true, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromDays(30) });
                return true;
            }
            if (_cache.TryGetValue(memo, out _))
            {
                return true;
            }
            throw StudioException.NotFound($"You have no {kind} booking {bookingId}.");
        }, ct);

    // For writes whose answer is only an echo of what was done: when Mindbody accepted the request (2xx) but the
    // echo cannot be read back by the SDK (for example a 0001-01-01 timestamp), the write has still happened.
    private static async Task<bool> SucceededAsync<T>(Task<T> write)
    {
        try
        {
            await write;
        }
        catch (MindbodyPublicApi.Core.Exceptions.ResponseDeserializationException ex) when ((int)ex.StatusCode is >= 200 and < 300)
        {
        }
        return true;
    }

    // ---------------------------------------------------------------- 5. trainers

    public async Task<IReadOnlyList<StudioTrainerDto>> GetTrainersAsync(CancellationToken ct)
    {
        var staff = await MindbodyConnection.PageAllAsync<Staff>(async (limit, offset) =>
        {
            var r = await _mindbody.CallAsync((api, site, token) => api.StaffApi.GetStaff(new GetStaffOperationRequest
            {
                Authorization = token,
                SiteId = site,
                RequestLimit = limit,
                RequestOffset = offset,
            }), ct);
            return (r.StaffMembers, r.PaginationResponse?.TotalResults);
        });

        return staff
            .Where(s => s.Id.HasValue && s.Id.Value > 0)
            .GroupBy(s => s.Id!.Value).Select(g => g.First())
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ThenBy(s => s.Id)
            .Select(s => new StudioTrainerDto
            {
                Id = s.Id!.Value,
                FirstName = s.FirstName,
                LastName = s.LastName,
                DisplayName = s.DisplayName ?? s.Name,
                Active = s.Active != false,
                ClassTeacher = s.ClassTeacher == true,
                AppointmentInstructor = s.AppointmentInstructor == true,
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
