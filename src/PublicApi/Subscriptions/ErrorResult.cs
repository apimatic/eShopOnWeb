using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public static class ErrorResult
{
    public static IResult From(MaxioApiException ex) =>
        Results.Json(new { errors = ex.Errors }, statusCode: ex.StatusCode >= 500 ? 502 : ex.StatusCode);

    public static IResult From(SubscriptionPlanNotFoundException ex) =>
        Results.Json(new { errors = new[] { ex.Message } }, statusCode: 404);

    public static IResult BadRequest(string message) =>
        Results.Json(new { errors = new[] { message } }, statusCode: 400);
}
