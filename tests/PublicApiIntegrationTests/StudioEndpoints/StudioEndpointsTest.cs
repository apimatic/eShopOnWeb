using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.StudioEndpoints;

/// <summary>
/// Runs against the Mindbody sandbox (credentials come from the MINDBODY_* environment variables).
/// </summary>
[TestClass]
public class StudioEndpointsTest
{
    private static readonly Regex IsoWithOffset = new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?([+-]\d{2}:\d{2}|Z)$");

    private static HttpClient ClientFor(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient Shopper(string email) => ClientFor(ApiTokenHelper.GetUserToken(email));
    private static HttpClient Admin() => ClientFor(ApiTokenHelper.GetAdminUserToken());

    private static string NewEmail() => $"it.{Guid.NewGuid():N}@example.com";

    private static object Details(string? referredBy = "Google") => new
    {
        homeLocationId = 1,
        streetAddress = "1 Main St",
        city = "Austin",
        state = "TX",
        postalCode = "78701",
        dateOfBirth = "1990-05-17",
        mobilePhone = "5125550123",
        referredBy
    };

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [TestMethod]
    public async Task ClassesRequireAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/studio/classes");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ClassesDefaultToTodayAndSevenDaysWithOffsets()
    {
        var response = await Shopper("demouser@microsoft.com").GetAsync("api/studio/classes");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var classes = (await Json(response)).EnumerateArray().ToList();
        Assert.IsTrue(classes.Count > 0);
        foreach (var c in classes)
        {
            Assert.IsTrue(IsoWithOffset.IsMatch(c.GetProperty("startTime").GetString()!));
            Assert.IsTrue(IsoWithOffset.IsMatch(c.GetProperty("endTime").GetString()!));
            Assert.IsTrue(DateTimeOffset.Parse(c.GetProperty("endTime").GetString()!) > DateTimeOffset.UtcNow);
            Assert.IsTrue(DateTimeOffset.Parse(c.GetProperty("startTime").GetString()!) < DateTimeOffset.UtcNow.AddDays(9));
        }
    }

    [TestMethod]
    public async Task ClassesRejectsAnInvertedRange()
    {
        var response = await Shopper("demouser@microsoft.com").GetAsync("api/studio/classes?from=2030-01-10&to=2030-01-01");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SlotsAreListedOncePerStartTime()
    {
        var from = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd");
        var response = await Shopper("demouser@microsoft.com").GetAsync($"api/studio/slots?sessionTypeId=23&from={from}&to={to}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var slots = (await Json(response)).EnumerateArray().ToList();
        Assert.IsTrue(slots.Count > 0);
        var starts = slots.Select(s => s.GetProperty("startTime").GetString()!).ToList();
        Assert.AreEqual(starts.Count, starts.Distinct().Count());
        foreach (var s in slots)
        {
            Assert.IsTrue(IsoWithOffset.IsMatch(s.GetProperty("startTime").GetString()!));
            Assert.IsTrue(DateTimeOffset.Parse(s.GetProperty("endTime").GetString()!) > DateTimeOffset.Parse(s.GetProperty("startTime").GetString()!));
        }
    }

    [TestMethod]
    public async Task SlotsRejectAClassSessionType()
    {
        var response = await Shopper("demouser@microsoft.com").GetAsync("api/studio/slots?sessionTypeId=10&from=2030-01-01&to=2030-01-02");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task FirstBookingNeedsTheNewClientDetails()
    {
        var response = await Shopper(NewEmail()).PostAsJsonAsync("api/studio/class-bookings", new { classId = await FirstClassId() });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "dateOfBirth");
    }

    [TestMethod]
    public async Task ClassBookingIsCreatedOnceAndCanBeCancelledOnRetry()
    {
        var email = NewEmail();
        var shopper = Shopper(email);
        var classId = await FirstClassId();
        var body = new
        {
            classId,
            homeLocationId = 1,
            streetAddress = "1 Main St",
            city = "Austin",
            state = "TX",
            postalCode = "78701",
            dateOfBirth = "1990-05-17",
            mobilePhone = "5125550123",
            referredBy = "google"
        };

        var first = await shopper.PostAsJsonAsync("api/studio/class-bookings", body);
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode, await first.Content.ReadAsStringAsync());
        var id = (await Json(first)).GetProperty("bookingId").GetInt64();

        var retry = await shopper.PostAsJsonAsync("api/studio/class-bookings", body);
        Assert.AreEqual(HttpStatusCode.OK, retry.StatusCode);
        Assert.AreEqual(id, (await Json(retry)).GetProperty("bookingId").GetInt64());

        var stranger = await Shopper(NewEmail()).DeleteAsync($"api/studio/class-bookings/{id}");
        Assert.AreEqual(HttpStatusCode.NotFound, stranger.StatusCode);

        var cancel = await shopper.DeleteAsync($"api/studio/class-bookings/{id}");
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode, await cancel.Content.ReadAsStringAsync());
        var cancelAgain = await shopper.DeleteAsync($"api/studio/class-bookings/{id}");
        Assert.AreEqual(HttpStatusCode.OK, cancelAgain.StatusCode);
    }

    [TestMethod]
    public async Task AppointmentBookingRoundTrip()
    {
        var email = NewEmail();
        var shopper = Shopper(email);
        var from = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.AddDays(6).ToString("yyyy-MM-dd");
        var slots = await Json(await shopper.GetAsync($"api/studio/slots?sessionTypeId=23&from={from}&to={to}"));

        // The studio's time grid is listed whether or not a trainer is free, so look for a time that is bookable.
        object? body = null;
        HttpResponseMessage? first = null;
        foreach (var slot in slots.EnumerateArray()
                     .Select(s => s.GetProperty("startTime").GetString()!)
                     .Where(t => Regex.IsMatch(t, @"T(09|10|11|12|13|14|15):(00|30):00")))
        {
            body = new
            {
                sessionTypeId = 23,
                startTime = slot,
                homeLocationId = 1,
                streetAddress = "1 Main St",
                city = "Austin",
                state = "TX",
                postalCode = "78701",
                dateOfBirth = "1990-05-17",
                mobilePhone = "5125550123",
                referredBy = "Google"
            };
            first = await shopper.PostAsJsonAsync("api/studio/appointment-bookings", body);
            if (first.StatusCode == HttpStatusCode.Created) break;
            Assert.AreEqual(HttpStatusCode.Conflict, first.StatusCode, await first.Content.ReadAsStringAsync());
        }

        Assert.IsNotNull(first);
        Assert.AreEqual(HttpStatusCode.Created, first!.StatusCode, await first.Content.ReadAsStringAsync());
        var id = (await Json(first)).GetProperty("bookingId").GetInt64();

        var retry = await shopper.PostAsJsonAsync("api/studio/appointment-bookings", body!);
        Assert.AreEqual(HttpStatusCode.OK, retry.StatusCode, await retry.Content.ReadAsStringAsync());
        Assert.AreEqual(id, (await Json(retry)).GetProperty("bookingId").GetInt64());

        var cancel = await shopper.DeleteAsync($"api/studio/appointment-bookings/{id}");
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode, await cancel.Content.ReadAsStringAsync());
        var cancelAgain = await shopper.DeleteAsync($"api/studio/appointment-bookings/{id}");
        Assert.AreEqual(HttpStatusCode.OK, cancelAgain.StatusCode);
    }

