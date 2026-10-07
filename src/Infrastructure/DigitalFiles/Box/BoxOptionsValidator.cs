using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.DigitalFiles.Box;

/// <summary>
/// Validates <see cref="BoxOptions"/> at startup. Messages name the configuration key and never echo a value.
/// </summary>
public sealed class BoxOptionsValidator : IValidateOptions<BoxOptions>
{
    public ValidateOptionsResult Validate(string? name, BoxOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.AccessToken))
        {
            failures.Add("Box:AccessToken is not configured. Set it with 'dotnet user-secrets set Box:AccessToken <token>' " +
                "or the BOX_ACCESS_TOKEN environment variable before starting the app.");
        }

        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true))
        {
            failures.AddRange(results.Select(r =>
                $"Box:{string.Join(",", r.MemberNames)} is invalid: {r.ErrorMessage}"));
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
