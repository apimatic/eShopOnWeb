using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.StudioEndpoints;

[TestClass]
public class StudioEndpointsAuthTest
{
    [TestMethod]
    public async Task ClassesRequireAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/studio/classes");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task TrainersAreForbiddenToShoppers()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/studio/admin/trainers");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task AdminBookingIsForbiddenToShoppers()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsJsonAsync("api/studio/admin/class-bookings", new { shopper = "demouser@microsoft.com", classId = 1 });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task InvalidDateIsABadRequest()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/studio/classes?from=not-a-date");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
