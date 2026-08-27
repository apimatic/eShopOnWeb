namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Masks a phone number so reports never expose more than its last digits.</summary>
public static class PhoneNumberMask
{
    public static string Mask(string? phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber))
        {
            return string.Empty;
        }

        var visible = phoneNumber.Length <= 4 ? phoneNumber : phoneNumber[^4..];
        return $"***{visible}";
    }
}
