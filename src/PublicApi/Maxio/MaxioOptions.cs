using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }

    public string? Subdomain { get; set; }

    public string? ProductFamilyHandle { get; set; }

    public string? BaseUrl { get; set; }

    public string ResolvedBaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(BaseUrl))
            {
                return BaseUrl.TrimEnd('/');
            }

            if (string.IsNullOrWhiteSpace(Subdomain))
            {
                throw new OptionsValidationException(SectionName, typeof(MaxioOptions),
                    new[] { "Maxio:Subdomain must be configured when Maxio:BaseUrl is not set." });
            }

            return $"https://{Subdomain}.chargify.com";
        }
    }

    public void Validate()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add($"{SectionName}:ApiKey");
        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain)) missing.Add($"{SectionName}:Subdomain");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle)) missing.Add($"{SectionName}:ProductFamilyHandle");
        if (missing.Count > 0)
        {
            throw new OptionsValidationException(SectionName, typeof(MaxioOptions),
                missing.ConvertAll(key => $"Configuration value '{key}' is required."));
        }
    }
}
