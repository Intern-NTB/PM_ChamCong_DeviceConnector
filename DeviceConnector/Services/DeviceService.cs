using DeviceConnector.Protos;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using System.Diagnostics;
using SDK.Helper;

namespace DeviceConnector.Services
{
    public class DeviceService : Protos.DeviceConnector.DeviceConnectorBase
    {
        private readonly SDKHelperManager _sdkHelperManager;

        public DeviceService(SDKHelperManager sdkHelperManager)
        {
            _sdkHelperManager = sdkHelperManager;
        }

        private SDKHelper GetSDKHelper(ServerCallContext context)
        {
            // Get JWT token from authorization header
            var authHeader = context.GetHttpContext().Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                throw new RpcException(new Status(StatusCode.Unauthenticated, "JWT token is required"));
            }

            // Extract the token (remove "Bearer " prefix)
            var token = authHeader.Substring(7);
            
            // Use the token as client ID
            return _sdkHelperManager.GetOrCreateSDKHelper(token);
        }

        public override async Task<BaseDeviceResponse> Connect(ConnectRequest request, ServerCallContext context)
        {
            Debug.WriteLine($"Received connection request for device: {request.IpAddress}, {request.Port}");
            var sdkHelper = GetSDKHelper(context);

            if (sdkHelper.GetConnectionStatus())
            {
                Debug.WriteLine("Already connected to the device.");
                return new BaseDeviceResponse { Success = true };
            }

            if (await sdkHelper.ConnectAsync(request.IpAddress, request.Port))
            {
                Debug.WriteLine($"Successfully connected to the device at {request.IpAddress}:{request.Port}");
                return new BaseDeviceResponse { Success = true };
            }
            Debug.WriteLine($"Failed to connect to the device at {request.IpAddress}:{request.Port}");
            return new BaseDeviceResponse { Success = false, Message = "Failed to connect to the device." };
        }

        public override async Task<BaseDeviceResponse> Disconnect(Empty request, ServerCallContext context)
        {
            Debug.WriteLine("Received disconnect request.");
            var sdkHelper = GetSDKHelper(context);
            if (!sdkHelper.GetConnectionStatus())
            {
                Debug.WriteLine("Not connected to any device.");
                return new BaseDeviceResponse { Success = false, Message = "Not connected to any device." };
            }
            await sdkHelper.DisconnectAsync();
            _sdkHelperManager.RemoveSDKHelper(context.GetHttpContext().Request.Headers["Authorization"].ToString().Substring(7));
            Debug.WriteLine("Successfully disconnected from the device.");
            return new BaseDeviceResponse { Success = true };
        }

        public override async Task<GetDeviceSerialDeviceResponse> GetDeviceSerial(GetDeviceSerialRequest request, ServerCallContext context)
        {
            Debug.WriteLine("Received request for device serial number.");
            var sdkHelper = GetSDKHelper(context);
            if (!sdkHelper.GetConnectionStatus())
            {
                Debug.WriteLine("Not connected to any device.");
                return new GetDeviceSerialDeviceResponse { SerialNumber = "Not connected" };
            }

            string serialNumber = await sdkHelper.GetDeviceSerialAsync();
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
    
        public override async Task<BaseDeviceResponse> ClearAdmin(Empty request, ServerCallContext context)
        {
            Debug.WriteLine("Received request to clear admin data.");
            var sdkHelper = GetSDKHelper(context);
            if (!sdkHelper.GetConnectionStatus())
            {
                Debug.WriteLine("Not connected to any device.");
                return new BaseDeviceResponse { Success = false, Message = "Not connected to any device." };
            }
            bool result = await sdkHelper.ClearAdminAsync();
            if (result)
            {
                Debug.WriteLine("Successfully cleared admin data.");
                return new BaseDeviceResponse { Success = true };
            }
            else
            {
                Debug.WriteLine("Failed to clear admin data.");
                return new BaseDeviceResponse { Success = false, Message = "Failed to clear admin data." };
            }
        }

        public override async Task<BaseDeviceResponse> ClearGLog(Empty empty, ServerCallContext context)
        {
            Debug.WriteLine("Received request to clear GLog data.");
            var sdkHelper = GetSDKHelper(context);
            if (!sdkHelper.GetConnectionStatus())
            {
                Debug.WriteLine("Not connected to any device.");
                return new BaseDeviceResponse { Success = false, Message = "Not connected to any device." };
            }

            bool result = await sdkHelper.ClearAttendanceAsync();

            if (result)
            {
                Debug.WriteLine("Successfully cleared GLog data.");
                return new BaseDeviceResponse { Success = true };
            }
            else
            {
                Debug.WriteLine("Failed to clear GLog data.");
                return new BaseDeviceResponse { Success = false, Message = "Failed to clear GLog data." };
            }
        }

        public override async Task<BaseDeviceResponse> SyncDeviceTime(Empty request, ServerCallContext context)
        {
            Debug.WriteLine("Received request to sync device time");
            var sdkHelper = GetSDKHelper(context);
            if (!sdkHelper.GetConnectionStatus())
            {
                Debug.WriteLine("Not connected to any device.");
                return new BaseDeviceResponse { Success = false, Message = "Not connected to any device." };
            }
            bool result = await sdkHelper.SyncDeviceTimeAsync();

            if (result)
            {
                Debug.WriteLine("Successfully sync device time");
                return new BaseDeviceResponse { Success = true };
            }
            else
            {
                Debug.WriteLine("Failed to sync device time");
                return new BaseDeviceResponse { Success = false, Message = "Failed to sync device time" };
            }
        }
    }
}
