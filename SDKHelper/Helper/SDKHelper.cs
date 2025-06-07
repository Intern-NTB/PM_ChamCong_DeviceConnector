using System;
using System.Collections.Generic;
using zkemkeeper;
using Shared.Model;
using Shared.Entity;
using Shared.Interface;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;

namespace SDK.Helper
{
    public class SDKHelper
    {
        private static readonly SemaphoreSlim _deviceLock = new SemaphoreSlim(1, 1);
        public CZKEM connector = new CZKEM();
        private static bool _isConnected = false;
        private static string _deviceSerial = string.Empty;
        private static int _deviceNumber = 1;
        private readonly INhanVienRepository _nhanVienRepository;
        private readonly ILogger<SDKHelper> _logger;

        public SDKHelper(INhanVienRepository nhanVienRepository, ILogger<SDKHelper> logger)
        {
            _nhanVienRepository = nhanVienRepository;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Connect/Disconnect Methods
        public bool GetConnectionStatus()
        {
            _logger.LogDebug("Getting connection status: {Status}", _isConnected);
            return _isConnected;
        }

        public void SetConnectionStatus(bool isConnected)
        {
            _logger.LogInformation("Setting connection status to {Status}", isConnected);
            _isConnected = isConnected;
        }

        public int GetDeviceNumber()
        {
            _logger.LogDebug("Getting device number: {DeviceNumber}", _deviceNumber);
            return _deviceNumber;
        }

        public void SetDeviceNumber(int deviceNumber)
        {
            _logger.LogInformation("Setting device number to {DeviceNumber}", deviceNumber);
            _deviceNumber = deviceNumber;
        }

        public async Task<string> GetDeviceSerialAsync()
        {
            try
            {
                if (!GetConnectionStatus())
                {
                    _logger.LogWarning("Attempted to get device serial while not connected");
                    return string.Empty;
                }
                
                return await Task.Run(() =>
                {
                    if (connector.GetSerialNumber(GetDeviceNumber(), out _deviceSerial))
                    {
                        _logger.LogInformation("Retrieved device serial: {Serial}", _deviceSerial);
                        return _deviceSerial;
                    }
                    else
                    {
                        _logger.LogWarning("Failed to get device serial number");
                        return string.Empty;
                    }
                });
            }
            catch (Exception ex)
            {
                int errorCode = 0;
                connector.GetLastError(ref errorCode);
                _logger.LogError(ex, "Error getting device serial number ", errorCode);
                return string.Empty;
            }
        }

        public void SetDeviceSerial(string deviceSerial)
        {
            _logger.LogInformation("Setting device serial to {Serial}", deviceSerial);
            _deviceSerial = deviceSerial;
        }

        public async Task<bool> EnableDeviceAsync()
        {
            try
            {
                _logger.LogInformation("Waiting for device lock to enable device");
                await _deviceLock.WaitAsync();
                try
                {
                    _logger.LogInformation("Enabling device");
                    return await Task.Run(() => connector.EnableDevice(_deviceNumber, true));
                }
                finally
                {
                    _deviceLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enabling device");
                return false;
            }
        }

        public async Task<bool> DisableDeviceAsync()
        {
            try
            {
                _logger.LogInformation("Waiting for device lock to disable device");
                await _deviceLock.WaitAsync();
                try
                {
                    _logger.LogInformation("Disabling device");
                    return await Task.Run(() => connector.EnableDevice(_deviceNumber, false));
                }
                finally
                {
                    _deviceLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disabling device");
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                if (_isConnected)
                {
                    _logger.LogInformation("Disconnecting from device");
                    await Task.Run(() => connector.Disconnect());
                    SetConnectionStatus(false);
                    SetDeviceSerial(string.Empty);
                    _logger.LogInformation("Successfully disconnected from device");
                }
                else
                {
                    _logger.LogDebug("Disconnect called but already disconnected");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting from device");
                throw;
            }
        }

        public async Task<bool> ConnectAsync(string ipAddress, int port)
        {
            try
            {
                _logger.LogInformation("Connecting to device at {IpAddress}:{Port}", ipAddress, port);
                if (_isConnected)
                {
                    _logger.LogWarning("Already connected to a device");
                    return true; // Already connected
                }
                
                bool result = await Task.Run(() => connector.Connect_Net(ipAddress, port));
                if (result)
                {
                    SetConnectionStatus(true);
                    _logger.LogInformation("Successfully connected to device at {IpAddress}:{Port}", ipAddress, port);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Failed to connect to device at {IpAddress}:{Port}", ipAddress, port);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting to device at {IpAddress}:{Port}", ipAddress, port);
                return false;
            }
        }
        #endregion

        #region User Management Methods
            #region User Information Methods
            public async Task<List<Employee>> GetAllEmployeeAsync()
            {
                try
                {
                    _logger.LogInformation("Getting all employees from device");
                    if (!GetConnectionStatus())
                    {
                        var ex = new InvalidOperationException("Not connected to the device.");
                        _logger.LogError(ex, "Failed to get employees: not connected");
                        throw ex;
                    }

                    var employees = new List<Employee>();

                    bool readResult = await Task.Run(() => connector.ReadAllUserID(_deviceNumber));
                    if (readResult)
                    {
                        int dwEnrollNumber = 0;
                        string Name = string.Empty;
                        string Password = string.Empty;
                        int Privilege = 0;
                        bool Enabled = false;

                        // Move to the first user
                        bool hasUser = await Task.Run(() => connector.GetAllUserInfo(_deviceNumber, ref dwEnrollNumber, ref Name, ref Password, ref Privilege, ref Enabled));
                        while (hasUser)
                        {
                            var employee = new Employee
                            {
                                employeeId = dwEnrollNumber,
                                name = Name,
                                password = Password,
                                privilege = Privilege,
                                enabled = Enabled
                            };
                            employees.Add(employee);
                            _logger.LogDebug("Found employee: ID={ID}, Name={Name}", dwEnrollNumber, Name);

                            // Move to the next user
                            hasUser = await Task.Run(() => connector.GetAllUserInfo(_deviceNumber, ref dwEnrollNumber, ref Name, ref Password, ref Privilege, ref Enabled));
                        }
                    }
                    else
                    {
                        _logger.LogWarning("ReadAllUserID returned false");
                    }
                    
                    _logger.LogInformation("Retrieved {Count} employees from device", employees.Count);
                    return employees;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving employees from device");
                    throw;
                }
            }

            public async Task<Employee> GetUserAsync(int employeeId)
            {
                try
                {
                    _logger.LogInformation("Getting employee with ID: {EmployeeId}", employeeId);
                    if (!GetConnectionStatus())
                    {
                        var ex = new InvalidOperationException("Not connected to the device.");
                        _logger.LogError(ex, "Failed to get employee: not connected");
                        throw ex;
                    }
                    
                    Employee employee = new Employee();
                    string Name = string.Empty;
                    string Password = string.Empty;
                    int Privilege = 0;
                    bool Enabled = false;
                    
                    bool result = await Task.Run(() => connector.GetUserInfo(_deviceNumber, employeeId, ref Name, ref Password, ref Privilege, ref Enabled));
                    if (result)
                    {
                        employee.employeeId = employeeId;
                        employee.name = Name;
                        employee.password = Password;
                        employee.privilege = Privilege;
                        employee.enabled = Enabled;
                        _logger.LogInformation("Successfully retrieved employee with ID: {EmployeeId}, Name: {Name}", employeeId, Name);
                    }
                    else
                    {
                        _logger.LogWarning("Employee with ID {EmployeeId} not found", employeeId);
                    }
                    
                    return employee;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving employee with ID: {EmployeeId}", employeeId);
                    throw;
                }
            }

            private async Task<bool> WaitForDeviceReadyAsync(int maxRetries = 3)
            {
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        int status = 0;
                        bool isEnabled = await Task.Run(() => connector.GetDeviceStatus(_deviceNumber, 1, ref status));
                        if (isEnabled)
                        {
                            _logger.LogInformation("Device is ready");
                            return true;
                        }
                        _logger.LogWarning("Device not ready, attempt {Attempt} of {MaxRetries}", i + 1, maxRetries);
                        await Task.Delay(500); // Wait 500ms between retries
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error checking device status");
                    }
                }
                return false;
            }

            public async Task<bool> SetUserAsync(Employee employee)
            {
                try
                {
                    _logger.LogInformation("Setting user data for employee ID: {EmployeeId}, Name: {Name}", 
                        employee.employeeId, employee.name);
                        
                    if (!GetConnectionStatus())
                    {
                        var ex = new InvalidOperationException("Not connected to the device.");
                        _logger.LogError(ex, "Failed to set user: not connected");
                        throw ex;
                    }

                    // Wait for device lock before proceeding
                    await _deviceLock.WaitAsync();
                    try
                    {
                        // First disable device to ensure clean state
                        await Task.Run(() => connector.EnableDevice(_deviceNumber, false));
                        await Task.Delay(200);

                        // Enable device before setting user
                        bool enableResult = await Task.Run(() => connector.EnableDevice(_deviceNumber, true));
                        if (!enableResult)
                        {
                            _logger.LogWarning("Failed to enable device before setting user");
                            return false;
                        }

                        // Wait for device to be ready
                        if (!await WaitForDeviceReadyAsync())
                        {
                            _logger.LogWarning("Device not ready after enabling");
                            return false;
                        }

                        // Wait a short time to ensure device is ready
                        await Task.Delay(500);

                        bool result = await Task.Run(() => connector.SSR_SetUserInfo(
                            _deviceNumber, 
                            employee.employeeId.ToString(), 
                            employee.name, 
                            employee.password, 
                            employee.privilege, 
                            employee.enabled));
                            
                        if (result)
                        {
                            _logger.LogInformation("Successfully set user data for employee ID: {EmployeeId}", employee.employeeId);
                        }
                        else
                        {
                            int errorCode = 0;
                            connector.GetLastError(ref errorCode);
                            _logger.LogWarning("Failed to set user data for employee ID: {EmployeeId}, Error: {errorCode}", employee.employeeId, errorCode);
                        }

                        // Wait before refreshing data
                        await Task.Delay(200);

                        // Refresh device data
                        await Task.Run(() => connector.RefreshData(_deviceNumber));

                        // Wait before disabling
                        await Task.Delay(200);

                        // Disable device after setting user
                        await Task.Run(() => connector.EnableDevice(_deviceNumber, false));

                        return result;
                    }
                    finally
                    {
                        _deviceLock.Release();
                    }
                }
                catch (Exception ex)
                {
                    int errorCode = 0;
                    connector.GetLastError(ref errorCode);
                    _logger.LogError(ex, "Error setting user data for employee ID: {EmployeeId}, error: {errorCode}", employee.employeeId, errorCode);
                    throw;
                }
            }
        #endregion
            #region User Fingerprint Methods
            public async Task<Fingerprint> GetFingerprintAsync(int employeeId, int fingerIndex)
            {
                try
                {
                    _logger.LogInformation("Getting fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}", 
                        employeeId, fingerIndex);
                        
                    if (!GetConnectionStatus())
                    {
                        var ex = new InvalidOperationException("Not connected to the device.");
                        _logger.LogError(ex, "Failed to get fingerprint: not connected");
                        throw ex;
                    }

                    Fingerprint fingerprint = new Fingerprint();
                    string fingerData = string.Empty;
                    int tmpLength = 0;

                    bool readResult = await Task.Run(() => connector.ReadAllTemplate(_deviceNumber));
                    if (readResult)
                    {
                        bool getResult = await Task.Run(() => connector.GetUserTmpStr(_deviceNumber, employeeId, fingerIndex, ref fingerData, ref tmpLength));
                        if (getResult)
                        {
                            fingerprint.employeeId = employeeId;
                            fingerprint.fingerIndex = fingerIndex;
                            fingerprint.fingerData = fingerData;
                            fingerprint.fingerLength = tmpLength;
                            _logger.LogInformation("Successfully retrieved fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}, length: {Length}",
                                employeeId, fingerIndex, tmpLength);
                        }
                        else
                        {
                            _logger.LogWarning("Fingerprint not found for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                                employeeId, fingerIndex);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Failed to read templates from device");
                    }
                    
                    return fingerprint;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                        employeeId, fingerIndex);
                    throw;
                }
            }

            public async Task<bool> SetFingerprintAsync(Fingerprint fingerprint)
            {
                try
                {
                    _logger.LogInformation("Setting fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}", 
                        fingerprint.employeeId, fingerprint.fingerIndex);
                        
                    if (!GetConnectionStatus())
                    {
                        var ex = new InvalidOperationException("Not connected to the device.");
                        _logger.LogError(ex, "Failed to set fingerprint: not connected");
                        throw ex;
                    }
                    
                    bool result = await Task.Run(() => connector.SetUserTmpStr(
                        _deviceNumber, 
                        fingerprint.employeeId, 
                        fingerprint.fingerIndex, 
                        fingerprint.fingerData));
                        
                    if (result)
                    {
                        _logger.LogInformation("Successfully set fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                            fingerprint.employeeId, fingerprint.fingerIndex);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to set fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                            fingerprint.employeeId, fingerprint.fingerIndex);
                    }
                    
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error setting fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                        fingerprint.employeeId, fingerprint.fingerIndex);
                    throw;
                }
            }
        #endregion
        #endregion

        #region Real-time Data Methods
        public async Task<bool> RegisterRealtimeEventAsync()
        {
            try
            {
                _logger.LogInformation("Registering realtime events");
                
                if (!GetConnectionStatus())
                {
                    var ex = new InvalidOperationException("Not connected to the device.");
                    _logger.LogError(ex, "Failed to register realtime events: not connected");
                    throw ex;
                }
                
                bool result = await Task.Run(() => connector.RegEvent(_deviceNumber, 65535));
                if (result)
                {
                    connector.OnEnrollFinger += OnEnrollFingerEvent;
                    _logger.LogInformation("Successfully registered realtime events");
                    return true;
                }
                else
                {
                    _logger.LogWarning("Failed to register realtime events");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering realtime events");
                throw;
            }
        }

        public async void OnEnrollFingerEvent(int employeeId, int fingerIndex, int result, int tmpLength)
        {
            try
            {
                _logger.LogInformation("Enroll finger event received: Employee ID: {EmployeeId}, Finger Index: {FingerIndex}, Result: {Result}, Length: {Length}",
                    employeeId, fingerIndex, result, tmpLength);
                    
                if (!GetConnectionStatus())
                {
                    var ex = new InvalidOperationException("Not connected to the device.");
                    _logger.LogError(ex, "Failed to process enrollment: not connected");
                    throw ex;
                }

                if (result == 0) // Assuming 0 means success
                {
                    _logger.LogInformation("Enrollment successful, retrieving fingerprint data");
                    var fingerprint = await GetFingerprintAsync(employeeId, fingerIndex);
                    
                    if (string.IsNullOrEmpty(fingerprint.fingerData))
                    {
                        _logger.LogWarning("Retrieved empty fingerprint data for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                            employeeId, fingerIndex);
                    }
                    else
                    {
                        _logger.LogInformation("Saving fingerprint to database for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                            employeeId, fingerIndex);
                            
                        try
                        {
                            var vanTay = new NhanVienVanTay
                            {
                                MaNhanVien = employeeId,
                                ViTriNgonTay = fingerIndex,
                                DuLieuVanTay = fingerprint.fingerData
                            };
                            
                            int dbResult = await _nhanVienRepository.SetNhanVienVanTay(vanTay);
                            _logger.LogInformation("Database update result: {Result} rows affected", dbResult);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error saving fingerprint to database for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                                employeeId, fingerIndex);
                        }
                    }
                }
                else
                {
                    var ex = new Exception($"Failed to enroll fingerprint with result code: {result}");
                    _logger.LogError(ex, "Enrollment failed for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                        employeeId, fingerIndex);
                    throw ex;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in OnEnrollFingerEvent");
                // Don't rethrow in event handlers as it can crash the application
            }
        }
        #endregion
    }
}