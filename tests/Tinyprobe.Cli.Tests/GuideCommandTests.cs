namespace Tinyprobe.Cli.Tests;

public class GuideCommandTests
{
    [Fact]
    public void Text_IsTheEmbeddedLlmGuide()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tinyprobe.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var file = File.ReadAllText(Path.Combine(directory.FullName, "docs", "llm-guide.md"));

        Assert.Equal(file.ReplaceLineEndings(), GuideCommand.Text.ReplaceLineEndings());
        Assert.StartsWith("# tinyprobe guide for coding agents", GuideCommand.Text);
    }
}
