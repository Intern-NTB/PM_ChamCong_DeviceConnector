using DeviceConnector.Protos;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using System.Diagnostics;
using SDK.Helper;

namespace DeviceConnector.Services
{
    public class DeviceService : Protos.DeviceConnector.DeviceConnectorBase
    {
        private readonly SDKHelper _sdkHelper;

        public DeviceService(SDKHelper sdkHelper)
        {
            _sdkHelper = sdkHelper;
        }

        public override async Task<ConnectResponse> Connect(ConnectRequest request, ServerCallContext context)
        {
            Debug.WriteLine($"Received connection request for device: {request.IpAddress}, {request.Port}");

            if (_sdkHelper.GetConnectionStatus())
            {
                Debug.WriteLine("Already connected to the device.");
                return new ConnectResponse { Success = true };
            }

            if (await _sdkHelper.ConnectAsync(request.IpAddress, request.Port))
            {
                Debug.WriteLine($"Successfully connected to the device at {request.IpAddress}:{request.Port}");
                return new ConnectResponse { Success = true };
            }
            Debug.WriteLine($"Failed to connect to the device at {request.IpAddress}:{request.Port}");
            return new ConnectResponse { Success = false, Message = "Failed to connect to the device." };
        }

        public override async Task<DisconnectResponse> Disconnect(Empty request, ServerCallContext context)
        {
            Debug.WriteLine("Received disconnect request.");
            if (!_sdkHelper.GetConnectionStatus())
            {
                Debug.WriteLine("Not connected to any device.");
                return new DisconnectResponse { Success = false, Message = "Not connected to any device." };
            }
            await _sdkHelper.DisconnectAsync();
            Debug.WriteLine("Successfully disconnected from the device.");
            return new DisconnectResponse { Success = true };
        }

        public override async Task<GetDeviceSerialDeviceResponse> GetDeviceSerial(GetDeviceSerialRequest request, ServerCallContext context)
        {
            Debug.WriteLine("Received request for device serial number.");
            if (!_sdkHelper.GetConnectionStatus())
            {
                Debug.WriteLine("Not connected to any device.");
                return new GetDeviceSerialDeviceResponse { SerialNumber = "Not connected" };
            }

            string serialNumber = await _sdkHelper.GetDeviceSerialAsync();
            if (!string.IsNullOrEmpty(serialNumber))
            {
                Debug.WriteLine($"Device serial number: {serialNumber}");
                return new GetDeviceSerialDeviceResponse { SerialNumber = serialNumber };
            }
            else
            {
                Debug.WriteLine("Failed to retrieve device serial number.");
                return new GetDeviceSerialDeviceResponse { SerialNumber = "Error retrieving serial number" };
            }
        }
    
        public override async Task<ClearAdminResponse> ClearAdmin(Empty request, ServerCallContext context)
        {
            Debug.WriteLine("Received request to clear admin data.");
            if (!_sdkHelper.GetConnectionStatus())
            {
                Debug.WriteLine("Not connected to any device.");
                return new ClearAdminResponse { Success = false, Message = "Not connected to any device." };
            }
            bool result = await _sdkHelper.ClearAdminAsync();
            if (result)
            {
                Debug.WriteLine("Successfully cleared admin data.");
                return new ClearAdminResponse { Success = true };
            }
            else
            {
                Debug.WriteLine("Failed to clear admin data.");
                return new ClearAdminResponse { Success = false, Message = "Failed to clear admin data." };
            }
        }

        public override async Task<SyncDeviceTimeResponse> SyncDeviceTime(Empty request, ServerCallContext context)
        {
            Debug.WriteLine("Received request to sync device time");
            if (!_sdkHelper.GetConnectionStatus())
            {
                Debug.WriteLine("Not connected to any device.");
                return new SyncDeviceTimeResponse { Success = false, Message = "Not connected to any device." };
            }
            bool result = await _sdkHelper.SyncDeviceTimeAsync();

            if (result)
            {
                Debug.WriteLine("Successfully sync device time");
                return new SyncDeviceTimeResponse { Success = true };
            }
            else
            {
                Debug.WriteLine("Failed to sync device time");
                return new SyncDeviceTimeResponse { Success = false, Message = "Failed to sync device time" };
            }
        }
    }
}
