using EDI_Conector_FC.Jobs;
using EDI_Conector_FC.Models;
using EDI_Conector_FC.Models.SapModels;
using EDI_Conector_FC.Services.ClientConfig;
using EDI_Conector_FC.Services.CsvGenerator;
using EDI_Conector_FC.Services.CsvImporter;
using EDI_Conector_FC.Services.Desadv;
using EDI_Conector_FC.Services.Invoic;
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

// Permite que el mismo .exe corra como consola (debug) o como Servicio de Windows.
// Solo activa el ciclo de vida de servicio cuando el SO realmente lo arranca como tal.
builder.Services.AddWindowsService(options =>
{
	options.ServiceName = "EDI_Conector_FC";
});

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
builder.Services.Configure<SapOptions>(builder.Configuration.GetSection("LoginSAP"));


// ── SAP Service Layer ─────────────────────────────────────────────────────────
builder.Services.AddSingleton<ServiceLoginSL>();
builder.Services.AddSingleton<ServiceLayerClient>();
builder.Services.AddSingleton<IItemResolverService, ItemResolverService>();
builder.Services.AddSingleton<IPackResolverService, PackResolverService>();
builder.Services.AddSingleton<IBooztMetadataService, BooztMetadataService>();

// ── FTP ───────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IFtpService, FtpService>();
builder.Services.AddSingleton<IFtpServiceFactory, FtpServiceFactory>();

// ── Parser ────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IEre1OrdersParser, Ere1OrdersParser>();

// ── Pedidos entrada: EDI → CSV → SAP ─────────────────────────────────────────
builder.Services.AddSingleton<IClientConfigLoader, ClientConfigLoader>();
builder.Services.AddSingleton<IOrderCsvGenerator, OrderCsvGenerator>();
builder.Services.AddSingleton<IOrderCsvReader, OrderCsvReader>();
builder.Services.AddSingleton<IOrderCsvImporter, OrderCsvImporter>();

// ── DESADV salida ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IDesadvSapService, DesadvSapService>();
builder.Services.AddSingleton<IDesadvFileGenerator, DesadvFileGenerator>();

// ── INVOIC salida ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IInvoicSapService, InvoicSapService>();
builder.Services.AddSingleton<IInvoicFileGenerator, InvoicFileGenerator>();

// ── Procesador legacy ECI ─────────────────────────────────────────────────────
builder.Services.AddScoped<IOrdersInProcessor, OrdersInProcessor>();

// ── Quartz ────────────────────────────────────────────────────────────────────
builder.Services.AddQuartz(q =>
{
	var jobs = builder.Configuration.GetSection("Jobs").Get<JobsOptions>() ?? new JobsOptions();
	var cron = builder.Configuration.GetSection("Quartz").Get<QuartzScheduleOptions>() ?? new QuartzScheduleOptions();

	void AddJob<T>(string name, string cronExpr) where T : IJob
	{
		var key = new JobKey(name);
		q.AddJob<T>(o => o.WithIdentity(key));
		q.AddTrigger(o => o
			.ForJob(key)
			.WithIdentity($"{name}Trigger")
			.WithCronSchedule(cronExpr));
	}

	//if (jobs.JobOrdersIn) AddJob<JobOrdersIn>(nameof(JobOrdersIn), cron.JobOrdersIn);
	if (jobs.JobDesadvOut) AddJob<JobDesadvOut>(nameof(JobDesadvOut), cron.JobDesadvOut);
	if (jobs.JobInvoicesOut) AddJob<JobInvoicesOut>(nameof(JobInvoicesOut), cron.JobInvoicesOut);
	if (jobs.JobOrdersEdiToCsv) AddJob<JobOrdersEdiToCsv>(nameof(JobOrdersEdiToCsv), cron.JobOrdersEdiToCsv);
	if (jobs.JobOrdersCsvToSap) AddJob<JobOrdersCsvToSap>(nameof(JobOrdersCsvToSap), cron.JobOrdersCsvToSap);
});

builder.Services.AddQuartzHostedService(opt => opt.WaitForJobsToComplete = true);

var app = builder.Build();

Log.Information("=== EDI_Conector_FC iniciado ===");
Log.Information("BaseDir={Base}", AppContext.BaseDirectory);

await app.RunAsync();