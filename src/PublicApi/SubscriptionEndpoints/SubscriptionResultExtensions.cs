using System;
using System.Linq;
using Ardalis.Result;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps Ardalis.Result statuses produced by the subscription services onto
/// conventional HTTP responses.
/// </summary>
public static class SubscriptionResultExtensions
{
    public static ActionResult ToActionResult<T>(this Result<T> result, Func<T, object>? project = null) where T : class
    {
        switch (result.Status)
        {
            case ResultStatus.Ok:
                var value = result.Value;
                return new OkObjectResult(project != null ? project(value) : value);
            case ResultStatus.NotFound:
                return new NotFoundObjectResult(new { errors = result.Errors });
            case ResultStatus.Invalid:
                return new BadRequestObjectResult(new
                {
                    errors = result.ValidationErrors.Select(e => e.ErrorMessage).ToList()
                });
            case ResultStatus.Unauthorized:
                return new UnauthorizedResult();
            case ResultStatus.Forbidden:
                return new StatusCodeResult(403);
            case ResultStatus.Error:
            default:
                return new ObjectResult(new { errors = result.Errors })
                {
                    StatusCode = 500
                };
        }
    }
}