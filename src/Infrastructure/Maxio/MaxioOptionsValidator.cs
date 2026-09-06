using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Fails the first resolution of <see cref="MaxioOptions"/> with an actionable message when the
/// <c>Maxio</c> section is missing or incomplete, rather than letting a null base address surface as
/// an opaque error deep inside an HTTP call.
/// </summary>
public class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var errors = options.Validate();

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
