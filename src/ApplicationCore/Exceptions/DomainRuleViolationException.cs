using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The request conflicts with the current state of the domain (e.g. dispatching a
/// cancelled order, resending a message whose content has been disposed of).
/// </summary>
public class DomainRuleViolationException : Exception
{
    public DomainRuleViolationException(string message) : base(message)
    {
    }
}
