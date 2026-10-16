using System.Text;

namespace Foxify.Cli;

/// <summary>
/// <c>foxify guide</c>: prints the guide for coding agents (docs/llm-guide.md), embedded in the
/// binary so it always describes the installed version.
/// </summary>
internal static class GuideCommand
{
    private const string ResourceName = "llm-guide.md";

    public static string Text
    {
        get
        {
            using var stream = typeof(GuideCommand).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }

    public static int Run()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
        }

        Console.Out.Write(Text);
        return 0;
    }
}
