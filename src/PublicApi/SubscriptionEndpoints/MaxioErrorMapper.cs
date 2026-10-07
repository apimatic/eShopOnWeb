using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class MaxioErrorMapper
{
    public static IResult Map(MaxioApiException exception)
    {
        var detail = exception.Errors.Count > 0
            ? string.Join("; ", exception.Errors)
            : exception.Message;

        return exception.StatusCode switch
        {
            int code when code >= 400 && code < 500 => Results.Problem(
                title: "The subscription request was rejected by Maxio.",
                detail: detail,
                statusCode: exception.StatusCode),
            _ => Results.Problem(
                title: "The billing system (Maxio) is currently unavailable.",
                detail: detail,
                statusCode: 502)
        };
    }
}
