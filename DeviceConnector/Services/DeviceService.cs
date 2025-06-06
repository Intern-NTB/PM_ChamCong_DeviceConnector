using DeviceConnector.Protos;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using System.Diagnostics;
using zkemkeeper;

namespace DeviceConnector.Services
{
    public class DeviceService : DeviceConnector.Protos.DeviceConnector.DeviceConnectorBase
    {
        private readonly CZKEM _czkemClass;
        private bool _isConnected = false;
        public DeviceService()
        {
            _czkemClass = new CZKEM();
        }

        public override Task<ConnectResponse> Connect(ConnectRequest request, ServerCallContext context)
        {
            Debug.WriteLine($"Received connection request for device: {request.IpAddress}, {request.Port}");
            // Here you would implement the logic to connect to the device.  
            if (_isConnected)
            {
                Debug.WriteLine("Already connected to the device.");
                return Task.FromResult(new ConnectResponse { Success = true });
            }

            if (_czkemClass.Connect_Net(request.IpAddress, request.Port))
            {
                Debug.WriteLine($"Successfully connected to the device at {request.IpAddress}:{request.Port}");
                _isConnected = true;
            }
            else
            {
                Debug.WriteLine($"Failed to connect to the device at {request.IpAddress}:{request.Port}");
                return Task.FromResult(new ConnectResponse { Success = false, Message = "Failed to connect to the device." });

            }
            return Task.FromResult(new ConnectResponse { Success = true });
        }

        public override Task<DisconnectResponse> Disconnect(Empty request, ServerCallContext context)
        {
            Debug.WriteLine("Received disconnect request.");
            if (!_isConnected)
            {
                Debug.WriteLine("Not connected to any device.");
                return Task.FromResult(new DisconnectResponse { Success = false, Message = "Not connected to any device." });
            }
            _czkemClass.Disconnect();
            _isConnected = false;
            Debug.WriteLine("Successfully disconnected from the device.");
            return Task.FromResult(new DisconnectResponse { Success = true });
        }

        public override Task<GetDeviceSerialDeviceResponse> GetDeviceSerial(GetDeviceSerialRequest request, ServerCallContext context)
        {
            Debug.WriteLine("Received request for device serial number.");
            if (!_isConnected)
            {
                Debug.WriteLine("Not connected to any device.");
                return Task.FromResult(new GetDeviceSerialDeviceResponse { SerialNumber = "Not connected" });
            }
            
            if (_czkemClass.GetSerialNumber(request.DeviceNumber, out string serialNumber))
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