    [TestMethod]
    public async Task TrainersAreForAdministratorsOnly()
    {
        var denied = await Shopper("demouser@microsoft.com").GetAsync("api/studio/admin/trainers");
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);

        var ok = await Admin().GetAsync("api/studio/admin/trainers");
        Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode);
        var trainers = (await Json(ok)).EnumerateArray().ToList();
        Assert.IsTrue(trainers.Count > 0);
        Assert.IsTrue(trainers.All(t => t.GetProperty("id").GetInt64() > 0));
        Assert.IsTrue(trainers.Any(t => t.GetProperty("mobilePhone").ValueKind == JsonValueKind.String));
    }

    [TestMethod]
    public async Task AdministratorPlacesAShopperInAClass()
    {
        var email = NewEmail();
        var classId = await FirstClassId();
        var unknown = await Admin().PostAsJsonAsync("api/studio/admin/class-bookings", new { shopper = email, classId });
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);

        var denied = await Shopper(email).PostAsJsonAsync("api/studio/admin/class-bookings", new { shopper = email, classId });
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);

        // Becomes a client with a first booking of their own, then the desk places them in another class.
        var own = await Shopper(email).PostAsJsonAsync("api/studio/class-bookings", new
        {
            classId,
            homeLocationId = 1,
            streetAddress = "1 Main St",
            city = "Austin",
            state = "TX",
            postalCode = "78701",
            dateOfBirth = "1990-05-17",
            mobilePhone = "5125550123",
            referredBy = "Google"
        });
        Assert.AreEqual(HttpStatusCode.Created, own.StatusCode, await own.Content.ReadAsStringAsync());
        var ownId = (await Json(own)).GetProperty("bookingId").GetInt64();

        var again = await Admin().PostAsJsonAsync("api/studio/admin/class-bookings", new { shopper = email, classId });
        Assert.AreEqual(HttpStatusCode.OK, again.StatusCode);
        Assert.AreEqual(ownId, (await Json(again)).GetProperty("bookingId").GetInt64());

        await Shopper(email).DeleteAsync($"api/studio/class-bookings/{ownId}");
    }

    private static async Task<long> FirstClassId()
    {
        var response = await Shopper("demouser@microsoft.com").GetAsync("api/studio/classes?from=" +
            DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd") + "&to=" + DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"));
        var classes = (await Json(response)).EnumerateArray().ToList();
        return classes.First().GetProperty("classId").GetInt64();
    }
}
