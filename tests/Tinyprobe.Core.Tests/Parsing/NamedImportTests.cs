using System.Text.Json;
using Tinyprobe.Core.Execution;
using Tinyprobe.Core.Parsing;
using Tinyprobe.Core.Protocol;
using Tinyprobe.Core.Tests.Fakes;

namespace Tinyprobe.Core.Tests.Parsing;

/// <summary>Imports with a name, <c>- billing: ../api/billing.yaml</c> (docs/specs/0013-namespaced-imports.md).</summary>
public sealed class NamedImportTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("tinyprobe-named-imports-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Two services with the same endpoint names, each with its own base URL and auth.</summary>
    private string Workflow(string imports, string steps)
    {
        Write("api/users.yaml", """
            base_url: https://users.test
            auth:
              key: { X-Users-Key: u-1 }
            endpoints:
              login: { method: POST, path: /login, auth: key }
              health: { path: /health }
            """);
        Write("api/billing.yaml", """
            base_url: https://billing.test
            auth:
              key: { X-Billing-Key: b-1 }
            endpoints:
              login: { method: POST, path: /session, auth: key }
              invoice: { path: "/invoices/${id}", auth: key }
            """);
        return Write("workflows/test.yaml", $"name: Test\nimport:\n{imports}\nsteps:\n{steps}");
    }

    private WorkflowLoadException LoadFails(string path) => Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.Load(path));

    private string Relative(string error) => error
        .Replace(Path.GetRelativePath(Environment.CurrentDirectory, _directory) + Path.DirectorySeparatorChar, "")
        .Replace(_directory + Path.DirectorySeparatorChar, "");

    [Fact]
    public async Task ANamedImport_PrefixesItsEndpoints_WhichKeepTheirFilesBaseUrlAndAuth()
    {
        var path = Workflow("  - ../api/users.yaml\n  - billing: ../api/billing.yaml", """
              - call: login
              - call: billing.login
              - call: billing.invoice
                with: { id: 7 }
            """);
        var workflow = WorkflowLoader.Load(path);
        var server = new FakeServer();

        var report = await new WorkflowEngine(new HttpClient(server)).ExecuteAsync(workflow);

        Assert.All(report.StepReports, s => Assert.True(s.IsSuccess, s.FailureReason));
        Assert.Equal(["https://users.test/login", "https://billing.test/session", "https://billing.test/invoices/7"], server.Requests.Select(r => r.Url));
        Assert.Equal("u-1", server.Requests[0].Headers["X-Users-Key"]);
        Assert.Equal("b-1", server.Requests[1].Headers["X-Billing-Key"]);
        Assert.False(server.Requests[1].Headers.ContainsKey("X-Users-Key"));
        Assert.Equal(["login", "billing.login", "billing.invoice id=7"], report.StepReports.Select(s => s.StepName));
        Assert.Equal(["billing.invoice", "billing.login", "health", "login"], workflow.Endpoints.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheSameFile_UnderTwoNames_GivesTwoSetsOfNames_AndUnderOneName_IsLoadedOnce()
    {
        var workflow = WorkflowLoader.Load(Workflow(
            "  - a: ../api/billing.yaml\n  - b: ../api/billing.yaml\n  - a: ../api/billing.yaml",
            "  - call: a.login\n  - call: b.login\n"));

        Assert.Equal(["a.invoice", "a.login", "b.invoice", "b.login"], workflow.Endpoints.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(2, workflow.Imports.Count);
    }

    [Fact]
    public void ADuplicateName_SuggestsANamedImport()
    {
        var errors = LoadFails(Workflow("  - ../api/users.yaml\n  - ../api/billing.yaml", "  - call: login\n")).Errors.Select(Relative);

        Assert.Contains(errors, e => e.StartsWith("api/billing.yaml:5:3: endpoint 'login' is already defined at api/users.yaml:5:3")
            && e.EndsWith("(give one import a name: `- billing: <path>`, then `call: billing.login`)"));
    }

    [Theory]
    [InlineData("  - Billing-API: ../api/billing.yaml", "workflows/test.yaml:3:5: import name 'Billing-API' must be lowercase letters, digits and _, starting with a letter (billing)")]
    [InlineData("  - { a: ../api/billing.yaml, b: ../api/users.yaml }", "workflows/test.yaml:3:5: an import with a name is one `name: path` pair")]
    [InlineData("  - billing: ../api/billing.yaml\n  - billing: ../api/users.yaml", "workflows/test.yaml:4:5: import name 'billing' is already used for api/billing.yaml")]
    [InlineData("  - billing: ../api/billng.yaml", "workflows/test.yaml:3:14: imported file not found: ../api/billng.yaml")]
    public void InvalidNamedImports_AreReportedWithTheirLocation(string imports, string expected)
    {
        var errors = LoadFails(Workflow(imports, "  - call: health\n")).Errors.Select(Relative);

        Assert.Contains(expected, errors);
    }

    [Fact]
    public void AnUnknownPrefixedName_GetsASuggestion()
    {
        var errors = LoadFails(Workflow("  - billing: ../api/billing.yaml", "  - call: billing.invoce\n")).Errors.Select(Relative);

        Assert.Contains(errors, e => e.EndsWith("unknown endpoint 'billing.invoce' (did you mean 'billing.invoice'?)"));
    }

    [Fact]
    public void ASequence_NamesItsImportsTheSameWay()
    {
        Workflow("", "");
        Write("sequences/pay.yaml", "import:\n  - billing: ../api/billing.yaml\nsequence:\n  - call: billing.login\n");
        var path = Write("workflows/seq.yaml", "import: [../api/users.yaml]\nsteps:\n  - use: ../sequences/pay.yaml\n  - call: login\n");

        var workflow = WorkflowLoader.Load(path);

        Assert.Contains("billing.login", workflow.Endpoints.Keys);
        Assert.Contains("login", workflow.Endpoints.Keys);
    }

    [Fact]
    public void Inspection_HasTheImports_AndEachEndpointsImportName()
    {
        var json = JsonDocument.Parse(ProtocolJson.Inspection("1.2.3",
            WorkflowLoader.Load(Workflow("  - ../api/users.yaml\n  - billing: ../api/billing.yaml", "  - call: login\n")), indented: false)).RootElement;

        var imports = json.GetProperty("imports").EnumerateArray().ToList();
        Assert.Equal(JsonValueKind.Null, imports[0].GetProperty("name").ValueKind);
        Assert.Equal("billing", imports[1].GetProperty("name").GetString());
        Assert.EndsWith("billing.yaml", imports[1].GetProperty("file").GetString());

        var endpoints = json.GetProperty("endpoints").EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString()!);
        Assert.Equal("billing", endpoints["billing.invoice"].GetProperty("import_name").GetString());
        Assert.Equal(JsonValueKind.Null, endpoints["login"].GetProperty("import_name").ValueKind);
        Assert.Equal("""["id"]""", endpoints["billing.invoice"].GetProperty("arguments").GetRawText());
    }
}
