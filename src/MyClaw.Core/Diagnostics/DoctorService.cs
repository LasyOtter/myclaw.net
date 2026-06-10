using System.Text.Json;
using MyClaw.Core.Configuration;
using MyClaw.Core.Ribosome;

namespace MyClaw.Core.Diagnostics;

/// <summary>
/// 自诊断服务（对标 hermes-agent 的 <c>doctor</c>）。
///
/// 对配置、工作区、核心 DNA 文件、本能定义等做一系列健康检查，逐项给出
/// 通过 / 告警 / 失败状态与可执行的修复建议。所有检查均为只读，不会修改磁盘，
/// 因此可安全反复运行，也便于单元测试。
/// </summary>
public class DoctorService
{
    private readonly MyClawConfiguration _cfg;
    private readonly string _configPath;
    private readonly string? _templatesDir;

    /// <summary>
    /// 模板同步会落地到工作区根目录的核心 DNA 文件，缺失则建议 <c>sync-templates</c>。
    /// </summary>
    private static readonly string[] EssentialDnaFiles =
    {
        "AGENTS.md", "SOUL.md", "IDENTITY.md", "USER.md", "MEMORY.md", "TOOLS.md", "HEARTBEAT.md"
    };

    /// <summary>
    /// 可提供 API 密钥的环境变量（与 StatusCommand 保持一致）。
    /// </summary>
    private static readonly string[] ApiKeyEnvVars =
    {
        "OPENAI_API_KEY", "DEEPSEEK_API_KEY", "ANTHROPIC_API_KEY", "MYCLAW_API_KEY"
    };

    /// <param name="cfg">已加载的配置。</param>
    /// <param name="configPath">配置文件路径（用于检查其是否存在）。</param>
    /// <param name="templatesDir">模板目录；为 null 表示未找到。</param>
    public DoctorService(MyClawConfiguration cfg, string configPath, string? templatesDir = null)
    {
        _cfg = cfg;
        _configPath = configPath;
        _templatesDir = templatesDir;
    }

    /// <summary>
    /// 运行全部诊断检查并返回汇总报告。
    /// </summary>
    public async Task<DiagnosticReport> RunAsync()
    {
        var report = new DiagnosticReport();
        report.Checks.Add(CheckConfigFile());
        report.Checks.Add(CheckApiKey());
        report.Checks.Add(CheckWorkspace());
        report.Checks.Add(CheckMemoryDir());
        report.Checks.Add(CheckDnaFiles());
        report.Checks.Add(CheckSkills());
        report.Checks.Add(CheckTemplates());
        report.Checks.Add(await CheckRibosomeAsync());
        return report;
    }

    private DiagnosticCheck CheckConfigFile()
    {
        if (File.Exists(_configPath))
        {
            return new DiagnosticCheck
            {
                Name = "配置文件",
                Status = DiagnosticStatus.Pass,
                Message = _configPath
            };
        }

        return new DiagnosticCheck
        {
            Name = "配置文件",
            Status = DiagnosticStatus.Warn,
            Message = $"未找到 {_configPath}（将使用默认配置）",
            Hint = "运行 `myclaw onboard` 生成默认配置文件。"
        };
    }

    private DiagnosticCheck CheckApiKey()
    {
        var hasKey = !string.IsNullOrWhiteSpace(_cfg.Provider.ApiKey);
        var envVar = ApiKeyEnvVars.FirstOrDefault(
            e => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(e)));

        if (hasKey || envVar != null)
        {
            var source = envVar != null ? $"环境变量 {envVar}" : "配置文件";
            var provider = string.IsNullOrEmpty(_cfg.Provider.Type) ? "默认" : _cfg.Provider.Type;
            return new DiagnosticCheck
            {
                Name = "API 密钥",
                Status = DiagnosticStatus.Pass,
                Message = $"已配置（来源：{source}，provider：{provider}）"
            };
        }

