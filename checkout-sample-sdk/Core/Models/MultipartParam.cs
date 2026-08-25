namespace PayPalServerSdk.Core.Models;

internal readonly record struct MultipartParam(
    string Key,
    object? Value,
    string? ContentType = null);
