using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

/// <summary>
/// Maps a messaging-provider failure to a caller-facing HTTP result, applying one consistent policy: our own
/// credential/quota problems and transport failures become 5xx (the caller did nothing wrong and cannot fix
/// them), while a genuine caller-caused 4xx is passed back so the caller can act on it. The message is always
/// the exception's caller-safe text — never a raw provider body, phone number or secret.
/// </summary>
internal static class ProviderErrorResults
{
    public static IResult From(SmsProviderException ex)
    {
        var (status, message) = Map(ex);
        return Results.Json(new { message }, statusCode: status);
    }

    private static (int Status, string Message) Map(SmsProviderException ex)
    {
        int? code = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : null;
        return code switch
        {
            401 or 403 => (502, "The messaging provider is unavailable."),
            429 => (503, "The messaging provider is temporarily unavailable."),
            >= 400 and < 500 => (code!.Value, ex.Message),
            _ => (502, "The messaging provider is unavailable.")
        };
    }
}
