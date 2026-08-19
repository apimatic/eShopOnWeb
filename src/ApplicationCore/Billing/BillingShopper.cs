namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The authenticated eShopOnWeb user we enroll in Maxio.
/// </summary>
public sealed class BillingShopper
{
    public BillingShopper(string userId, string email, string? userName)
    {
        UserId = userId;
        Email = email;
        UserName = userName;
    }

    public string UserId { get; }
    public string Email { get; }
    public string? UserName { get; }
}
