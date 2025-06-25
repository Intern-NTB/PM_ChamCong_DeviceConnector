using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using SDK.Helper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DeviceConnector.Services
{
    public class RealTimeService : BackgroundService
    {
        private readonly IConfiguration _configuration;
        private readonly SDKHelperManager _sdkHelperManager;
        private readonly Dictionary<string, SDKHelper> _deviceConnections;
        private readonly ILogger<RealTimeService> _logger;

        public RealTimeService(
            IConfiguration configuration,
            SDKHelperManager sdkHelperManager,
            ILogger<RealTimeService> logger)
        {
            _configuration = configuration;
            _sdkHelperManager = sdkHelperManager;
            _deviceConnections = new Dictionary<string, SDKHelper>();
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RealTimeService is starting.");

            // Add a delay at startup to allow other services to initialize
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            var devices = _configuration.GetSection("Devices").Get<List<DeviceConfig>>();
            if (devices == null || !devices.Any())
            {
                _logger.LogWarning("No devices found in configuration. RealTimeService will not run.");
                return;
            }

            // Initialize all device connections at startup
            foreach (var device in devices)
            {
                var deviceId = $"{device.IpAddress}:{device.Port}";
                var helper = _sdkHelperManager.GetOrCreateSDKHelper($"realtime_{deviceId}");
                _deviceConnections[deviceId] = helper;
                _logger.LogInformation("Initialized helper for device {DeviceId}", deviceId);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogDebug("Starting new device connection check cycle.");
                    
                    foreach (var device in devices)
                    {
                        var deviceId = $"{device.IpAddress}:{device.Port}";
                        var currentHelper = _deviceConnections[deviceId];

                        // RELIABLE RECONNECT STRATEGY:
                        // 1. Always attempt to disconnect first. This clears any stale/dead connections in the SDK.
                        // 2. Then, attempt to connect. This establishes a fresh connection.
                        // This mimics a full restart for the specific device without restarting the whole application.
                        _logger.LogInformation("Attempting to refresh connection to device {DeviceId}...", deviceId);

                        bool connectionSuccessful = false;
                        int retryCount = 0;
                        const int maxRetries = 3;

                        while (!connectionSuccessful && retryCount < maxRetries && !stoppingToken.IsCancellationRequested)
                        {
                            try
                            {
                                // Step 1: Disconnect to clear any stale state.
                                await currentHelper.DisconnectAsync();

                                // Step 2: Connect to establish a fresh connection.
                                bool connected = await currentHelper.ConnectAsync(device.IpAddress, device.Port, true);
                                
                                if (connected)
                                {
                                    _logger.LogInformation("Successfully established fresh connection to device {DeviceId}.", deviceId);
                                    connectionSuccessful = true;
                                }
                                else
                                {
                                    retryCount++;
                                    _logger.LogWarning("Failed to establish fresh connection to device {DeviceId}. Retry {RetryCount}/{MaxRetries}.", deviceId, retryCount, maxRetries);
                                    
                                    if (retryCount < maxRetries)
                                    {
                                        // Wait 2 seconds before retry
                                        await Task.Delay(2000, stoppingToken);
                                    }
                                }
                            }
                            catch (Exception connEx)
                            {
                                retryCount++;
                                _logger.LogError(connEx, "Error during connection refresh for device {DeviceId}. Retry {RetryCount}/{MaxRetries}.", deviceId, retryCount, maxRetries);
                                
                                if (retryCount < maxRetries)
                                {
                                    // Wait 2 seconds before retry
                                    await Task.Delay(2000, stoppingToken);
                                }
                            }
                        }

                        if (!connectionSuccessful)
                        {
                            _logger.LogError("Failed to connect to device {DeviceId} after {MaxRetries} attempts. Will try again in next cycle.", deviceId, maxRetries);
                        }
                    }

                    // Wait for the next cycle
                    _logger.LogDebug("Device connection check cycle complete. Waiting for 30 seconds.");
                    await Task.Delay(TimeSpan.FromSeconds(300), stoppingToken);
                }
                catch (Exception ex)
                {
                    // This is the crucial part. It catches ANY error in the loop.
                    _logger.LogCritical(ex, "An unexpected error occurred in the RealTimeService execution loop. The service will attempt to recover and continue after a short delay.");

                    // Wait for a moment before restarting the loop to prevent rapid-fire failures
                    await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                }
            }

            _logger.LogInformation("RealTimeService is stopping.");
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            // Cleanup connections when service stops
            foreach (var connection in _deviceConnections.Values)
            {
                try
                {
                    await connection.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disconnecting device");
                }
            }
            _deviceConnections.Clear();

            await base.StopAsync(cancellationToken);
        }
    }

    public class DeviceConfig
    {
        public string IpAddress { get; set; }
        public int Port { get; set; }
    }
}
