using DeviceConnector.Services;
using Microsoft.Data.SqlClient;
using SDK.Helper; 
using System.Data;
using SDK.Repository; 
using Shared.Interface;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Add services to the container.
builder.Services.AddGrpc();

// Update registration
builder.Services.AddScoped<SDKHelper>(); // Change to scoped
builder.Services.AddTransient<IDbConnection>(sp => new SqlConnection(connectionString)); // Use transient
builder.Services.AddScoped<NhanVienRepository>();
builder.Services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.AddDebug();

});
// Add interfaces for better abstraction
builder.Services.AddScoped<INhanVienRepository, NhanVienRepository>();

var app = builder.Build();

app.MapGrpcService<DeviceService>();
app.MapGrpcService<EmployeeService>();

// Configure the HTTP request pipeline.
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
