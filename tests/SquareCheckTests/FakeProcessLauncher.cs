using SquareCheck;

namespace SquareCheckTests;

internal sealed class FakeProcessLauncher : IProcessLauncher
{
    private readonly List<string> _openedUrls = new();
    public IReadOnlyList<string> OpenedUrls => _openedUrls;

    public void OpenUrl(string url) => _openedUrls.Add(url);
}
