using System;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class FinixDuplicateAuthorizationException : PaymentException
{
    public FinixDuplicateAuthorizationException(string authorizationId, string message) : base(message)
    {
        AuthorizationId = authorizationId;
    }

    public string AuthorizationId { get; }
}
