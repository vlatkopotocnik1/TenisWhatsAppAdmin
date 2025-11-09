using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using MudBlazor;
using MudBlazor.Services;
using Serilog;
using Serilog.Events;
using WhatsAppAdmin.Authentication;
using WhatsAppAdmin.Services;
using WhatsAppAdmin.Helpers;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

var isDev = builder.Environment.IsDevelopment();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(isDev ? LogEventLevel.Debug : LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft", isDev ? LogEventLevel.Information : LogEventLevel.Warning)
    .MinimumLevel.Override("System", isDev ? LogEventLevel.Information : LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "WhatsAppAdmin")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
    .CreateLogger();

builder.Host.UseSerilog();

// ✅ Register Blazor/MudBlazor
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.PreventDuplicates = false;
    config.SnackbarConfiguration.NewestOnTop = false;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 4000;
    config.SnackbarConfiguration.HideTransitionDuration = 500;
    config.SnackbarConfiguration.ShowTransitionDuration = 500;
});

// ✅ Protected browser storage and auth
builder.Services.AddScoped<ProtectedSessionStorage>();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, AuthStateProvider>();
builder.Services.AddScoped<AuthStateProvider>();

// ✅ Existing app services
builder.Services.AddSingleton<IService, MockService>();
builder.Services.AddSingleton<ImportStateService>();
builder.Services.AddSingleton<OverlayService>();

builder.Services.AddTransient<LoggingHandler>();

builder.Services.AddHttpClient<WhapiService>((sp, client) =>
{
    client.BaseAddress = new Uri("https://gate.whapi.cloud");
    var cfg = sp.GetRequiredService<IConfiguration>();
    var apiKey = cfg["Whapi:ApiKey"] ?? throw new InvalidOperationException("Whapi:ApiKey not configured.");
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
})
.AddHttpMessageHandler<LoggingHandler>();

// ✅ Register refactored services
builder.Services.AddScoped<GroupSyncService>();   // handles import, sync, delete
builder.Services.AddScoped<DialogManager>();      // handles dialog interactions

builder.WebHost.UseStaticWebAssets();

var app = builder.Build();

// ✅ Serilog request logging
app.UseSerilogRequestLogging(opts =>
{
    opts.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
});

// ✅ Middleware
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
