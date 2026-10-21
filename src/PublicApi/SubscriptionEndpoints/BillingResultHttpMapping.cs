using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The one place billing failures become HTTP results, so every subscription endpoint answers the
/// same failure the same way.
/// </summary>
internal static class BillingResultHttpMapping
{
    public static IResult ToFailureResult<T>(this BillingResult<T> result, Guid correlationId)
    {
        var (status, retryAfterSeconds) = result.Status switch
        {
            BillingResultStatus.Invalid => (StatusCodes.Status400BadRequest, (int?)null),
            BillingResultStatus.Conflict => (StatusCodes.Status409Conflict, null),
            BillingResultStatus.InProgress => (StatusCodes.Status409Conflict, 2),
            BillingResultStatus.Rejected => (StatusCodes.Status422UnprocessableEntity, null),
            BillingResultStatus.Pending => (StatusCodes.Status202Accepted, 5),
            BillingResultStatus.Unavailable => (StatusCodes.Status503ServiceUnavailable, 5),
            _ => (StatusCodes.Status500InternalServerError, null)
        };

        var body = new SubscriptionErrorResponse(correlationId)
        {
            StatusCode = status,
            Message = result.Message ?? string.Empty,
            Errors = result.Errors.ToList()
        };

        IResult inner = Results.Json(body, statusCode: status);
        return retryAfterSeconds is null ? inner : new RetryAfterResult(inner, retryAfterSeconds.Value);
    }

    public static IResult UnknownCaller(Guid correlationId) =>
        Results.Json(new SubscriptionErrorResponse(correlationId)
        {
            StatusCode = StatusCodes.Status401Unauthorized,
            Message = "The authenticated user was not found."
        }, statusCode: StatusCodes.Status401Unauthorized);

    private sealed class RetryAfterResult : IResult
    {
        private readonly IResult _inner;
        private readonly int _seconds;

        public RetryAfterResult(IResult inner, int seconds)
        {
            _inner = inner;
            _seconds = seconds;
        }

        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.RetryAfter = _seconds.ToString(CultureInfo.InvariantCulture);
            return _inner.ExecuteAsync(httpContext);
        }
    }
}
