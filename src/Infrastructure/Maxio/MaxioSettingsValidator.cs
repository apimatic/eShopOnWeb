using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Checks the Maxio section the first time it is read, so a misconfigured host reports exactly what
/// is missing instead of failing somewhere inside an HTTP call.
/// </summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new List<string>();

        // Run the [Required]/[Range] attributes on MaxioSettings, then the cross-field rules.
        var annotationResults = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), annotationResults, validateAllProperties: true);
        failures.AddRange(annotationResults
            .Select(result => result.ErrorMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message))!);

        if (string.IsNullOrWhiteSpace(options.BaseUrl) && string.IsNullOrWhiteSpace(options.Subdomain))
        {
            failures.Add("Maxio:Subdomain is required unless Maxio:BaseUrl is set.");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl)
            && !Uri.TryCreate(options.BaseUrl.Trim(), UriKind.Absolute, out _))
        {
            failures.Add("Maxio:BaseUrl must be an absolute URL, for example https://your-site.chargify.com/.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
