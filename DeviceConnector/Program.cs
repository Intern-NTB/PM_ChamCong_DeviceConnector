using DeviceConnector.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using SDK.Helper; 
using SDK.Repository; 
using Shared.Interface;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Add services to the container.
builder.Services.AddGrpc();

// Register SDKHelper as Singleton since it handles long-running events
builder.Services.AddSingleton<SDKHelper>();

// Register repositories as transient to avoid connection sharing issues
builder.Services.AddTransient<INhanVienRepository, NhanVienRepository>();
builder.Services.AddTransient<IChamCongRepository, ChamCongRepository>();

// Register a factory for creating database connections on demand
builder.Services.AddTransient<IDbConnection>(sp => {
    var conn = new SqlConnection(connectionString);
    return conn;
});

builder.Services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.AddDebug();

});
// Add interfaces for better abstraction


var app = builder.Build();

app.MapGrpcService<DeviceService>();
app.MapGrpcService<EmployeeService>();

// Configure the HTTP request pipeline.
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
