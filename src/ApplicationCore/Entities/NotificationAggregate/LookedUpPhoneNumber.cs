namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class LookedUpPhoneNumber
{
    public LookedUpPhoneNumber(bool valid, string? phoneNumber, string? nationalFormat, string[] validationErrors)
    {
        Valid = valid;
        PhoneNumber = phoneNumber;
        NationalFormat = nationalFormat;
        ValidationErrors = validationErrors;
    }

    public bool Valid { get; }
    public string? PhoneNumber { get; }
    public string? NationalFormat { get; }
    public string[] ValidationErrors { get; }
}
