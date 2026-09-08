using System;

namespace Microsoft.eShopWeb.ApplicationCore.Integration.Maxio;

/// <summary>The Maxio integration is misconfigured or could not complete an otherwise valid request.</summary>
public class MaxioIntegrationException : Exception
{
    public MaxioIntegrationException(string message) : base(message)
    {
    }

    public MaxioIntegrationException(string message, Exception inner) : base(message, inner)
    {
    }
}
