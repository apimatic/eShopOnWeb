using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Payments;

internal static class EndpointResults
{
    public static IResult From(ServiceResult result)
    {
        return result.Body is null
            ? Results.StatusCode(result.StatusCode)
            : Results.Json(result.Body, statusCode: result.StatusCode);
    }
}
