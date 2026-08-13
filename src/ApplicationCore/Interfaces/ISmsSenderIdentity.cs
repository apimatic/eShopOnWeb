namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Exposes this application's own configured sending number to the domain, without leaking the
/// provider's configuration types into the application layer. Reconciliation counts only messages
/// sent from this number.
/// </summary>
public interface ISmsSenderIdentity
{
    /// <summary>The application's configured sending number (the provider's <c>FromNumber</c>), in E.164.</summary>
    string FromNumber { get; }
}
