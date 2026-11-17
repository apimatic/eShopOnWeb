using System;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentApiException : Exception
{
    public PaymentApiException(int statusCode, string message, string? code = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string? Code { get; }
}

public interface ICurrentUser
{
    string UserId { get; }
}

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string UserId
    {
        get
        {
            var name = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new PaymentApiException(401, "The authenticated shopper identity is missing.", "UNAUTHENTICATED");
            }

            return name;
        }
    }
}
