using System.Linq;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class EndpointResultMapper
{
    public static Microsoft.AspNetCore.Http.IResult Map(Result result)
    {
        return result.Status switch
        {
            ResultStatus.NotFound => Results.NotFound(),
            ResultStatus.Invalid => Results.BadRequest(new { errors = result.ValidationErrors.Select(e => e.ErrorMessage) }),
            ResultStatus.Unauthorized => Results.Unauthorized(),
            ResultStatus.Forbidden => Results.Forbid(),
            _ => Results.Problem(string.Join("; ", result.Errors))
        };
    }

    public static Microsoft.AspNetCore.Http.IResult Map<T>(Result<T> result)
    {
        return result.Status switch
        {
            ResultStatus.NotFound => Results.NotFound(),
            ResultStatus.Invalid => Results.BadRequest(new { errors = result.ValidationErrors.Select(e => e.ErrorMessage) }),
            ResultStatus.Unauthorized => Results.Unauthorized(),
            ResultStatus.Forbidden => Results.Forbid(),
            _ => Results.Problem(string.Join("; ", result.Errors))
        };
    }
}
