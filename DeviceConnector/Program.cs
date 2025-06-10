using DeviceConnector.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SDK.Helper; 
using SDK.Repository; 
using Shared.Interface;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

// Configure thread pool settings to prevent thread starvation
ThreadPool.SetMinThreads(100, 100);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Add services to the container.
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = true;
});

// Register SDKHelper as Singleton since it handles long-running events
builder.Services.AddSingleton<SDKHelper>();

// Register repositories as transient to avoid connection sharing issues
builder.Services.AddTransient<INhanVienRepository, NhanVienRepository>();
builder.Services.AddTransient<IChamCongRepository, ChamCongRepository>();

// Better approach for DB connections with connection pooling
builder.Services.AddTransient<IDbConnection>(sp => 
{
    // Ensure connection pooling is enabled
    var connBuilder = new SqlConnectionStringBuilder(connectionString)
    {
        Pooling = true,
        MinPoolSize = 5,
        MaxPoolSize = 100
    };
    return new SqlConnection(connBuilder.ConnectionString);
});

// Enhanced logging
builder.Services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.AddDebug();
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck("sqlServer", () => {
        try {
            using var connection = new SqlConnection(connectionString);
            connection.Open();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) {
            return HealthCheckResult.Unhealthy(exception: ex);
        }
    });

// Add global exception handler middleware
builder.Services.AddExceptionHandler(options => {
    // Log exceptions but don't expose details to clients
});

// Configure graceful shutdown - use builder.Services directly
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

// Configure Kestrel for HTTP/2 and keep-alive settings
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(1);
    options.Limits.Http2.KeepAlivePingTimeout = TimeSpan.FromSeconds(20);
    options.Limits.Http2.KeepAlivePingDelay = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

// Global exception handling middleware
app.UseExceptionHandler("/error");

// Map health checks endpoint
app.MapHealthChecks("/health");

app.MapGrpcService<DeviceService>();
app.MapGrpcService<EmployeeService>();

// Configure the HTTP request pipeline.
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

// Set up graceful termination handling
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    Console.WriteLine("Application is stopping. Cleaning up resources...");
    // Add any additional cleanup logic here
});

app.Run();
