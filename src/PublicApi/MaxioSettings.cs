using System;
using System.Text;

namespace Microsoft.eShopWeb.PublicApi;

public class MaxioSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
            return BaseUrl.TrimEnd('/');

        return $"https://{Subdomain}.chargify.com/api/v2";
    }

    public string GetAuthorizationHeader()
    {
        string credentials = $"{ApiKey}:x";
        byte[] credentialsBytes = Encoding.UTF8.GetBytes(credentials);
        string base64Credentials = Convert.ToBase64String(credentialsBytes);
        return $"Basic {base64Credentials}";
    }
}
