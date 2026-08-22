using System.Collections.Generic;
using Ardalis.Result;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

internal static class ResultHelpers
{
    public static Result<T> Invalid<T>(string identifier, string message) =>
        Result<T>.Invalid(new List<ValidationError>
        {
            new ValidationError { Identifier = identifier, ErrorMessage = message }
        });
}
