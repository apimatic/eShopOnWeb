namespace Microsoft.eShopWeb.SquareCheck.Configuration;

/// <summary>
/// The raw <c>Square:</c> configuration section, exactly as bound. Validate it with
/// <see cref="SquareSettingsValidator"/> before use.
/// </summary>
public sealed class SquareSettings
{
    public const string SectionName = "Square";

    public string? Environment { get; set; }
    public string? ApplicationId { get; set; }
    public string? ApplicationSecret { get; set; }
    public string? RedirectUri { get; set; }

    // Never let a secret reach a log line or an exception message by accident.
    public override string ToString() => $"{nameof(SquareSettings)} {{ values redacted }}";
}
