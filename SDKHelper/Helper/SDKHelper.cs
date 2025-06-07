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
    public class SDKHelper
    {
        private static readonly SemaphoreSlim _deviceLock = new SemaphoreSlim(1, 1);
        private static CZKEM connector = new CZKEM();
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
                    
                    // Disable device before disconnecting
                    await Task.Run(() => connector.EnableDevice(_deviceNumber, false));
                    _logger.LogInformation("Device disabled successfully");
                    
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
                    
                    // Enable device after successful connection
                    bool enableResult = await Task.Run(() => connector.EnableDevice(_deviceNumber, true));
                    if (!enableResult)
                    {
                        _logger.LogWarning("Failed to enable device after connection");
                        return false;
                    }
                    _logger.LogInformation("Device enabled successfully");

                    bool regEvent = await Task.Run(() => RegisterRealtimeEventAsync());

                    if (!regEvent)
                    {
                        _logger.LogWarning("Failed to register realtime events after connection");
                        return false;
                    }
                    _logger.LogInformation("Realtime events registered successfully");

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
                        string dwEnrollNumber = "";
                        string Name = string.Empty;
                        string Password = string.Empty;
                        int Privilege = 0;
                        bool Enabled = false;

                        // Move to the first user
                        bool hasUser = await Task.Run(() => connector.SSR_GetAllUserInfo(_deviceNumber, out dwEnrollNumber, out Name, out Password, out Privilege, out Enabled));
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
                            hasUser = await Task.Run(() => connector.SSR_GetAllUserInfo(_deviceNumber, out dwEnrollNumber, out Name, out Password, out Privilege, out Enabled));
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
                    
                    bool result = await Task.Run(() => connector.SSR_GetUserInfo(_deviceNumber, employeeId.ToString(), out Name, out Password, out Privilege, out Enabled));
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
                        // Giả định thiết bị đã được enabled bởi ConnectAsync.
                        // Không cần enable/disable lại ở đây để tránh xung đột.

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

            public async Task<bool> BatchSetUserAsync(List<Employee> employees)
            {
                if (!GetConnectionStatus() || !employees.Any())
                {
                    _logger.LogWarning("Cannot perform batch set: Not connected or employee list is empty.");
                    return false;
                }

                _logger.LogInformation("Starting batch set user operation for {Count} employees", employees.Count);
                
                // Mỗi thao tác hàng loạt là một "giao dịch", được bảo vệ bởi lock
                await _deviceLock.WaitAsync();
                try
                {
                    // BƯỚC 1: Mở khóa thiết bị một lần duy nhất cho thao tác batch này.
                    if (!await Task.Run(() => connector.EnableDevice(_deviceNumber, true)))
                    {
                        _logger.LogError("Failed to enable device for batch operation. Aborting.");
                        return false;
                    }

                    bool batchSuccess = false;
                    try
                    {
                        // BƯỚC 2: Báo cho thiết bị biết CHUẨN BỊ nhận dữ liệu hàng loạt.
                        if (!await Task.Run(() => connector.BeginBatchUpdate(_deviceNumber, 1))) // Tham số 1: Cập nhật dữ liệu người dùng (hoặc vân tay, theo tài liệu SDK)
                        {
                            int errorCode = 0;
                            connector.GetLastError(ref errorCode);
                            _logger.LogError("Failed to begin batch update mode on the device. Error code: {errorCode}", errorCode);
                            return false;
                        }

                        _logger.LogInformation("Device is in batch update mode. Sending user data...");
                        
                        // Lặp và thêm từng người dùng vào "bộ đệm" của SDK.
                        // Ở bước này, dữ liệu CHƯA được gửi đi.
                        foreach (var employee in employees)
                        {
                            if (!connector.SSR_SetUserInfo(_deviceNumber, employee.employeeId.ToString(), employee.name, employee.password, employee.privilege, employee.enabled))
                            {
                                // Nếu có lỗi ở đây, thường là do dữ liệu đầu vào không hợp lệ (ví dụ: tên quá dài)
                                _logger.LogWarning("Could not add user {EmployeeId} to the batch.", employee.employeeId);
                            }
                        }

                        // BƯỚC 3: GỬI toàn bộ dữ liệu trong bộ đệm lên thiết bị trong MỘT LẦN.
                        if (await Task.Run(() => connector.BatchUpdate(_deviceNumber)))
                        {
                            _logger.LogInformation("Batch update successfully committed to the device.");
                            batchSuccess = true;
                        }
                        else
                        {
                            int errorCode = 0;
                            connector.GetLastError(ref errorCode);
                            _logger.LogError("Batch update commit failed. Error code: {errorCode}", errorCode);
                        }

                        // (Tùy chọn) Gọi RefreshData một lần duy nhất sau khi batch thành công.
                        if (batchSuccess)
                        {
                            await Task.Run(() => connector.RefreshData(_deviceNumber));
                        }
                    }
                    finally
                    {
                        // BƯỚC 4: Dọn dẹp và khóa lại thiết bị, dù thành công hay thất bại.
                        // CancelBatchUpdate để đảm bảo thiết bị không bị "treo" ở chế độ batch.
                        await Task.Run(() => connector.CancelBatchUpdate(_deviceNumber)); 
                        await Task.Run(() => connector.EnableDevice(_deviceNumber, false));
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
        #endregion
            #region User Fingerprint Methods
            public async Task<Fingerprint> GetFingerprintAsync(string employeeId, int fingerIndex)
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
                        bool getResult = await Task.Run(() => connector.SSR_GetUserTmpStr(_deviceNumber, employeeId, fingerIndex, out fingerData, out tmpLength));
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
            public async Task<bool> GetAllFingerprintsAsync()
            {
                try
                {
                    _logger.LogInformation("Starting to get all fingerprints from device and save to database");
                    
                    if (!GetConnectionStatus())
                    {
                        var ex = new InvalidOperationException("Not connected to the device.");
                        _logger.LogError(ex, "Failed to get fingerprints: not connected");
                        throw ex;
                    }

                    // Get all employees first
                    var employees = await GetAllEmployeeAsync();
                    if (employees == null || !employees.Any())
                    {
                        _logger.LogWarning("No employees found on device");
                        return false;
                    }

                    int totalFingerprints = 0;
                    int savedFingerprints = 0;

                    // For each employee, get their fingerprints
                    foreach (var employee in employees)
                    {
                        _logger.LogInformation("Getting fingerprints for employee ID: {EmployeeId}", employee.employeeId);
                        
                        bool readResult = await Task.Run(() => connector.ReadAllTemplate(_deviceNumber));
                        if (!readResult)
                        {
                            _logger.LogWarning("Failed to read templates for employee ID: {EmployeeId}", employee.employeeId);
                            continue;
                        }

                        // Try to get fingerprints for all possible finger indices (typically 1-10)
                        for (int fingerIndex = 1; fingerIndex <= 10; fingerIndex++)
                        {
                            string fingerData = string.Empty;
                            int tmpLength = 0;
                            
                            bool getResult = await Task.Run(() => connector.SSR_GetUserTmpStr(
                                _deviceNumber, 
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
                                    if (dbResult > 0)
                                    {
                                        savedFingerprints++;
                                        _logger.LogDebug("Saved fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                                            employee.employeeId, fingerIndex);
                                    }
                                    else
                                    {
                                        _logger.LogWarning("Failed to save fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                                            employee.employeeId, fingerIndex);
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
                    
                    return savedFingerprints > 0;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting all fingerprints from device");
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

        
            public async Task<bool> BatchSetFingerprintsAsync(List<Fingerprint> fingerprints)
            {
                if (!GetConnectionStatus() || !fingerprints.Any())
                {
                    _logger.LogWarning("Cannot perform batch set: Not connected or fingerprint list is empty.");
                    return false;
                }

                _logger.LogInformation("Starting batch set fingerprint operation for {Count} fingerprints", fingerprints.Count);
                
                await _deviceLock.WaitAsync();
                try
                {
                    // Enable device for batch operation
                    if (!await Task.Run(() => connector.EnableDevice(_deviceNumber, true)))
                    {
                        _logger.LogError("Failed to enable device for batch operation. Aborting.");
                        return false;
                    }

                    bool batchSuccess = false;
                    try
                    {
                        // Begin batch update mode
                        if (!await Task.Run(() => connector.BeginBatchUpdate(_deviceNumber, 2))) // 2 for fingerprint data
                        {
                            int errorCode = 0;
                            connector.GetLastError(ref errorCode);
                            _logger.LogError("Failed to begin batch update mode on the device. Error code: {errorCode}", errorCode);
                            return false;
                        }

                        _logger.LogInformation("Device is in batch update mode. Sending fingerprint data...");
                        
                        // Add each fingerprint to the batch
                        foreach (var fingerprint in fingerprints)
                        {
                            if (!connector.SSR_SetUserTmpStr(
                                _deviceNumber,
                                fingerprint.employeeId.ToString(),
                                fingerprint.fingerIndex,
                                fingerprint.fingerData))
                            {
                                _logger.LogWarning("Could not add fingerprint for employee {EmployeeId}, finger index {FingerIndex} to the batch.",
                                    fingerprint.employeeId, fingerprint.fingerIndex);
                            }
                        }

                        // Commit the batch update
                        if (await Task.Run(() => connector.BatchUpdate(_deviceNumber)))
                        {
                            _logger.LogInformation("Batch update successfully committed to the device.");
                            batchSuccess = true;
                        }
                        else
                        {
                            int errorCode = 0;
                            connector.GetLastError(ref errorCode);
                            _logger.LogError("Batch update commit failed. Error code: {errorCode}", errorCode);
                        }

                        // Refresh data after successful batch
                        if (batchSuccess)
                        {
                            await Task.Run(() => connector.RefreshData(_deviceNumber));
                        }
                    }
                    finally
                    {
                        // Clean up and disable device
                        await Task.Run(() => connector.CancelBatchUpdate(_deviceNumber));
                        await Task.Run(() => connector.EnableDevice(_deviceNumber, false));
                        _logger.LogInformation("Device disabled. Batch operation finished.");
                    }

                    return batchSuccess;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred during the batch set fingerprint operation.");
                    return false;
                }
                finally
                {
                    _deviceLock.Release();
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
                    connector.OnEnrollFinger += new _IZKEMEvents_OnEnrollFingerEventHandler(OnEnrollFingerEvent);
                    //connector.OnEnrollFingerEx += new _IZKEMEvents_OnEnrollFingerExEventHandler(OnEnrollFingerEvent);
                    connector.OnFinger += new _IZKEMEvents_OnFingerEventHandler(OnFingerEvent);
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
        public async void OnFingerEvent()
        {
            try
            {
                _logger.LogInformation("Finger event received");
                
                if (!GetConnectionStatus())
                {
                    var ex = new InvalidOperationException("Not connected to the device.");
                    _logger.LogError(ex, "Failed to process finger event: not connected");
                    throw ex;
                }
                // Handle finger event logic here
                // For example, you might want to log or process the finger index
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in OnFingerEvent");
            }
        }
        public async void OnEnrollFingerEvent(int EnrollNumber, int FingerIndex, int ActionResult, int TemplateLength)
        {
            try
            {
                _logger.LogInformation("Enroll finger event received: Employee ID: {EnrollNumber}, Finger Index: {FingerIndex}, Result: {ActionResult}, Length: {TemplateLength}",
                    EnrollNumber, FingerIndex, ActionResult, TemplateLength);
                    
                if (!GetConnectionStatus())
                {
                    var ex = new InvalidOperationException("Not connected to the device.");
                    _logger.LogError(ex, "Failed to process enrollment: not connected");
                    throw ex;
                }

                if (ActionResult == 0) // Assuming 0 means success
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