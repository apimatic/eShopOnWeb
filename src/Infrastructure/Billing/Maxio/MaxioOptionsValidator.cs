using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Fails fast at start-up when the Maxio section is missing or nonsensical, rather than letting the
/// first shopper discover it.
/// </summary>
public class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();

        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(options, context, results, validateAllProperties: true))
        {
            foreach (var result in results)
            {
                failures.Add(result.ErrorMessage ?? "Invalid Maxio configuration.");
            }
        }

        if (!MaxioEnvironments.IsSupported(options.Environment))
        {
            failures.Add($"Maxio:Environment '{options.Environment}' is not supported. Use '{MaxioEnvironments.Us}' or '{MaxioEnvironments.Eu}'.");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl)
            && !Uri.TryCreate(options.BaseUrl!.Trim(), UriKind.Absolute, out _))
        {
            failures.Add("Maxio:BaseUrl must be an absolute URL when supplied.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
