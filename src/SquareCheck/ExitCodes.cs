namespace Microsoft.eShopWeb.SquareCheck;

public static class ExitCodes
{
    public const int Success = 0;

    /// <summary>Square refused or failed a request.</summary>
    public const int SquareFailed = 1;

    /// <summary>The operator declined access, or did not finish signing in in time.</summary>
    public const int SignInNotCompleted = 2;

    /// <summary>The tool is not set up to run: a setting is missing or invalid, or the redirect address cannot be listened on.</summary>
    public const int SetupProblem = 3;

    /// <summary>Something the tool did not anticipate went wrong.</summary>
    public const int Unexpected = 70;

    /// <summary>The operator pressed Ctrl+C.</summary>
    public const int Cancelled = 130;
}
