using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface ICurrentUser
{
    /// <summary>The authenticated caller's identity, taken from the JWT.</summary>
    string BuyerId { get; }
}

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string BuyerId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var name = user?.Identity?.Name
                ?? user?.FindFirst(ClaimTypes.Name)?.Value
                ?? user?.FindFirst("name")?.Value;

            if (string.IsNullOrEmpty(name))
            {
                throw new InvalidOperationException("The caller's token does not contain a name claim.");
            }

            return name;
        }
    }
}
