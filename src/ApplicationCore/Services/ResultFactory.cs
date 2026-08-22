using System.Collections.Generic;
using Ardalis.Result;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

internal static class ResultFactory
{
    public static Result<T> Invalid<T>(string identifier, string errorMessage)
    {
        return Result<T>.Invalid(new List<ValidationError>
        {
            new() { Identifier = identifier, ErrorMessage = errorMessage }
        });
    }

    public static Result Invalid(string identifier, string errorMessage)
    {
        return Result.Invalid(new List<ValidationError>
        {
            new() { Identifier = identifier, ErrorMessage = errorMessage }
        });
    }
}
