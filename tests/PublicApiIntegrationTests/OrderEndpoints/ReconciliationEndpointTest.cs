using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class ReconciliationEndpointTest
{
    [TestMethod]
    public async Task ReturnsForbiddenForNonAdminUser()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/reconciliation?from=2024-01-01T00:00:00Z&to=2024-01-02T00:00:00Z");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
