using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using MyClaw.Core.Configuration;
using MyClaw.Core.Diagnostics;
using Spectre.Console;

namespace MyClaw.CLI.Commands;

/// <summary>
/// Doctor 命令 - 自诊断 myclaw 安装与配置的健康状况（对标 hermes-agent 的 doctor）。
/// 全部检查只读，存在致命问题时进程返回非零退出码，便于脚本/CI 使用。
/// </summary>
public class DoctorCommand : Command
{
    public DoctorCommand() : base("doctor", "自诊断 myclaw 的配置与环境健康状况")
    {
        this.SetHandler(async (InvocationContext context) =>
        {
            MyClawConfiguration cfg;
            try
            {
                cfg = ConfigurationLoader.Load();
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]✗ 配置加载失败: {Markup.Escape(ex.Message)}[/]");
                AnsiConsole.MarkupLine("[dim]建议: 运行 `myclaw onboard` 重新生成配置。[/]");
                context.ExitCode = 1;
                return;
            }

            var service = new DoctorService(cfg, ConfigurationLoader.ConfigPath, TemplateLocator.Find());
            var report = await service.RunAsync();

            var table = new Table();
            table.AddColumn("检查项");
            table.AddColumn("状态");
            table.AddColumn("详情");

            foreach (var check in report.Checks)
            {
                var detail = Markup.Escape(check.Message);
                if (check.Status != DiagnosticStatus.Pass && !string.IsNullOrEmpty(check.Hint))
                {
                    detail += $"\n[dim]→ {Markup.Escape(check.Hint)}[/]";
                }

                table.AddRow(
                    Markup.Escape(check.Name),
                    StatusLabel(check.Status),
                    detail);
            }

            AnsiConsole.Write(table);

            var summary = $"通过 {report.PassCount} · 告警 {report.WarnCount} · 失败 {report.FailCount}";
            if (report.HasFailures)
            {
                AnsiConsole.MarkupLine($"[red]✗ 发现致命问题。{summary}[/]");
                context.ExitCode = 1;
            }
            else if (report.HasWarnings)
            {
                AnsiConsole.MarkupLine($"[yellow]⚠ 存在告警，部分功能可能受限。{summary}[/]");
                context.ExitCode = 0;
            }
            else
            {
                AnsiConsole.MarkupLine($"[green]✓ 一切正常。{summary}[/]");
                context.ExitCode = 0;
            }
        });
    }

    private static string StatusLabel(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Pass => "[green]✓ 通过[/]",
        DiagnosticStatus.Warn => "[yellow]⚠ 告警[/]",
        DiagnosticStatus.Fail => "[red]✗ 失败[/]",
        _ => status.ToString()
    };
}
