using EDI_Conector_FC.Jobs;
using EDI_Conector_FC.Models;
using EDI_Conector_FC.Models.SapModels;
using EDI_Conector_FC.Services.OrdersIn;
using EDI_Conector_FC.Services.Remote;
using EDI_Conector_FC.Services.ServiceSAP;
using EDI_Conector_FC.Services.ServiceSAP.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Quartz;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Forzar lectura del appsettings.json desde el output (bin/Debug/net8.0)
builder.Configuration
	.SetBasePath(AppContext.BaseDirectory)
	.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// Serilog a consola
Log.Logger = new LoggerConfiguration()
	.WriteTo.Console()
	.CreateLogger();

builder.Services.AddSerilog();

// ---- Options ----
builder.Services.Configure<JobsOptions>(builder.Configuration.GetSection("Jobs"));
builder.Services.Configure<QuartzScheduleOptions>(builder.Configuration.GetSection("Quartz"));
builder.Services.Configure<PathsOptions>(builder.Configuration.GetSection("Paths"));
builder.Services.Configure<SapOptions>(builder.Configuration.GetSection("LoginSAP"));

// ---- DI Service Layer (igual que tu patrón bueno) ----
builder.Services.AddSingleton<ServiceLoginSL>();
builder.Services.AddSingleton<ServiceLayerClient>();

builder.Services.AddSingleton<IItemResolverService, ItemResolverService>();

builder.Services.AddScoped<IOrdersInProcessor, OrdersInProcessor>();
builder.Services.AddSingleton<IEre1OrdersParser, Ere1OrdersParser>();

//Conexion FTP
builder.Services.Configure<FtpOptions>(builder.Configuration.GetSection("Ftp"));
builder.Services.AddSingleton<IFtpService, FtpService>();


//Docs
builder.Services.Configure<OrdersInOptions>(builder.Configuration.GetSection("OrdersIn"));




// ---- Quartz ----
builder.Services.AddQuartz(q =>
{
	var jobs = builder.Configuration.GetSection("Jobs").Get<JobsOptions>() ?? new JobsOptions();
	var cron = builder.Configuration.GetSection("Quartz").Get<QuartzScheduleOptions>() ?? new QuartzScheduleOptions();

	if (jobs.JobOrdersIn)
	{
		var jobKey = new JobKey(nameof(JobOrdersIn));
		q.AddJob<JobOrdersIn>(opts => opts.WithIdentity(jobKey));
		q.AddTrigger(opts => opts
			.ForJob(jobKey)
			.WithIdentity($"{nameof(JobOrdersIn)}Trigger")
			.WithCronSchedule(cron.JobOrdersIn));
	}

	if (jobs.JobDesadvOut)
	{
		var jobKey = new JobKey(nameof(JobDesadvOut));
		q.AddJob<JobDesadvOut>(opts => opts.WithIdentity(jobKey));
		q.AddTrigger(opts => opts
			.ForJob(jobKey)
			.WithIdentity($"{nameof(JobDesadvOut)}Trigger")
			.WithCronSchedule(cron.JobDesadvOut));
	}

	if (jobs.JobInvoicesOut)
	{
		var jobKey = new JobKey(nameof(JobInvoicesOut));
		q.AddJob<JobInvoicesOut>(opts => opts.WithIdentity(jobKey));
		q.AddTrigger(opts => opts
			.ForJob(jobKey)
			.WithIdentity($"{nameof(JobInvoicesOut)}Trigger")
			.WithCronSchedule(cron.JobInvoicesOut));
	}


	//TEST FTP - Se comenta luego:
	//
	/*
	q.AddJob<JobFtpTest>(opts => opts.WithIdentity("JobFtpTest"));
	q.AddTrigger(opts => opts
		.ForJob("JobFtpTest")
		.WithIdentity("JobFtpTest-trigger")
		.WithCronSchedule("0/30 * * ? * *")); // cada 30 segundos para probar
	*/
	//


});

builder.Services.AddQuartzHostedService(opt => opt.WaitForJobsToComplete = true);

var app = builder.Build();

Log.Information("=== EDI_Conector_FC iniciado (NET8 Host) ===");
Log.Information("ContentRoot={Root}", builder.Environment.ContentRootPath);
Log.Information("BaseDir={Base}", AppContext.BaseDirectory);

await app.RunAsync();
