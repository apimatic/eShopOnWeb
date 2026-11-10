using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.StudioEndpoints;

[TestClass]
public class StudioEndpointsAuthorizationTest
{
    [DataTestMethod]
    [DataRow("api/studio/classes")]
    [DataRow("api/studio/slots?sessionTypeId=1&from=2030-01-01&to=2030-01-02")]
    [DataRow("api/studio/admin/trainers")]
    public async Task ReturnsUnauthorizedWithoutToken(string url)
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync(url);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsForbiddenForTrainersGivenNormalUserToken()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/studio/admin/trainers");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsForbiddenForAdminBookingGivenNormalUserToken()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/studio/admin/class-bookings",
            new StringContent("{\"shopper\":\"demouser@microsoft.com\",\"classId\":1}", System.Text.Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
