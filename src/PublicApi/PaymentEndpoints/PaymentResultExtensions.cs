using System.Collections.Generic;
using System.Linq;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Maps an unsuccessful <see cref="Ardalis.Result"/> onto an HTTP problem response.</summary>
public static class PaymentResultExtensions
{
    public static Microsoft.AspNetCore.Http.IResult ToProblem(this Ardalis.Result.IResult result)
    {
        var messages = (result.Errors ?? Enumerable.Empty<string>()).ToList();

        return result.Status switch
        {
            ResultStatus.NotFound => Results.NotFound(new ProblemPayload(messages.Count > 0 ? messages : new List<string> { "The requested resource was not found." })),
            ResultStatus.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
            ResultStatus.Unauthorized => Results.Unauthorized(),
            ResultStatus.Invalid => Results.BadRequest(new ProblemPayload(result.ValidationErrors.Select(v => v.ErrorMessage).ToList())),
            _ => Results.BadRequest(new ProblemPayload(messages.Count > 0 ? messages : new List<string> { "The request could not be processed." }))
        };
    }

    public sealed record ProblemPayload(IReadOnlyList<string> Errors);
}
