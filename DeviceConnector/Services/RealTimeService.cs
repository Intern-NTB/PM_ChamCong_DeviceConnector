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
            // Read device list from configuration
            var devices = _configuration.GetSection("Devices").Get<List<DeviceConfig>>();
            if (devices == null || !devices.Any())
            {
                _logger.LogWarning("No devices configured in user secrets");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var device in devices)
                {
                    var deviceId = $"{device.IpAddress}:{device.Port}";
                    
                    // Get or create SDKHelper instance for this device
                    if (!_deviceConnections.ContainsKey(deviceId))
                    {
                        var helper = _sdkHelperManager.GetOrCreateSDKHelper($"realtime_{deviceId}");
                        _deviceConnections[deviceId] = helper;
                    }

                    var currentHelper = _deviceConnections[deviceId];

                    if (!currentHelper.GetConnectionStatus())
                    {
                        _logger.LogInformation($"Attempting to connect to device at {device.IpAddress}:{device.Port}");
                        
                        try
                        {
                            bool connected = await currentHelper.ConnectAsync(device.IpAddress, device.Port, true);
                            
                            if (connected)
                            {
                                _logger.LogInformation($"Successfully connected to device at {device.IpAddress}:{device.Port}");
                            }
                            else
                            {
                                _logger.LogWarning($"Failed to connect to device at {device.IpAddress}:{device.Port}. Retrying in 5 seconds...");
                                await Task.Delay(5000, stoppingToken);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error connecting to device at {device.IpAddress}:{device.Port}. Retrying in 5 seconds...");
                            await Task.Delay(5000, stoppingToken);
                        }
                    }
                }

                // Wait before checking connections again
                await Task.Delay(10000, stoppingToken);
            }
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
