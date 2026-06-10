using System.Text.Json;
using Quartz;
using Quartz.Impl;

namespace MyClaw.Cron;

/// <summary>
/// Cron 服务 - 定时任务调度
/// </summary>
public class CronService
{
    private readonly string _storePath;
    private readonly IScheduler _scheduler;
    private readonly List<CronJob> _jobs = new();
    private readonly object _lock = new();
    private readonly Dictionary<string, IJobDetail> _jobDetails = new();

    /// <summary>
    /// 任务执行回调
    /// </summary>
    public Func<CronJob, Task<string>>? OnJob { get; set; }

    public CronService(string storePath)
    {
        _storePath = storePath;
        var schedulerFactory = new StdSchedulerFactory();
        _scheduler = schedulerFactory.GetScheduler().Result;
    }

    /// <summary>
    /// 启动 Cron 服务
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        // 加载已有任务
        await LoadAsync();

        // 启动调度器
        await _scheduler.Start(ct);

        // 注册所有启用的 cron 类型任务（在锁外 await，避免锁内同步等待异步）
        List<CronJob> cronJobs;
        lock (_lock)
        {
            cronJobs = _jobs.Where(j => j.Enabled && j.Schedule.Kind == "cron").ToList();
        }
        foreach (var job in cronJobs)
        {
            await RegisterCronJob(job);
        }

        Console.WriteLine($"[cron] 已启动，共 {_jobs.Count} 个任务");

        // 启动 tick loop 处理 "every" 和 "at" 类型任务
        _ = TickLoopAsync(ct);
    }

    /// <summary>
    /// 停止 Cron 服务
    /// </summary>
    public async Task StopAsync()
    {
        await _scheduler.Shutdown();
        Console.WriteLine("[cron] 已停止");
    }

    /// <summary>
    /// 添加任务
    /// </summary>
    public async Task<CronJob> AddJobAsync(string name, Schedule schedule, Payload payload)
    {
        var job = CronJob.Create(name, schedule, payload);

        lock (_lock)
        {
            _jobs.Add(job);
        }

        if (job.Schedule.Kind == "cron")
        {
            await RegisterCronJob(job);
        }

        await SaveAsync();
        return job;
    }

    /// <summary>
    /// 删除任务
    /// </summary>
    public async Task<bool> RemoveJobAsync(string id)
    {
        IJobDetail? toDelete = null;
        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == id);
            if (job == null) return false;

            _jobs.Remove(job);

            // 记录待移除的 jobDetail，实际删除放到锁外 await
            if (_jobDetails.TryGetValue(id, out var jobDetail))
            {
                toDelete = jobDetail;
                _jobDetails.Remove(id);
            }
        }

        if (toDelete != null)
        {
            await _scheduler.DeleteJob(toDelete.Key);
        }

        await SaveAsync();
        return true;
    }

    /// <summary>
    /// 获取所有任务
    /// </summary>
    public List<CronJob> ListJobs()
    {
        lock (_lock)
        {
            return _jobs.ToList();
        }
    }

    /// <summary>
    /// 启用/禁用任务
    /// </summary>
    public async Task<CronJob?> EnableJobAsync(string id, bool enabled)
    {
        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == id);
            if (job == null) return null;

            job.Enabled = enabled;
            return job;
        }
    }

    /// <summary>
    /// 注册 Cron 类型任务到 Quartz
    /// </summary>
    private async Task RegisterCronJob(CronJob job)
    {
        var jobDetail = JobBuilder.Create<AgentJob>()
            .WithIdentity(job.Id)
            .UsingJobData("jobId", job.Id)
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{job.Id}-trigger")
            .WithCronSchedule(job.Schedule.Expr)
            .Build();

        await _scheduler.ScheduleJob(jobDetail, trigger);
        _jobDetails[job.Id] = jobDetail;
    }

    /// <summary>
    /// 执行单个任务
    /// </summary>
    internal async Task ExecuteJobAsync(CronJob job)
    {
        Console.WriteLine($"[cron] 执行任务 {job.Name} ({job.Id})");

        if (OnJob == null)
        {
            Console.WriteLine("[cron] 未设置 OnJob 处理程序");
            return;
        }

        try
        {
            var result = await OnJob(job);

            lock (_lock)
            {
                job.State.LastRunAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                job.State.LastStatus = "ok";
                job.State.LastError = "";
            }

            Console.WriteLine($"[cron] 任务 {job.Name} 结果: {Truncate(result, 100)}");
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                job.State.LastRunAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                job.State.LastStatus = "error";
                job.State.LastError = ex.Message;
            }

            Console.WriteLine($"[cron] 任务 {job.Name} 错误: {ex.Message}");
        }

        // 如果是一次性任务，执行后删除
        if (job.DeleteAfterRun)
        {
            await RemoveJobAsync(job.Id);
        }
        else
        {
            await SaveAsync();
        }
    }

    /// <summary>
    /// Tick loop - 处理 "every" 和 "at" 类型任务
    /// </summary>
    private async Task TickLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (await timer.WaitForNextTickAsync(ct))
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            lock (_lock)
            {
                foreach (var job in _jobs.Where(j => j.Enabled))
                {
                    switch (job.Schedule.Kind)
                    {
                        case "every":
                            if (job.Schedule.EveryMs > 0)
                            {
                                var nextRun = job.State.LastRunAtMs + job.Schedule.EveryMs;
                                if (now >= nextRun)
                                {
                                    // 执行但不等待（避免阻塞）
                                    _ = ExecuteJobAsync(job);
                                }
                            }
                            break;

                        case "at":
                            if (job.Schedule.AtMs > 0 && now >= job.Schedule.AtMs)
                            {
                                job.Enabled = false;
                                _ = ExecuteJobAsync(job);
                            }
                            break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 从文件加载任务
    /// </summary>
    private async Task LoadAsync()
    {
        if (!File.Exists(_storePath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(_storePath);
            var jobs = JsonSerializer.Deserialize<List<CronJob>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (jobs != null)
            {
                lock (_lock)
                {
                    _jobs.Clear();
                    _jobs.AddRange(jobs);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[cron] 警告: 加载任务失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 保存任务到文件
    /// </summary>
    private async Task SaveAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            lock (_lock)
            {
                var json = JsonSerializer.Serialize(_jobs, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_storePath, json);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[cron] 警告: 保存任务失败: {ex.Message}");
        }
    }

    private static string Truncate(string s, int n)
    {
        if (s.Length <= n)
            return s;
        return s[..n] + "...";
    }
}

/// <summary>
/// Quartz Job 实现
/// </summary>
public class AgentJob : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var jobId = context.JobDetail.Key.Name;
        var service = context.JobDetail.JobDataMap["service"] as CronService;
        
        if (service != null)
        {
            var job = service.ListJobs().FirstOrDefault(j => j.Id == jobId);
            if (job != null)
            {
                await service.ExecuteJobAsync(job);
            }
        }
    }
}
