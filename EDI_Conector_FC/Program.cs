using EDI_Conector_FC.Jobs;
using EDI_Conector_FC.Models;
using EDI_Conector_FC.Models.SapModels;
using EDI_Conector_FC.Services.ClientConfig;
using EDI_Conector_FC.Services.CsvGenerator;
using EDI_Conector_FC.Services.CsvImporter;
using EDI_Conector_FC.Services.OrdersIn;
using EDI_Conector_FC.Services.Remote;
using EDI_Conector_FC.Services.ServiceSAP;
using EDI_Conector_FC.Services.ServiceSAP.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// ── Serilog ───────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(
            builder.Configuration["Logging:LogPath"] ?? "C:\\EDI_Conector_FC\\Logs",
            "edi_conector_.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateLogger();

builder.Services.AddSerilog();

// ── Options ───────────────────────────────────────────────────────────────────
builder.Services.Configure<JobsOptions>(builder.Configuration.GetSection("Jobs"));
builder.Services.Configure<QuartzScheduleOptions>(builder.Configuration.GetSection("Quartz"));
builder.Services.Configure<PathsOptions>(builder.Configuration.GetSection("Paths"));
builder.Services.Configure<SapOptions>(builder.Configuration.GetSection("LoginSAP"));
builder.Services.Configure<FtpOptions>(builder.Configuration.GetSection("Ftp"));
builder.Services.Configure<OrdersInOptions>(builder.Configuration.GetSection("OrdersIn"));

// ── SAP Service Layer ─────────────────────────────────────────────────────────
builder.Services.AddSingleton<ServiceLoginSL>();
builder.Services.AddSingleton<ServiceLayerClient>();
builder.Services.AddSingleton<IItemResolverService, ItemResolverService>();

// ── FTP ───────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IFtpService, FtpService>();         // legacy ECI
builder.Services.AddSingleton<IFtpServiceFactory, FtpServiceFactory>(); // multi-cliente

// ── Parser (compartido entre legacy y nuevo flujo) ────────────────────────────
builder.Services.AddSingleton<IEre1OrdersParser, Ere1OrdersParser>();

// ── Nuevo flujo: EDI → CSV → SAP ─────────────────────────────────────────────
builder.Services.AddSingleton<IClientConfigLoader, ClientConfigLoader>();
builder.Services.AddSingleton<IOrderCsvGenerator, OrderCsvGenerator>();
builder.Services.AddSingleton<IOrderCsvReader, OrderCsvReader>();
builder.Services.AddSingleton<IOrderCsvImporter, OrderCsvImporter>();

// ── Procesador legacy ECI ─────────────────────────────────────────────────────
builder.Services.AddScoped<IOrdersInProcessor, OrdersInProcessor>();

// ── Quartz ────────────────────────────────────────────────────────────────────
builder.Services.AddQuartz(q =>
{
    var jobs = builder.Configuration.GetSection("Jobs").Get<JobsOptions>() ?? new JobsOptions();
    var cron = builder.Configuration.GetSection("Quartz").Get<QuartzScheduleOptions>() ?? new QuartzScheduleOptions();

    // ── Job legacy: ECI directo a SAP ────────────────────────────────────────
    if (jobs.JobOrdersIn)
    {
        var key = new JobKey(nameof(JobOrdersIn));
        q.AddJob<JobOrdersIn>(o => o.WithIdentity(key));
        q.AddTrigger(o => o
            .ForJob(key)
            .WithIdentity($"{nameof(JobOrdersIn)}Trigger")
            .WithCronSchedule(cron.JobOrdersIn));
    }

    if (jobs.JobDesadvOut)
    {
        var key = new JobKey(nameof(JobDesadvOut));
        q.AddJob<JobDesadvOut>(o => o.WithIdentity(key));
        q.AddTrigger(o => o
            .ForJob(key)
            .WithIdentity($"{nameof(JobDesadvOut)}Trigger")
            .WithCronSchedule(cron.JobDesadvOut));
    }

    if (jobs.JobInvoicesOut)
    {
        var key = new JobKey(nameof(JobInvoicesOut));
        q.AddJob<JobInvoicesOut>(o => o.WithIdentity(key));
        q.AddTrigger(o => o
            .ForJob(key)
            .WithIdentity($"{nameof(JobInvoicesOut)}Trigger")
            .WithCronSchedule(cron.JobInvoicesOut));
    }

    // ── Paso 1: EDI → CSV ─────────────────────────────────────────────────────
    if (jobs.JobOrdersEdiToCsv)
    {
        var key = new JobKey(nameof(JobOrdersEdiToCsv));
        q.AddJob<JobOrdersEdiToCsv>(o => o.WithIdentity(key));
        q.AddTrigger(o => o
            .ForJob(key)
            .WithIdentity($"{nameof(JobOrdersEdiToCsv)}Trigger")
            .WithCronSchedule(cron.JobOrdersEdiToCsv));
    }

    // ── Paso 2: CSV → SAP ─────────────────────────────────────────────────────
    if (jobs.JobOrdersCsvToSap)
    {
        var key = new JobKey(nameof(JobOrdersCsvToSap));
        q.AddJob<JobOrdersCsvToSap>(o => o.WithIdentity(key));
        q.AddTrigger(o => o
            .ForJob(key)
            .WithIdentity($"{nameof(JobOrdersCsvToSap)}Trigger")
            .WithCronSchedule(cron.JobOrdersCsvToSap));
    }
});

builder.Services.AddQuartzHostedService(opt => opt.WaitForJobsToComplete = true);

var app = builder.Build();

Log.Information("=== EDI_Conector_FC iniciado ===");
Log.Information("BaseDir={Base}", AppContext.BaseDirectory);

await app.RunAsync();
