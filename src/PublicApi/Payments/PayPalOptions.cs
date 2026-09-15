using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";

    [Required]
    public string ClientId { get; set; } = string.Empty;
    [Required]
    public string ClientSecret { get; set; } = string.Empty;
    [Required]
    public string Environment { get; set; } = string.Empty;
    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public TimeSpan TotalCallTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed class PaymentApiException : Exception
{
    public PaymentApiException(int statusCode, string code, string safeMessage,
        string? providerDebugId = null, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        ProviderDebugId = providerDebugId;
    }

    public int StatusCode { get; }
    public string Code { get; }
    public string? ProviderDebugId { get; }
}

public sealed class PayPalCallContext
{
    private readonly System.Threading.AsyncLocal<System.Net.HttpStatusCode?> _lastStatus = new();
    public System.Net.HttpStatusCode? LastStatus { get => _lastStatus.Value; set => _lastStatus.Value = value; }
}
