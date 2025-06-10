using System;
using System.Collections.Generic;
using zkemkeeper;
using Shared.Model;
using Shared.Entity;
using Shared.Interface;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace SDK.Helper
{
    public class DeviceState
    {
        public bool IsConnected { get; set; }
        public string DeviceSerial { get; set; } = string.Empty;
        public int DeviceNumber { get; set; } = 1;
        public CZKEM Connector { get; } = new CZKEM();
    }

    public class SDKHelper
    {
        private static readonly SemaphoreSlim _deviceLock = new SemaphoreSlim(1, 1);
        private readonly DeviceState _deviceState;
        private readonly INhanVienRepository _nhanVienRepository;
        private readonly IChamCongRepository _chamCongRepository;
        private readonly ILogger<SDKHelper> _logger;

        public SDKHelper(INhanVienRepository nhanVienRepository, IChamCongRepository chamCongRepository, ILogger<SDKHelper> logger)
        {
            _deviceState = new DeviceState();
            _nhanVienRepository = nhanVienRepository;
            _chamCongRepository = chamCongRepository;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        #region Terminal Methods
        public async Task<bool> SyncDeviceTimeAsync()
        {
            try
            {
                if (!_deviceState.IsConnected)
                {
                    var ex = new InvalidOperationException("Not connected to the device.");
                    _logger.LogError(ex, "Failed to sync device time");
                    throw ex;
                }
                bool result = await Task.Run(() => _deviceState.Connector.SetDeviceTime(_deviceState.DeviceNumber));

                if (result)
                {
                    _logger.LogInformation("Sync device time successfull");
                }
                else
                {
                    _logger.LogWarning("Sync device time unsuccessfull");
                }
                return result;
            }
            catch(Exception error)
            {
                throw error;
            }
        }
        #endregion
        #region Connect/Disconnect Methods
        public bool GetConnectionStatus()
        {
            _logger.LogDebug("Getting connection status: {Status}", _deviceState.IsConnected);
            return _deviceState.IsConnected;
        }

        public void SetConnectionStatus(bool isConnected)
        {
            _logger.LogInformation("Setting connection status to {Status}", isConnected);
            _deviceState.IsConnected = isConnected;
        }

        public int GetDeviceNumber()
        {
            _logger.LogDebug("Getting device number: {DeviceNumber}", _deviceState.DeviceNumber);
            return _deviceState.DeviceNumber;
        }

        public void SetDeviceNumber(int deviceNumber)
        {
            _logger.LogInformation("Setting device number to {DeviceNumber}", deviceNumber);
            _deviceState.DeviceNumber = deviceNumber;
        }

        public async Task<string> GetDeviceSerialAsync()
        {
            try
            {
                if (!_deviceState.IsConnected)
                {
                    _logger.LogWarning("Attempted to get device serial while not connected");
                    return string.Empty;
                }
                
                return await Task.Run(() =>
                {
                    if (_deviceState.Connector.GetSerialNumber(_deviceState.DeviceNumber, out string serial))
                    {
                        _deviceState.DeviceSerial = serial;
                        _logger.LogInformation("Retrieved device serial: {Serial}", serial);
                        return serial;
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
                _deviceState.Connector.GetLastError(ref errorCode);
                _logger.LogError(ex, "Error getting device serial number ", errorCode);
                return string.Empty;
            }
        }

        public void SetDeviceSerial(string deviceSerial)
        {
            _logger.LogInformation("Setting device serial to {Serial}", deviceSerial);
            _deviceState.DeviceSerial = deviceSerial;
        }

        public async Task<bool> EnableDeviceAsync()
        {
            _logger.LogInformation("Starting device enable process");
            if (!_deviceState.IsConnected)
            {
                _logger.LogError("Cannot enable device: Not connected to the device.");
                return false;
            }

            await _deviceLock.WaitAsync();
            try
            {
                bool finalResult = false;
                try
                {
                    finalResult = await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, true));
                    if (finalResult)
                    {
                        _logger.LogInformation("Successfully enabled device");
                    }
                    else
                    {
                        int errorCode = 0;
                        _deviceState.Connector.GetLastError(ref errorCode);
                        _logger.LogWarning("Failed to enable device. Error: {ErrorCode}", errorCode);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred during device enable process");
                    return false;
                }
                finally
                {
                    _logger.LogInformation("Device enable operation completed.");
                }

                return finalResult;
            }
            finally
            {
                _deviceLock.Release();
            }
        }

        public async Task<bool> DisableDeviceAsync()
        {
            _logger.LogInformation("Starting device disable process");
            if (!_deviceState.IsConnected)
            {
                _logger.LogError("Cannot disable device: Not connected to the device.");
                return false;
            }

            await _deviceLock.WaitAsync();
            try
            {
                bool finalResult = false;
                try
                {
                    finalResult = await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, false));
                    if (finalResult)
                    {
                        _logger.LogInformation("Successfully disabled device");
                    }
                    else
                    {
                        int errorCode = 0;
                        _deviceState.Connector.GetLastError(ref errorCode);
                        _logger.LogWarning("Failed to disable device. Error: {ErrorCode}", errorCode);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred during device disable process");
                    return false;
                }
                finally
                {
                    _logger.LogInformation("Device disable operation completed.");
                }

                return finalResult;
            }
            finally
            {
                _deviceLock.Release();
            }
        }

        public async Task DisconnectAsync()
        {
            _logger.LogInformation("Starting disconnection process");
            if (!_deviceState.IsConnected)
            {
                _logger.LogDebug("Disconnect called but already disconnected");
                return;
            }

            await _deviceLock.WaitAsync();
            try
            {
                try
                {
                    await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, false));
                    _logger.LogInformation("Device disabled successfully");

                    await Task.Run(() => _deviceState.Connector.Disconnect());
                    _deviceState.IsConnected = false;
                    _deviceState.DeviceSerial = string.Empty;
                    _deviceState.DeviceNumber = 1;
                    _logger.LogInformation("Successfully disconnected from device");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred during disconnection process");
                    throw;
                }
                finally
                {
                    _logger.LogInformation("Disconnection process completed.");
                }
            }
            finally
            {
                _deviceLock.Release();
            }
        }

        public async Task<bool> ConnectAsync(string ipAddress, int port)
        {
            _logger.LogInformation("Starting connection process to device at {IpAddress}:{Port}", ipAddress, port);
            if (_deviceState.IsConnected)
            {
                _logger.LogWarning("Already connected to a device");
                return true;
            }

            await _deviceLock.WaitAsync();
            try
            {
                bool finalResult = false;
                try
                {
                    bool connectResult = await Task.Run(() => _deviceState.Connector.Connect_Net(ipAddress, port));
                    if (!connectResult)
                    {
                        _logger.LogWarning("Failed to connect to device at {IpAddress}:{Port}", ipAddress, port);
                        return false;
                    }

                    _deviceState.IsConnected = true;
                    _deviceState.DeviceNumber = _deviceState.Connector.MachineNumber;
                    _logger.LogInformation("Successfully connected to device at {IpAddress}:{Port} with machine number {MachineNumber}", 
                        ipAddress, port, _deviceState.DeviceNumber);

                    if (!await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, true)))
                    {
                        _logger.LogWarning("Failed to enable device after connection");
                        await DisconnectAsync();
                        return false;
                    }
                    _logger.LogInformation("Device enabled successfully");

                    if (!await Task.Run(() => RegisterRealtimeEventAsync()))
                    {
                        _logger.LogWarning("Failed to register realtime events after connection");
                        await DisconnectAsync();
                        return false;
                    }

                    finalResult = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred during connection process to {IpAddress}:{Port}", ipAddress, port);
                    await DisconnectAsync();
                    return false;
                }
                finally
                {
                    _logger.LogInformation("Connection process completed.");
                }

                return finalResult;
            }
            finally
            {
                _deviceLock.Release();
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
                    if (!_deviceState.IsConnected)
                    {
                        var ex = new InvalidOperationException("Not connected to the device.");
                        _logger.LogError(ex, "Failed to get employees: not connected");
                        throw ex;
                    }

                    var employees = new List<Employee>();

                    bool readResult = await Task.Run(() => _deviceState.Connector.ReadAllUserID(_deviceState.DeviceNumber));
                    if (readResult)
                    {
                        string dwEnrollNumber = "";
                        string Name = string.Empty;
                        string Password = string.Empty;
                        int Privilege = 0;
                        bool Enabled = false;

                        // Move to the first user
                        bool hasUser = await Task.Run(() => _deviceState.Connector.SSR_GetAllUserInfo(_deviceState.DeviceNumber, out dwEnrollNumber, out Name, out Password, out Privilege, out Enabled));
                        while (hasUser)
                        {
                            var employee = new Employee
                            {
                                employeeId = Int32.Parse(dwEnrollNumber),
                                name = Name,
                                password = Password,
                                privilege = Privilege,
                                enabled = Enabled
                            };
                            employees.Add(employee);
                            _logger.LogDebug("Found employee: ID={ID}, Name={Name}", dwEnrollNumber, Name);

                            // Move to the next user
                            hasUser = await Task.Run(() => _deviceState.Connector.SSR_GetAllUserInfo(_deviceState.DeviceNumber, out dwEnrollNumber, out Name, out Password, out Privilege, out Enabled));
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
                    if (!_deviceState.IsConnected)
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
                    
                    bool result = await Task.Run(() => _deviceState.Connector.SSR_GetUserInfo(_deviceState.DeviceNumber, employeeId.ToString(), out Name, out Password, out Privilege, out Enabled));
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
            public async Task<bool> SetUserAsync(Employee employee)
            {
                _logger.LogInformation("Starting set user process for employee ID: {EmployeeId}, Name: {Name}", 
                    employee.employeeId, employee.name);
                    
                if (!_deviceState.IsConnected)
                {
                    _logger.LogError("Cannot set user: Not connected to the device.");
                    return false;
                }

                await _deviceLock.WaitAsync();
                try
                {
                    bool finalResult = false;
                    try
                    {
                        if (!await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, true)))
                        {
                            _logger.LogError("Failed to enable device for set user operation. Aborting.");
                            return false;
                        }

                        bool result = await Task.Run(() => _deviceState.Connector.SSR_SetUserInfo(
                            _deviceState.DeviceNumber, 
                            employee.employeeId.ToString(), 
                            employee.name, 
                            employee.password, 
                            employee.privilege, 
                            employee.enabled));
                            
                        if (result)
                        {
                            _logger.LogInformation("Successfully set user data for employee ID: {EmployeeId}", employee.employeeId);
                            finalResult = true;
                        }
                        else
                        {
                            int errorCode = 0;
                            _deviceState.Connector.GetLastError(ref errorCode);
                            _logger.LogWarning("Failed to set user data for employee ID: {EmployeeId}, Error: {errorCode}", 
                                employee.employeeId, errorCode);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "An exception occurred during set user process for employee ID: {EmployeeId}", 
                            employee.employeeId);
                        return false;
                    }
                    finally
                    {
                        await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, false));
                        _logger.LogInformation("Device disabled after set user operation.");
                    }

                    return finalResult;
                }
                finally
                {
                    _deviceLock.Release();
                }
            }
            public async Task<bool> BatchSetUserAsync(List<Employee> employees)
            {
                if (!_deviceState.IsConnected || !employees.Any())
                {
                    _logger.LogWarning("Cannot perform batch set: Not connected or employee list is empty.");
                    return false;
                }

                _logger.LogInformation("Starting batch set user operation for {Count} employees", employees.Count);
                
                await _deviceLock.WaitAsync();
                try
                {
                    if (!await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, true)))
                    {
                        _logger.LogError("Failed to enable device for batch operation. Aborting.");
                        return false;
                    }

                    bool batchSuccess = false;
                    try
                    {
                        if (!await Task.Run(() => _deviceState.Connector.BeginBatchUpdate(_deviceState.DeviceNumber, 1)))
                        {
                            int errorCode = 0;
                            _deviceState.Connector.GetLastError(ref errorCode);
                            _logger.LogError("Failed to begin batch update mode on the device. Error code: {errorCode}", errorCode);
                            return false;
                        }

                        _logger.LogInformation("Device is in batch update mode. Sending user data...");
                        
                        foreach (var employee in employees)
                        {
                            if (!_deviceState.Connector.SSR_SetUserInfo(_deviceState.DeviceNumber, employee.employeeId.ToString(), employee.name, employee.password, employee.privilege, employee.enabled))
                            {
                                _logger.LogWarning("Could not add user {EmployeeId} to the batch.", employee.employeeId);
                            }
                        }

                        if (await Task.Run(() => _deviceState.Connector.BatchUpdate(_deviceState.DeviceNumber)))
                        {
                            _logger.LogInformation("Batch update successfully committed to the device.");
                            batchSuccess = true;
                        }
                        else
                        {
                            int errorCode = 0;
                            _deviceState.Connector.GetLastError(ref errorCode);
                            _logger.LogError("Batch update commit failed. Error code: {errorCode}", errorCode);
                        }

                        if (batchSuccess)
                        {
                            await Task.Run(() => _deviceState.Connector.RefreshData(_deviceState.DeviceNumber));
                        }
                    }
                    finally
                    {
                        await Task.Run(() => _deviceState.Connector.CancelBatchUpdate(_deviceState.DeviceNumber)); 
                        await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, false));
                        _logger.LogInformation("Device disabled. Batch operation finished.");
                    }

                    return batchSuccess;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred during the batch set user operation.");
                    return false;
                }
                finally
                {
                    _deviceLock.Release();
                }
            }
            public async Task<bool> DeleteUserAsync(int employeeId)
            {
                _logger.LogInformation("Starting BULLETPROOF deletion process for user ID: {EmployeeId}", employeeId);
                if (!_deviceState.IsConnected)
                {
                    _logger.LogError("Cannot delete user: Not connected to the device.");
                    return false;
                }

                await _deviceLock.WaitAsync();
                try
                {
                    if (!await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, true)))
                    {
                        _logger.LogError("Failed to enable device for delete operation. Aborting.");
                        return false;
                    }

                    bool finalResult = false;
                    try
                    {
                        _logger.LogInformation("Deleting main user record and all associated data for user {EmployeeId} using index 12.", employeeId);
                        if (await Task.Run(() => _deviceState.Connector.SSR_DeleteEnrollData(_deviceState.DeviceNumber, employeeId.ToString(), 12)))
                        {
                            _logger.LogInformation("Successfully deleted main user record for ID: {EmployeeId}", employeeId);
                            finalResult = true;
                        }
                        else
                        {
                            int errorCode = 0;
                            _deviceState.Connector.GetLastError(ref errorCode);
                            _logger.LogWarning("Failed to delete main user record for user ID: {EmployeeId}. Error: {ErrorCode}", employeeId, errorCode);
                            finalResult = false;
                        }
                    }
                    finally
                    {
                        await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, false));
                        _logger.LogInformation("Device disabled after delete operation.");
                    }
                    return finalResult;
                }
                finally
                {
                    _deviceLock.Release();
                }
            }
            #endregion
            #region User Fingerprint Methods
            public async Task<Fingerprint> GetFingerprintAsync(string employeeId, int fingerIndex)
                {
                    try
                    {
                        _logger.LogInformation("Getting fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}", 
                            employeeId, fingerIndex);
                        
                        if (!_deviceState.IsConnected)
                        {
                            var ex = new InvalidOperationException("Not connected to the device.");
                            _logger.LogError(ex, "Failed to get fingerprint: not connected");
                            throw ex;
                        }

                        Fingerprint fingerprint = new Fingerprint();
                        string fingerData = string.Empty;
                        int tmpLength = 0;

                        bool readResult = await Task.Run(() => _deviceState.Connector.ReadAllTemplate(_deviceState.DeviceNumber));
                        if (readResult)
                        {
                            bool getResult = await Task.Run(() => _deviceState.Connector.SSR_GetUserTmpStr(_deviceState.DeviceNumber, employeeId, fingerIndex, out fingerData, out tmpLength));
                            if (getResult)
                            {
                                fingerprint.employeeId = Int32.Parse(employeeId);
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
            public async Task<(bool Success, int TotalFound, int SavedCount)> GetAllFingerprintsAsync()
            {
                try
                {
                    _logger.LogInformation("Starting to get all fingerprints from device and save to database");

                    if (!_deviceState.IsConnected)
                    {
                        var ex = new InvalidOperationException("Not connected to the device.");
                        _logger.LogError(ex, "Failed to get fingerprints: not connected");
                        throw ex;
                    }

                    var employees = await GetAllEmployeeAsync();
                    if (employees == null || !employees.Any())
                    {
                        _logger.LogWarning("No employees found on device");
                        return (false, 0, 0);
                    }

                    int totalFingerprints = 0;
                    int savedFingerprints = 0;

                    foreach (var employee in employees)
                    {
                        _logger.LogInformation("Getting fingerprints for employee ID: {EmployeeId}", employee.employeeId);

                        bool readResult = await Task.Run(() => _deviceState.Connector.ReadAllTemplate(_deviceState.DeviceNumber));
                        if (!readResult)
                        {
                            _logger.LogWarning("Failed to read templates for employee ID: {EmployeeId}", employee.employeeId);
                            continue;
                        }

                        for (int fingerIndex = 0; fingerIndex <= 9; fingerIndex++)
                        {
                            string fingerData = string.Empty;
                            int tmpLength = 0;

                            bool getResult = await Task.Run(() => _deviceState.Connector.SSR_GetUserTmpStr(
                                _deviceState.DeviceNumber,
                                employee.employeeId.ToString(),
                                fingerIndex,
                                out fingerData,
                                out tmpLength));

                            if (getResult && !string.IsNullOrEmpty(fingerData))
                            {
                                totalFingerprints++;

                                try
                                {
                                    var vanTay = new NhanVienVanTay
                                    {
                                        MaNhanVien = employee.employeeId,
                                        ViTriNgonTay = fingerIndex,
                                        DuLieuVanTay = fingerData
                                    };

                                    int dbResult = await _nhanVienRepository.SetNhanVienVanTay(vanTay);

                                    if (dbResult > 0 || dbResult == -1)
                                    {
                                        savedFingerprints++;
                                        _logger.LogDebug("Saved fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}, Result: {Result}",
                                            employee.employeeId, fingerIndex, dbResult);
                                    }
                                    else
                                    {
                                        _logger.LogWarning("Failed to save fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}, Result: {Result}",
                                            employee.employeeId, fingerIndex, dbResult);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error saving fingerprint to database for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                                        employee.employeeId, fingerIndex);
                                }
                            }
                        }
                    }

                    _logger.LogInformation("Finished getting fingerprints. Total found: {Total}, Successfully saved: {Saved}",
                        totalFingerprints, savedFingerprints);

                    return (totalFingerprints > 0, totalFingerprints, savedFingerprints);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting all fingerprints from device");
                    throw;
                }
            }
            public async Task<bool> SetFingerprintAsync(Fingerprint fingerprint)
            {
                _logger.LogInformation("Starting set fingerprint process for employee ID: {EmployeeId}, finger index: {FingerIndex}", 
                    fingerprint.employeeId, fingerprint.fingerIndex);
                    
                if (!_deviceState.IsConnected)
                {
                    _logger.LogError("Cannot set fingerprint: Not connected to the device.");
                    return false;
                }

                await _deviceLock.WaitAsync();
                try
                {
                    bool finalResult = false;
                    try
                    {
                        if (!await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, true)))
                        {
                            _logger.LogError("Failed to enable device for set fingerprint operation. Aborting.");
                            return false;
                        }

                        bool result = await Task.Run(() => _deviceState.Connector.SetUserTmpExStr(
                            _deviceState.DeviceNumber, 
                            fingerprint.employeeId.ToString(), 
                            fingerprint.fingerIndex, 
                            1,
                            fingerprint.fingerData));
                        
                        if (result)
                        {
                            _logger.LogInformation("Successfully set fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                                fingerprint.employeeId, fingerprint.fingerIndex);
                            finalResult = true;
                        }
                        else
                        {
                            int errorCode = 0;
                            _deviceState.Connector.GetLastError(ref errorCode);
                            _logger.LogWarning("Failed to set fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}, Error: {errorCode}",
                                fingerprint.employeeId, fingerprint.fingerIndex, errorCode);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "An exception occurred during set fingerprint process for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                            fingerprint.employeeId, fingerprint.fingerIndex);
                        return false;
                    }
                    finally
                    {
                        await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, false));
                        _logger.LogInformation("Device disabled after set fingerprint operation.");
                    }

                    return finalResult;
                }
                finally
                {
                    _deviceLock.Release();
                }
            }
            public async Task<(bool Success, int SuccessCount, int FailureCount)> BatchSetFingerprintsAsync(List<Fingerprint> fingerprints)
            {
                if (!_deviceState.IsConnected || !fingerprints.Any())
                {
                    _logger.LogWarning("Cannot perform batch set: Not connected or fingerprint list is empty.");
                    return (false, 0, 0);
                }

                _logger.LogInformation("Starting batch set fingerprint operation for {Count} fingerprints", fingerprints.Count);
                
                await _deviceLock.WaitAsync();
                try
                {
                    if (!await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, true)))
                    {
                        _logger.LogError("Failed to enable device for batch operation. Aborting.");
                        return (false, 0, 0);
                    }

                    int successCount = 0;
                    int failureCount = 0;
                    bool batchSuccess = false;

                    try
                    {
                        if (!await Task.Run(() => _deviceState.Connector.BeginBatchUpdate(_deviceState.DeviceNumber, 2))) // 2 for fingerprint data
                        {
                            int errorCode = 0;
                            _deviceState.Connector.GetLastError(ref errorCode);
                            _logger.LogError("Failed to begin batch update mode on the device. Error code: {errorCode}", errorCode);
                            return (false, 0, 0);
                        }

                        _logger.LogInformation("Device is in batch update mode. Sending fingerprint data...");
                        foreach (var fingerprint in fingerprints)
                        {
                            if (string.IsNullOrEmpty(fingerprint.fingerData))
                            {
                                _logger.LogWarning("Skipping fingerprint for employee {EmployeeId}, finger index {FingerIndex} - Empty fingerprint data",
                                    fingerprint.employeeId, fingerprint.fingerIndex);
                                failureCount++;
                                continue;
                            }

                            if (fingerprint.fingerIndex < 0 || fingerprint.fingerIndex > 9)
                            {
                                _logger.LogWarning("Skipping fingerprint for employee {EmployeeId}, finger index {FingerIndex} - Invalid finger index",
                                    fingerprint.employeeId, fingerprint.fingerIndex);
                                failureCount++;
                                continue;
                            }

                            bool addResult = _deviceState.Connector.SetUserTmpExStr(
                                _deviceState.DeviceNumber,
                                fingerprint.employeeId.ToString(),
                                fingerprint.fingerIndex,
                                1,
                                fingerprint.fingerData);

                            if (!addResult)
                            {
                                int errorCode = 0;
                                _deviceState.Connector.GetLastError(ref errorCode);
                                _logger.LogWarning("Could not add fingerprint for employee {EmployeeId}, finger index {FingerIndex} to the batch, error code {ErrorCode}",
                                    fingerprint.employeeId, fingerprint.fingerIndex, errorCode);

                                if (errorCode == -100)
                                {
                                    _logger.LogWarning("Fingerprint data might be invalid or corrupted for employee {EmployeeId}, finger index {FingerIndex}",
                                        fingerprint.employeeId, fingerprint.fingerIndex);
                                }
                                failureCount++;
                            }
                            else
                            {
                                _logger.LogDebug("Successfully added fingerprint for employee {EmployeeId}, finger index {FingerIndex} to batch",
                                    fingerprint.employeeId, fingerprint.fingerIndex);
                                successCount++;
                            }
                        }

                        if (await Task.Run(() => _deviceState.Connector.BatchUpdate(_deviceState.DeviceNumber)))
                        {
                            _logger.LogInformation("Batch update successfully committed to the device.");
                            batchSuccess = true;
                        }
                        else
                        {
                            int errorCode = 0;
                            _deviceState.Connector.GetLastError(ref errorCode);
                            _logger.LogError("Batch update commit failed. Error code: {errorCode}", errorCode);
                            failureCount += successCount;
                            successCount = 0;
                        }

                        if (batchSuccess)
                        {
                            await Task.Run(() => _deviceState.Connector.RefreshData(_deviceState.DeviceNumber));
                        }
                    }
                    finally
                    {
                        await Task.Run(() => _deviceState.Connector.CancelBatchUpdate(_deviceState.DeviceNumber));
                        await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, false));
                        _logger.LogInformation("Device disabled. Batch operation finished.");
                    }

                    return (batchSuccess, successCount, failureCount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred during the batch set fingerprint operation.");
                    return (false, 0, fingerprints.Count);
                }
                finally
                {
                    _deviceLock.Release();
                }
            }
            #endregion
        #endregion

        #region Admin Methods

        public async Task<bool> ClearAdminAsync()
        {
            try
            {
                if (!_deviceState.IsConnected)
                {
                    var ex = new InvalidOperationException("Not connected to the device.");
                    _logger.LogError(ex, "Failed to clear admin: not connected");
                    throw ex;
                }

                _logger.LogInformation("Clearing admin data from device {DeviceNumber}", _deviceState.DeviceNumber);

                bool result = await Task.Run(() => _deviceState.Connector.ClearAdministrators(_deviceState.DeviceNumber));

                if (result)
                {
                    _logger.LogInformation("Successfully cleared admin data from device {DeviceNumber}", _deviceState.DeviceNumber);
                    return true;
                }
                else
                {
                    int errorCode = 0;
                    _deviceState.Connector.GetLastError(ref errorCode);
                    _logger.LogError("Failed to clear admin data from device {DeviceNumber}, Error: {errorCode}", _deviceState.DeviceNumber, errorCode);
                    return false;
                }
            }
            catch (Exception error)
            {
                throw error;
            }
        }

        #endregion

        #region Real-time Data Methods
        public async Task<bool> RegisterRealtimeEventAsync() 
        {
            try
            {
                _logger.LogInformation("Registering realtime events");
                
                if (!_deviceState.IsConnected)
                {
                    var ex = new InvalidOperationException("Not connected to the device.");
                    _logger.LogError(ex, "Failed to register realtime events: not connected");
                    throw ex;
                }
                
                bool result = await Task.Run(() => _deviceState.Connector.RegEvent(_deviceState.DeviceNumber, 65535));
                if (result)
                {
                    //connector.OnEnrollFinger += new _IZKEMEvents_OnEnrollFingerEventHandler(OnEnrollFingerEvent);
                    //connector.OnEnrollFingerEx += new _IZKEMEvents_OnEnrollFingerExEventHandler(OnEnrollFingerExEvent);
                    _deviceState.Connector.OnAttTransactionEx += new _IZKEMEvents_OnAttTransactionExEventHandler(OnAttTransactionEx);
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
        private async void OnAttTransactionEx(string EnrollNumber, int IsInValid, int AttState, int VerifyMethod, int Year, int Month, int Day, int Hour, int Minute, int Second, int WorkCode)
        {
            try
            {
                _logger.LogInformation("Attendance transaction received: Employee ID: {EnrollNumber}, IsValid: {IsInValid}, State: {AttState}, Method: {VerifyMethod}, DateTime: {Year}-{Month}-{Day} {Hour}:{Minute}:{Second}, WorkCode: {WorkCode}",
                    EnrollNumber, IsInValid, AttState, VerifyMethod, Year, Month, Day, Hour, Minute, Second, WorkCode);
                
                if (!_deviceState.IsConnected)
                {
                    var ex = new InvalidOperationException("Not connected to the device.");
                    _logger.LogError(ex, "Failed to process attendance transaction: not connected");
                    throw ex;
                }

                var attendanceTime = new DateTime(Year, Month, Day, Hour, Minute, Second);

                if(IsInValid == 0)
                {
                    int dbResult = await _chamCongRepository.SetChamCong(EnrollNumber, attendanceTime);

                    if (dbResult > 0 || dbResult == -1)
                    {
                        _logger.LogInformation("Attendance recorded successfully for employee ID: {EnrollNumber} at {AttendanceTime}", EnrollNumber, attendanceTime);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to record attendance for employee ID: {EnrollNumber}, Result: {Result}", EnrollNumber, dbResult);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in OnAttTransactionEx");
            }
        }
        private async void OnEnrollFingerExEvent(string EnrollNumber, int FingerIndex, int ActionResult, int TemplateLength)
        {
            try
            {
                _logger.LogInformation("Enroll finger event received: Employee ID: {EnrollNumber}, Finger Index: {FingerIndex}, Result: {ActionResult}, Length: {TemplateLength}",
                    EnrollNumber, FingerIndex, ActionResult, TemplateLength);

                if (!_deviceState.IsConnected)
                {
                    var ex = new InvalidOperationException("Not connected to the device.");
                    _logger.LogError(ex, "Failed to process enrollment: not connected");
                    throw ex;
                }

                if (ActionResult == 0)
                {
                    _logger.LogInformation("Enrollment successful, retrieving fingerprint data");
                    var fingerprint = await GetFingerprintAsync(EnrollNumber, FingerIndex);

                    if (string.IsNullOrEmpty(fingerprint.fingerData))
                    {
                        _logger.LogWarning("Retrieved empty fingerprint data for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                            EnrollNumber, FingerIndex);
                    }
                    else
                    {
                        _logger.LogInformation("Saving fingerprint to database for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                            EnrollNumber, FingerIndex);

                        try
                        {
                            var vanTay = new NhanVienVanTay
                            {
                                MaNhanVien = int.Parse(EnrollNumber),
                                ViTriNgonTay = FingerIndex,
                                DuLieuVanTay = fingerprint.fingerData
                            };

                            int dbResult = await _nhanVienRepository.SetNhanVienVanTay(vanTay);
                            _logger.LogInformation("Database update result: {Result} rows affected", dbResult);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error saving fingerprint to database for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                                EnrollNumber, FingerIndex);
                        }
                    }
                }
                else
                {
                    var ex = new Exception($"Failed to enroll fingerprint with result code: {ActionResult}");
                    _logger.LogError(ex, "Enrollment failed for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                        EnrollNumber, FingerIndex);
                    throw ex;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in OnEnrollFingerExEvent");
            }
        }
        private async void OnEnrollFingerEvent(int EnrollNumber, int FingerIndex, int ActionResult, int TemplateLength)
        {
            try
            {
                _logger.LogInformation("Enroll finger event received: Employee ID: {EnrollNumber}, Finger Index: {FingerIndex}, Result: {ActionResult}, Length: {TemplateLength}",
                    EnrollNumber, FingerIndex, ActionResult, TemplateLength);
                    
                if (!_deviceState.IsConnected)
                {
                    var ex = new InvalidOperationException("Not connected to the device.");
                    _logger.LogError(ex, "Failed to process enrollment: not connected");
                    throw ex;
                }

                if (ActionResult == 0)
                {
                    _logger.LogInformation("Enrollment successful, retrieving fingerprint data");
                    var fingerprint = await GetFingerprintAsync(EnrollNumber.ToString(), FingerIndex);
                    
                    if (string.IsNullOrEmpty(fingerprint.fingerData))
                    {
                        _logger.LogWarning("Retrieved empty fingerprint data for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                            EnrollNumber, FingerIndex);
                    }
                    else
                    {
                        _logger.LogInformation("Saving fingerprint to database for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                            EnrollNumber, FingerIndex);
                            
                        try
                        {
                            var vanTay = new NhanVienVanTay
                            {
                                MaNhanVien = EnrollNumber,
                                ViTriNgonTay = FingerIndex,
                                DuLieuVanTay = fingerprint.fingerData
                            };
                            
                            int dbResult = await _nhanVienRepository.SetNhanVienVanTay(vanTay);
                            _logger.LogInformation("Database update result: {Result} rows affected", dbResult);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error saving fingerprint to database for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                                EnrollNumber, FingerIndex);
                        }
                    }
                }
                else
                {
                    var ex = new Exception($"Failed to enroll fingerprint with result code: {ActionResult}");
                    _logger.LogError(ex, "Enrollment failed for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                        EnrollNumber, FingerIndex);
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