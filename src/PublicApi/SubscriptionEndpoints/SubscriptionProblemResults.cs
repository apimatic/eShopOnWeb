using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps translated Maxio failures to HTTP results for subscription endpoints. The mapping is
/// one shared ladder: provider 4xx stays a client 4xx, provider/transport unavailability is
/// 502/504, our own configuration problems are 500 — never leaked to the wire.
/// </summary>
internal static class SubscriptionProblemResults
{
    public static IResult From(Microsoft.eShopWeb.PublicApi.Subscriptions.MaxioBillingException ex)
    {
        var detail = new StringBuilder(ex.Message);
        if (ex.ProviderErrors.Count > 0)
        {
            detail.Append(" ").Append(string.Join(" ", ex.ProviderErrors));
        }

        var status = ex.ResponseStatusCode;
        return Results.Problem(
            detail: detail.ToString(),
            statusCode: status,
            title: TitleFor(status));
    }

    private static string TitleFor(int statusCode) => statusCode switch
    {
        (int)HttpStatusCode.NotFound => "Not Found",
        (int)HttpStatusCode.Conflict => "Conflict",
        (int)HttpStatusCode.BadGateway => "Billing Provider Unavailable",
        (int)HttpStatusCode.GatewayTimeout => "Billing Provider Timed Out",
        >= 400 and < 500 => "Request Rejected",
        _ => "Internal Server Error",
    };
}
