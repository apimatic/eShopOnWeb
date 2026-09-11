using System.Collections.Generic;
using Ardalis.Result;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Small helpers over Ardalis.Result to keep service code terse. This version of Ardalis.Result
/// has no Conflict status, so business-rule violations are surfaced as Invalid (HTTP 400) with a
/// clear message.
/// </summary>
public static class ServiceResults
{
    public static List<ValidationError> Validation(string identifier, string message) =>
        new() { new ValidationError { Identifier = identifier, ErrorMessage = message } };

    public static List<ValidationError> Conflict(string message) =>
        new() { new ValidationError { Identifier = "conflict", ErrorMessage = message } };
}
