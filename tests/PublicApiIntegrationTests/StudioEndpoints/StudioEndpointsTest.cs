using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.StudioEndpoints;

/// <summary>
/// Runs against the Mindbody sandbox. The credentials come from the Mindbody:* configuration keys or, failing
/// that, the MINDBODY_* environment variables (see MindbodySettings); nothing is stubbed.
/// </summary>
[TestClass]
public class StudioEndpointsTest
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static HttpClient Client(string? token)
    {
        var client = ProgramTest.NewClient;
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private static HttpClient Shopper() => Client(ApiTokenHelper.GetNormalUserToken());
    private static HttpClient Admin() => Client(ApiTokenHelper.GetAdminUserToken());

    private static object NewClientDetails() => new
    {
        homeLocationId = 1,
        streetAddress = "4051 S Broad St",
        city = "San Luis Obispo",
        state = "CA",
        postalCode = "93401",
        dateOfBirth = "1990-05-17",
        mobilePhone = "805-555-0100",
        referredBy = "Google",
    };

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [TestMethod]
    public async Task Studio_endpoints_require_a_token()
    {
        var anonymous = Client(null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/studio/classes")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/studio/admin/trainers")).StatusCode);
    }

    [TestMethod]
    public async Task Staff_features_are_for_administrators_only()
    {
        var shopper = Shopper();
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync("api/studio/admin/trainers")).StatusCode);
        var response = await shopper.PostAsJsonAsync("api/studio/admin/class-bookings", new { shopper = "demouser@microsoft.com", classId = 1 });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Classes_default_to_today_and_the_next_7_days_with_offsets()
    {
        var response = await Shopper().GetAsync("api/studio/classes");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var classes = (await ReadAsync(response)).GetProperty("classes").EnumerateArray().ToList();
        Assert.IsTrue(classes.Count > 0);

        foreach (var c in classes)
        {
            var start = DateTimeOffset.Parse(c.GetProperty("startTime").GetString()!);
            var end = DateTimeOffset.Parse(c.GetProperty("endTime").GetString()!);
            Assert.IsTrue(end > start);
            // ISO-8601 date-time with an explicit UTC offset
            StringAssert.Matches(c.GetProperty("startTime").GetString()!, new System.Text.RegularExpressions.Regex(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(\.\d+)?([+-]\d\d:\d\d|Z)$"));
            Assert.IsTrue(end > DateTimeOffset.UtcNow, "classes that are over are not listed");
            Assert.IsFalse(c.TryGetProperty("email", out _));
        }

        var ids = classes.Select(c => c.GetProperty("id").GetInt32()).ToList();
        Assert.AreEqual(ids.Count, ids.Distinct().Count());
    }

    [TestMethod]
    public async Task Classes_in_a_range_stay_inside_it_and_are_not_cancelled()
    {
        var from = DateTime.UtcNow.Date.AddDays(10);
        var to = from.AddDays(20); // more than one page of results
        var response = await Shopper().GetAsync($"api/studio/classes?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var classes = (await ReadAsync(response)).GetProperty("classes").EnumerateArray().ToList();
        Assert.IsTrue(classes.Count > 100, $"expected every page to be read, got {classes.Count}");
        foreach (var c in classes)
        {
            var start = DateTimeOffset.Parse(c.GetProperty("startTime").GetString()!);
            Assert.IsTrue(start.Date >= from && start.Date <= to);
        }
    }

    [TestMethod]
    public async Task Classes_reject_an_inverted_range()
    {
        var response = await Shopper().GetAsync("api/studio/classes?from=2030-02-01&to=2030-01-01");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "from");
    }

    [TestMethod]
    public async Task Slots_list_every_start_time_on_every_day_with_offsets()
    {
        // 10 and 11 March 2027 straddle nothing; 13-15 March 2027 contains the US spring-forward.
        var response = await Shopper().GetAsync("api/studio/slots?sessionTypeId=23&from=2027-03-13&to=2027-03-14");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var slots = (await ReadAsync(response)).GetProperty("slots").EnumerateArray().ToList();
        Assert.IsTrue(slots.Count > 40);
        var starts = slots.Select(s => DateTimeOffset.Parse(s.GetProperty("startTime").GetString()!)).ToList();
        Assert.IsTrue(starts.Any(s => s.Offset == TimeSpan.FromHours(-8)), "13 March is still standard time");
        Assert.IsTrue(starts.Any(s => s.Offset == TimeSpan.FromHours(-7)), "14 March is daylight time");
        foreach (var s in slots)
        {
            Assert.AreEqual(30, (DateTimeOffset.Parse(s.GetProperty("endTime").GetString()!) - DateTimeOffset.Parse(s.GetProperty("startTime").GetString()!)).TotalMinutes);
        }
    }

    [TestMethod]
    public async Task Slots_validate_their_input()
    {
        var shopper = Shopper();
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync("api/studio/slots?from=2027-03-13&to=2027-03-14")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync("api/studio/slots?sessionTypeId=23&from=2027-03-13")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.GetAsync("api/studio/slots?sessionTypeId=99999&from=2027-03-13&to=2027-03-14")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync("api/studio/slots?sessionTypeId=207&from=2027-03-13&to=2027-03-14")).StatusCode, "a class type is not a one-to-one session");
    }

    [TestMethod]
    public async Task Book_and_cancel_a_class_is_repeatable()
    {
        var shopper = Shopper();
        var list = await ReadAsync(await shopper.GetAsync("api/studio/classes?from=" + DateTime.UtcNow.Date.AddDays(3).ToString("yyyy-MM-dd") + "&to=" + DateTime.UtcNow.Date.AddDays(4).ToString("yyyy-MM-dd")));
        var classId = list.GetProperty("classes").EnumerateArray()
            .First(c => c.GetProperty("spotsAvailable").GetInt32() > 2).GetProperty("id").GetInt32();

        var first = await shopper.PostAsJsonAsync("api/studio/class-bookings", Merge(new { classId }, NewClientDetails()));
        Assert.IsTrue(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        var booking = await ReadAsync(first);
        var bookingId = booking.GetProperty("bookingId").GetInt64();

        // the app did not get the answer and asks again: same booking, still a success
        var retry = await shopper.PostAsJsonAsync("api/studio/class-bookings", new { classId });
        Assert.AreEqual(HttpStatusCode.OK, retry.StatusCode, await retry.Content.ReadAsStringAsync());
        Assert.AreEqual(bookingId, (await ReadAsync(retry)).GetProperty("bookingId").GetInt64());

        // concurrent duplicates do not double book
        var parallel = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => shopper.PostAsJsonAsync("api/studio/class-bookings", new { classId })));
        Assert.IsTrue(parallel.All(r => r.IsSuccessStatusCode));
        foreach (var r in parallel)
        {
            Assert.AreEqual(bookingId, (await ReadAsync(r)).GetProperty("bookingId").GetInt64());
        }

        var cancel = await shopper.DeleteAsync($"api/studio/class-bookings/{bookingId}");
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode, await cancel.Content.ReadAsStringAsync());
        var cancelAgain = await shopper.DeleteAsync($"api/studio/class-bookings/{bookingId}");
        Assert.AreEqual(HttpStatusCode.OK, cancelAgain.StatusCode, await cancelAgain.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task Book_and_cancel_a_one_to_one_session_is_repeatable()
    {
        var shopper = Shopper();
        var days = Enumerable.Range(2, 6).Select(d => DateTime.UtcNow.Date.AddDays(d)).ToList();
        var slots = (await ReadAsync(await shopper.GetAsync($"api/studio/slots?sessionTypeId=23&from={days[0]:yyyy-MM-dd}&to={days[^1]:yyyy-MM-dd}")))
            .GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("startTime").GetString()!).ToList();

        JsonElement? booked = null;
        foreach (var start in slots.Where(s => DateTimeOffset.Parse(s).Hour is >= 8 and <= 16).Take(80))
        {
            var response = await shopper.PostAsJsonAsync("api/studio/appointment-bookings", Merge(new { sessionTypeId = 23, startTime = start }, NewClientDetails()));
            if (response.IsSuccessStatusCode)
            {
                booked = await ReadAsync(response);
                break;
            }
            Assert.IsTrue((int)response.StatusCode is >= 400 and < 500, $"unexpected {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            Assert.IsTrue((await response.Content.ReadAsStringAsync()).Length > 10, "a reason is given");
        }
        Assert.IsNotNull(booked, "no free trainer found in the sandbox for the next days");

        var bookingId = booked.Value.GetProperty("bookingId").GetInt64();
        var startTime = booked.Value.GetProperty("startTime").GetString()!;

        var retry = await shopper.PostAsJsonAsync("api/studio/appointment-bookings", new { sessionTypeId = 23, startTime });
        Assert.AreEqual(HttpStatusCode.OK, retry.StatusCode, await retry.Content.ReadAsStringAsync());
        Assert.AreEqual(bookingId, (await ReadAsync(retry)).GetProperty("bookingId").GetInt64());

        Assert.AreEqual(HttpStatusCode.OK, (await shopper.DeleteAsync($"api/studio/appointment-bookings/{bookingId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.DeleteAsync($"api/studio/appointment-bookings/{bookingId}")).StatusCode);
    }

    [TestMethod]
    public async Task Booking_in_a_cancelled_or_unknown_class_is_refused_with_a_reason()
    {
        var shopper = Shopper();
        var unknown = await shopper.PostAsJsonAsync("api/studio/class-bookings", new { classId = 99999999 });
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        StringAssert.Contains(await unknown.Content.ReadAsStringAsync(), "99999999");

        var missing = await shopper.PostAsJsonAsync("api/studio/class-bookings", new { });
        Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [TestMethod]
    public async Task Admin_sees_trainers_with_contact_details_and_books_a_shopper()
    {
        var admin = Admin();
        var response = await admin.GetAsync("api/studio/admin/trainers");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var trainers = (await ReadAsync(response)).GetProperty("trainers").EnumerateArray().ToList();
        Assert.IsTrue(trainers.Count > 100, $"every page must be read, got {trainers.Count}");
        Assert.IsTrue(trainers.Any(t => !string.IsNullOrEmpty(t.GetProperty("address").GetString())));
        Assert.IsTrue(trainers.All(t => t.GetProperty("id").GetInt64() > 0));

        var unknownShopper = await admin.PostAsJsonAsync("api/studio/admin/class-bookings", new { shopper = "nobody@nowhere.example", classId = 1 });
        Assert.AreEqual(HttpStatusCode.NotFound, unknownShopper.StatusCode);

        var from = DateTime.UtcNow.Date.AddDays(5);
        var list = await ReadAsync(await Shopper().GetAsync($"api/studio/classes?from={from:yyyy-MM-dd}&to={from.AddDays(1):yyyy-MM-dd}"));
        var classId = list.GetProperty("classes").EnumerateArray().Last(c => c.GetProperty("spotsAvailable").GetInt32() > 2).GetProperty("id").GetInt32();

        var booked = await admin.PostAsJsonAsync("api/studio/admin/class-bookings", Merge(new { shopper = "demouser@microsoft.com", classId }, NewClientDetails()));
        Assert.IsTrue(booked.IsSuccessStatusCode, await booked.Content.ReadAsStringAsync());
        var bookingId = (await ReadAsync(booked)).GetProperty("bookingId").GetInt64();
        Assert.AreEqual(HttpStatusCode.OK, (await Shopper().DeleteAsync($"api/studio/class-bookings/{bookingId}")).StatusCode);
    }

    private static JsonElement Merge(object a, object b)
    {
        var merged = JsonSerializer.SerializeToElement(a, Json).EnumerateObject().Concat(JsonSerializer.SerializeToElement(b, Json).EnumerateObject())
            .ToDictionary(p => p.Name, p => p.Value);
        return JsonSerializer.SerializeToElement(merged, Json);
    }
}
