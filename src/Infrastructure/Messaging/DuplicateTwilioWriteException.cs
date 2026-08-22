using System;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal sealed class DuplicateTwilioWriteException : Exception
{
    public DuplicateTwilioWriteException()
        : base("A duplicate create was blocked before it reached the provider.")
    {
    }
}