        return new DiagnosticCheck
        {
            Name = "API 密钥",
            Status = DiagnosticStatus.Warn,
            Message = "未设置",
            Hint = "`agent` / `gateway` 命令需要密钥；设置 OPENAI_API_KEY 等环境变量，或写入 config.json 的 provider.apiKey。"
        };
    }

    private DiagnosticCheck CheckWorkspace()
    {
        var ws = _cfg.Agent.Workspace;
        if (!string.IsNullOrEmpty(ws) && Directory.Exists(ws))
        {
            return new DiagnosticCheck
            {
                Name = "工作区",
                Status = DiagnosticStatus.Pass,
                Message = ws
            };
        }

        return new DiagnosticCheck
        {
            Name = "工作区",
            Status = DiagnosticStatus.Fail,
            Message = $"不存在：{ws}",
            Hint = "运行 `myclaw onboard` 初始化工作区。"
        };
    }

    private DiagnosticCheck CheckMemoryDir()
    {
        var memoryDir = Path.Combine(_cfg.Agent.Workspace, "memory");
        if (Directory.Exists(memoryDir))
        {
            var logCount = Directory.EnumerateFiles(memoryDir, "*.md", SearchOption.AllDirectories).Count();
            return new DiagnosticCheck
            {
                Name = "记忆目录",
                Status = DiagnosticStatus.Pass,
                Message = $"{memoryDir}（{logCount} 个日志文件）"
            };
        }

        return new DiagnosticCheck
        {
            Name = "记忆目录",
            Status = DiagnosticStatus.Warn,
            Message = $"未找到 {memoryDir}",
            Hint = "运行 `myclaw onboard` 创建 memory/ 目录。"
        };
    }

    private DiagnosticCheck CheckDnaFiles()
    {
        var ws = _cfg.Agent.Workspace;
        var missing = EssentialDnaFiles
            .Where(f => !File.Exists(Path.Combine(ws, f)))
            .ToList();

        if (missing.Count == 0)
        {
            return new DiagnosticCheck
            {
                Name = "核心 DNA 文件",
                Status = DiagnosticStatus.Pass,
                Message = $"{EssentialDnaFiles.Length} 个核心文件齐全"
            };
        }

        return new DiagnosticCheck
        {
            Name = "核心 DNA 文件",
            Status = DiagnosticStatus.Warn,
            Message = $"缺失 {missing.Count} 个：{string.Join(", ", missing)}",
            Hint = "运行 `myclaw sync-templates` 从模板补齐缺失的 DNA 文件。"
        };
    }

    private DiagnosticCheck CheckSkills()
    {
        if (!_cfg.Skills.Enabled)
        {
            return new DiagnosticCheck
            {
                Name = "技能",
                Status = DiagnosticStatus.Pass,
                Message = "已禁用"
            };
        }

        var skillsDir = string.IsNullOrEmpty(_cfg.Skills.Dir)
            ? Path.Combine(_cfg.Agent.Workspace, "skills")
            : _cfg.Skills.Dir;

        if (Directory.Exists(skillsDir))
        {
            var skillCount = Directory.EnumerateDirectories(skillsDir).Count();
            return new DiagnosticCheck
            {
                Name = "技能",
                Status = DiagnosticStatus.Pass,
                Message = $"已启用（{skillsDir}，{skillCount} 个技能）"
            };
        }

        return new DiagnosticCheck
        {
            Name = "技能",
            Status = DiagnosticStatus.Warn,
            Message = $"已启用但目录不存在：{skillsDir}",
            Hint = "运行 `myclaw onboard` 创建 skills/ 目录。"
        };
    }

    private DiagnosticCheck CheckTemplates()
    {
        if (!string.IsNullOrEmpty(_templatesDir) && Directory.Exists(_templatesDir))
        {
            return new DiagnosticCheck
            {
                Name = "模板目录",
                Status = DiagnosticStatus.Pass,
                Message = _templatesDir
            };
        }

        return new DiagnosticCheck
        {
            Name = "模板目录",
            Status = DiagnosticStatus.Warn,
            Message = "未找到",
            Hint = "设置 MYCLAW_TEMPLATES_DIR，或在包含 templates/ 的项目根目录运行；否则 `sync-templates` 不可用。"
        };
    }

    private async Task<DiagnosticCheck> CheckRibosomeAsync()
    {
        var userPath = Path.Combine(ConfigurationLoader.ConfigDir, "RIBOSOME.json");
        var templatePath = string.IsNullOrEmpty(_templatesDir)
            ? null
            : Path.Combine(_templatesDir, "RIBOSOME.json");
        var sourcePath = File.Exists(userPath)
            ? userPath
            : (templatePath != null && File.Exists(templatePath) ? templatePath : null);

        // 文件存在但 JSON 损坏时，加载器会静默回退到内置默认本能，
        // 这里主动解析一次以便明确报出损坏。
        if (sourcePath != null)
        {
            try
            {
                using var _ = JsonDocument.Parse(await File.ReadAllTextAsync(sourcePath));
            }
            catch (JsonException ex)
            {
                return new DiagnosticCheck
                {
                    Name = "RIBOSOME 本能",
                    Status = DiagnosticStatus.Fail,
                    Message = $"{sourcePath} 解析失败：{ex.Message}",
                    Hint = "修复 RIBOSOME.json 的 JSON 语法，或运行 `myclaw sync-templates` 从模板恢复。"
                };
            }
        }

        var loader = new RibosomeLoader(ConfigurationLoader.ConfigDir, _templatesDir ?? string.Empty);
        var instincts = await loader.LoadInstinctsAsync();

        if (sourcePath != null)
        {
            return new DiagnosticCheck
            {
                Name = "RIBOSOME 本能",
                Status = DiagnosticStatus.Pass,
                Message = $"{instincts.Count} 个本能（来源：{sourcePath}）"
            };
        }

        return new DiagnosticCheck
        {
            Name = "RIBOSOME 本能",
            Status = DiagnosticStatus.Warn,
            Message = $"未找到 RIBOSOME.json，已回退内置默认本能（{instincts.Count} 个）",
            Hint = "运行 `myclaw sync-templates` 将 RIBOSOME.json 落地到工作区/配置目录。"
        };
    }
}
