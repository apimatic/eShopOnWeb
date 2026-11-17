using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class PaymentResults
{
    public static ActionResult Problem(PaymentDomainException exception)
    {
        var details = new ProblemDetails
        {
            Status = exception.StatusCode,
            Title = "Payment request failed",
            Detail = exception.Message
        };
        details.Extensions["code"] = exception.Code;
        details.Extensions["logRef"] = exception.LogRef;

        return new ObjectResult(details) { StatusCode = exception.StatusCode };
    }

    public static IResult ProblemResult(PaymentDomainException exception)
    {
        return Results.Json(new
        {
            status = exception.StatusCode,
            title = "Payment request failed",
            detail = exception.Message,
            code = exception.Code,
            logRef = exception.LogRef
        }, statusCode: exception.StatusCode);
    }
}
