using DeviceConnector.Protos;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using System.Diagnostics;
using DeviceConnector.Helper;

namespace DeviceConnector.Services
{
    public class DeviceService : DeviceConnector.Protos.DeviceConnector.DeviceConnectorBase
    {
        private readonly SDKHelper _sdkHelper;

        public DeviceService(SDKHelper sdkHelper)
        {
            _sdkHelper = sdkHelper;
        }

        public override Task<ConnectResponse> Connect(ConnectRequest request, ServerCallContext context)
        {
            Debug.WriteLine($"Received connection request for device: {request.IpAddress}, {request.Port}");

            if (_sdkHelper.GetConnectionStatus())
            {
                Debug.WriteLine("Already connected to the device.");
                return Task.FromResult(new ConnectResponse { Success = true });
            }

            if (_sdkHelper.Connect(request.IpAddress, request.Port))
            {
                Debug.WriteLine($"Successfully connected to the device at {request.IpAddress}:{request.Port}");
                return Task.FromResult(new ConnectResponse { Success = true });
            }
            else
            {
                Debug.WriteLine($"Failed to connect to the device at {request.IpAddress}:{request.Port}");
                return Task.FromResult(new ConnectResponse { Success = false, Message = "Failed to connect to the device." });
            }
        }

        public override Task<DisconnectResponse> Disconnect(Empty request, ServerCallContext context)
        {
            Debug.WriteLine("Received disconnect request.");
            if (!_sdkHelper.GetConnectionStatus())
            {
                Debug.WriteLine("Not connected to any device.");
                return Task.FromResult(new DisconnectResponse { Success = false, Message = "Not connected to any device." });
            }
            _sdkHelper.Disconnect();
            Debug.WriteLine("Successfully disconnected from the device.");
            return Task.FromResult(new DisconnectResponse { Success = true });
        }

        public override Task<GetDeviceSerialDeviceResponse> GetDeviceSerial(GetDeviceSerialRequest request, ServerCallContext context)
        {
            Debug.WriteLine("Received request for device serial number.");
            if (!_sdkHelper.GetConnectionStatus())
            {
                Debug.WriteLine("Not connected to any device.");
                return Task.FromResult(new GetDeviceSerialDeviceResponse { SerialNumber = "Not connected" });
            }

            string serialNumber = _sdkHelper.GetDeviceSerial();
            if (!string.IsNullOrEmpty(serialNumber))
            {
                Debug.WriteLine($"Device serial number: {serialNumber}");
                return Task.FromResult(new GetDeviceSerialDeviceResponse { SerialNumber = serialNumber });
            }
            else
            {
                Debug.WriteLine("Failed to retrieve device serial number.");
                return Task.FromResult(new GetDeviceSerialDeviceResponse { SerialNumber = "Error retrieving serial number" });
            }
        }
    }
}
