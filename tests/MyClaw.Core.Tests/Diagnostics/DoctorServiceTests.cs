using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MyClaw.Core.Configuration;
using MyClaw.Core.Diagnostics;
using Xunit;

namespace MyClaw.Core.Tests.Diagnostics;

/// <summary>
/// <see cref="DoctorService"/> 的单元测试。每个测试使用临时工作区，互不影响。
/// </summary>
public class DoctorServiceTests : IDisposable
{
    private static readonly string[] EssentialDnaFiles =
    {
        "AGENTS.md", "SOUL.md", "IDENTITY.md", "USER.md", "MEMORY.md", "TOOLS.md", "HEARTBEAT.md"
    };

    private readonly string _root;

    public DoctorServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "myclaw-doctor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 清理失败不影响测试结果
        }
    }

    private MyClawConfiguration MakeConfig(string workspace, string? apiKey = null)
    {
        var cfg = MyClawConfiguration.Default();
        cfg.Agent.Workspace = workspace;
        cfg.Provider.ApiKey = apiKey ?? string.Empty;
        return cfg;
    }

    private string CreateHealthyWorkspace()
    {
        var ws = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(ws);
        Directory.CreateDirectory(Path.Combine(ws, "memory"));
        Directory.CreateDirectory(Path.Combine(ws, "skills"));
        foreach (var file in EssentialDnaFiles)
        {
            File.WriteAllText(Path.Combine(ws, file), "# placeholder\n## section\n");
        }
        return ws;
    }

    private static DiagnosticCheck Find(DiagnosticReport report, string name)
        => report.Checks.Single(c => c.Name == name);

    [Fact]
    public async Task RunAsync_MissingWorkspace_ReportsFailure()
    {
        var ws = Path.Combine(_root, "does-not-exist");
        var cfg = MakeConfig(ws, apiKey: "sk-test");
        var service = new DoctorService(cfg, Path.Combine(_root, "config.json"), templatesDir: null);

        var report = await service.RunAsync();

        Assert.True(report.HasFailures);
        Assert.Equal(DiagnosticStatus.Fail, Find(report, "工作区").Status);
    }

    [Fact]
    public async Task RunAsync_HealthyWorkspace_NoFailures()
    {
        var ws = CreateHealthyWorkspace();
        var configPath = Path.Combine(_root, "config.json");
        File.WriteAllText(configPath, "{}");
        var cfg = MakeConfig(ws, apiKey: "sk-test");
        var service = new DoctorService(cfg, configPath, templatesDir: null);

        var report = await service.RunAsync();

        Assert.False(report.HasFailures);
        Assert.Equal(DiagnosticStatus.Pass, Find(report, "工作区").Status);
        Assert.Equal(DiagnosticStatus.Pass, Find(report, "核心 DNA 文件").Status);
        Assert.Equal(DiagnosticStatus.Pass, Find(report, "配置文件").Status);
        Assert.Equal(DiagnosticStatus.Pass, Find(report, "API 密钥").Status);
    }

    [Fact]
    public async Task RunAsync_MissingDnaFiles_WarnsAndListsThem()
    {
        var ws = CreateHealthyWorkspace();
        File.Delete(Path.Combine(ws, "SOUL.md"));
        File.Delete(Path.Combine(ws, "TOOLS.md"));
        var cfg = MakeConfig(ws, apiKey: "sk-test");
        var service = new DoctorService(cfg, Path.Combine(_root, "config.json"), templatesDir: null);

        var report = await service.RunAsync();

        var dna = Find(report, "核心 DNA 文件");
        Assert.Equal(DiagnosticStatus.Warn, dna.Status);
        Assert.Contains("SOUL.md", dna.Message);
        Assert.Contains("TOOLS.md", dna.Message);
    }

    [Fact]
    public async Task RunAsync_NoApiKeyAndNoEnv_Warns()
    {
        var saved = ClearApiKeyEnvVars();
        try
        {
            var ws = CreateHealthyWorkspace();
            var cfg = MakeConfig(ws, apiKey: null);
            var service = new DoctorService(cfg, Path.Combine(_root, "config.json"), templatesDir: null);

            var report = await service.RunAsync();

            Assert.Equal(DiagnosticStatus.Warn, Find(report, "API 密钥").Status);
        }
        finally
        {
            RestoreApiKeyEnvVars(saved);
        }
    }

    [Fact]
    public async Task RunAsync_ValidTemplatesDirWithRibosome_PassesBothChecks()
    {
        var ws = CreateHealthyWorkspace();
        var templatesDir = Path.Combine(_root, "templates");
        Directory.CreateDirectory(templatesDir);
        File.WriteAllText(Path.Combine(templatesDir, "RIBOSOME.json"),
            "{\"instincts\": {\"myclaw_exec\": {\"handler\": \"Exec\", \"description\": \"d\", \"isCore\": true}}}");

        var cfg = MakeConfig(ws, apiKey: "sk-test");
        var service = new DoctorService(cfg, Path.Combine(_root, "config.json"), templatesDir);

        var report = await service.RunAsync();

        Assert.Equal(DiagnosticStatus.Pass, Find(report, "模板目录").Status);
        Assert.Equal(DiagnosticStatus.Pass, Find(report, "RIBOSOME 本能").Status);
    }

    [Fact]
    public async Task RunAsync_MalformedRibosome_ReportsFailure()
    {
        var ws = CreateHealthyWorkspace();
        var templatesDir = Path.Combine(_root, "templates");
        Directory.CreateDirectory(templatesDir);
        File.WriteAllText(Path.Combine(templatesDir, "RIBOSOME.json"), "{ this is not valid json ");

        var cfg = MakeConfig(ws, apiKey: "sk-test");
        var service = new DoctorService(cfg, Path.Combine(_root, "config.json"), templatesDir);

        var report = await service.RunAsync();

        Assert.Equal(DiagnosticStatus.Fail, Find(report, "RIBOSOME 本能").Status);
        Assert.True(report.HasFailures);
    }

    private static string?[] ClearApiKeyEnvVars()
    {
        var names = new[] { "OPENAI_API_KEY", "DEEPSEEK_API_KEY", "ANTHROPIC_API_KEY", "MYCLAW_API_KEY" };
        var saved = new string?[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            saved[i] = Environment.GetEnvironmentVariable(names[i]);
            Environment.SetEnvironmentVariable(names[i], null);
        }
        return saved;
    }

    private static void RestoreApiKeyEnvVars(string?[] saved)
    {
        var names = new[] { "OPENAI_API_KEY", "DEEPSEEK_API_KEY", "ANTHROPIC_API_KEY", "MYCLAW_API_KEY" };
        for (var i = 0; i < names.Length; i++)
        {
            Environment.SetEnvironmentVariable(names[i], saved[i]);
        }
    }
}
