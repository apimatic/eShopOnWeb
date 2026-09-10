using System.Collections.Generic;

namespace PayPalServerSdk.Core.Authentication.OAuth2;

internal delegate IReadOnlyList<T> CredentialParamsFactory<out T>(string clientId, string? clientSecret);
