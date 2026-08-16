using System.Text.Json;
using PacsCdTransfer.Core.Models;
using PacsCdTransfer.Core.Services;
using PacsCdTransfer.Web;

var builder = WebApplication.CreateBuilder(args);

// Load settings
var settingsStore = new AppSettingsStore();
var settings = settingsStore.Load();

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(settingsStore);
builder.Services.AddSingleton<ConnectionLogService>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:*", "https://localhost:*")
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

// Setup storage folder
var storageRoot = Path.IsPathRooted(settings.TempStorageFolder)
    ? settings.TempStorageFolder
    : Path.Combine(AppContext.BaseDirectory, settings.TempStorageFolder);
Directory.CreateDirectory(storageRoot);

// Start DICOM server
var serverHost = new DicomServerHost(settings.LocalPort, settings.LocalAeTitle, storageRoot);
try
{
    serverHost.Start();
    app.Logger.LogInformation($"DICOM SCP listening on port {settings.LocalPort}");
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "DICOM SCP failed to start");
}

app.UseStaticFiles();
app.UseRouting();
app.UseCors();

// Session storage for this instance
var bridge = new WebBridge(settings, app.Services.GetRequiredService<ConnectionLogService>());

// API endpoint
app.MapPost("/api", async (HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var request = await reader.ReadToEndAsync();
    var response = await bridge.HandleAsync(request);
    return Results.Content(response, "application/json");
});

// Serve app.html as default
app.MapGet("/", () =>
{
    var htmlPath = Path.Combine(app.Environment.WebRootPath, "app.html");
    return Results.File(htmlPath, "text/html");
});

// Health check
app.MapGet("/health", () => Results.Ok(new { ok = true }));

app.Run();
