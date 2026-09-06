using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Surfaces <see cref="MaxioOptions.Validate"/> failures as an options validation result, so a
/// misconfigured host reports which setting is wrong instead of throwing a bare
/// <see cref="ValidationException"/> from the middle of dependency injection.
/// </summary>
internal sealed class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        try
        {
            options.Validate();
            return ValidateOptionsResult.Success;
        }
        catch (ValidationException ex)
        {
            return ValidateOptionsResult.Fail(ex.Message);
        }
    }
}
