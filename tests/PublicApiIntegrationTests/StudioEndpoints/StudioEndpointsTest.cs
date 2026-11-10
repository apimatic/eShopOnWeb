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
/// Runs against the real Mindbody sandbox: its credentials are read from the "Mindbody:" settings
/// (user-secrets or Mindbody__* environment variables). The tests are inconclusive when they are missing.
/// </summary>
[TestClass]
public class StudioEndpointsTest
{
    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable && json.Contains("not configured"))
        {
            Assert.Inconclusive("The Mindbody sandbox settings are not configured.");
        }

        return json.Length == 0 ? default : JsonDocument.Parse(json).RootElement.Clone();
    }

    [TestMethod]
    public async Task EveryStudioEndpointRequiresASignedInCaller()
    {
        var client = ProgramTest.NewClient;
        foreach (var url in new[] { "api/studio/classes", "api/studio/slots?sessionTypeId=1", "api/studio/admin/trainers" })
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode, url);
        }

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.DeleteAsync("api/studio/class-bookings/1")).StatusCode);
    }

    [TestMethod]
    public async Task StaffEndpointsAreForAdministratorsOnly()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync("api/studio/admin/trainers")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("api/studio/admin/class-bookings", new { shopper = "demouser@microsoft.com", classId = 1 })).StatusCode);
    }

    [TestMethod]
    public async Task ClassesDefaultToTodayAndTheFollowingSevenDaysWithOffsets()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/studio/classes");
        var body = await ReadAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var classes = body.GetProperty("classes").EnumerateArray().ToList();
        Assert.IsTrue(classes.Count > 0);
        foreach (var c in classes)
        {
            var startText = c.GetProperty("startTime").GetString()!;
            var start = DateTimeOffset.Parse(startText);
            var end = DateTimeOffset.Parse(c.GetProperty("endTime").GetString()!);
            StringAssert.Matches(startText, new System.Text.RegularExpressions.Regex(@"(Z|[+-]\d\d:\d\d)$"));
            Assert.IsTrue(end > start);
            Assert.IsTrue(start < DateTimeOffset.UtcNow.AddDays(9));
        }
    }

    [TestMethod]
    public async Task ClassesRejectAReversedRange()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/studio/classes?from=2026-11-14&to=2026-11-12");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SlotsListEveryDayBetweenTheTwoDates()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var from = DateTime.UtcNow.Date.AddDays(10);

        var response = await client.GetAsync($"api/studio/slots?sessionTypeId=23&from={from:yyyy-MM-dd}&to={from.AddDays(2):yyyy-MM-dd}");
        var body = await ReadAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var slots = body.GetProperty("slots").EnumerateArray().Select(s => DateTimeOffset.Parse(s.GetProperty("startTime").GetString()!)).ToList();
        Assert.AreEqual(3, slots.Select(s => s.Date).Distinct().Count());
        Assert.IsTrue(slots.All(s => s.Offset != TimeSpan.Zero));
    }

    [TestMethod]
    public async Task SlotsForAnUnknownSessionTypeAreNotFound()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/studio/slots?sessionTypeId=987654&from=2030-01-01&to=2030-01-02");
        await ReadAsync(response);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TrainersListsEveryoneNotJustTheFirstPage()
    {
        var client = Client(ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/studio/admin/trainers");
        var body = await ReadAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var trainers = body.GetProperty("trainers").EnumerateArray().ToList();
        Assert.IsTrue(trainers.Count > 100, $"only {trainers.Count} trainers");
        Assert.AreEqual(trainers.Count, trainers.Select(t => t.GetProperty("id").GetInt64()).Distinct().Count());
    }

    [TestMethod]
    public async Task BookingAClassTwiceGivesTheSameBookingAndCancellingTwiceSucceeds()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var from = DateTime.UtcNow.Date.AddDays(3);
        var list = await ReadAsync(await client.GetAsync($"api/studio/classes?from={from:yyyy-MM-dd}&to={from.AddDays(2):yyyy-MM-dd}"));
        var classId = list.GetProperty("classes").EnumerateArray()
            .Where(c => c.GetProperty("capacity").ValueKind == JsonValueKind.Number &&
                        c.GetProperty("booked").GetInt32() < c.GetProperty("capacity").GetInt32() - 1)
            .Select(c => c.GetProperty("id").GetInt32())
            .FirstOrDefault();
        Assert.AreNotEqual(0, classId, "no class with room");

        var details = new
        {
            classId,
            homeLocationId = 1,
            streetAddress = "1 Main St",
            city = "San Luis Obispo",
            state = "CA",
            postalCode = "93401",
            dateOfBirth = "1990-05-06",
            mobilePhone = "8055550123",
            referredBy = "Google",
        };

        var first = await client.PostAsJsonAsync("api/studio/class-bookings", details);
        var firstBody = await ReadAsync(first);
        var again = await client.PostAsJsonAsync("api/studio/class-bookings", new { classId });
        var againBody = await ReadAsync(again);

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, again.StatusCode);
        var bookingId = firstBody.GetProperty("booking").GetProperty("bookingId").GetInt64();
        Assert.AreEqual(bookingId, againBody.GetProperty("booking").GetProperty("bookingId").GetInt64());

        Assert.AreEqual(HttpStatusCode.NoContent, (await client.DeleteAsync($"api/studio/class-bookings/{bookingId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, (await client.DeleteAsync($"api/studio/class-bookings/{bookingId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.DeleteAsync("api/studio/class-bookings/1")).StatusCode);
    }
}
