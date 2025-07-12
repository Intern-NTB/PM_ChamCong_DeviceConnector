using Microsoft.Extensions.Logging;
using Shared.Entity;
using Shared.Interface;
using Shared.Model;
using stdole;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using zkemkeeper;

namespace SDK.Helper
{
    public class DeviceState : IDisposable
    {
        public bool IsConnected { get; set; }
        public string DeviceSerial { get; set; } = string.Empty;
        public int DeviceNumber { get; set; } = 1;
        public CZKEM Connector { get; } = new CZKEM();
        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (Connector != null)
                    {
                        try
                        {
                            Connector.Disconnect();
                        }
                        catch { }
                    }
                }
                _disposed = true;
            }
        }

        ~DeviceState()
        {
            Dispose(false);
        }
    }

    public class SDKHelper : IDisposable
    {
        private readonly SemaphoreSlim _deviceLock;
        private readonly DeviceState _deviceState;
        private readonly INhanVienRepository _nhanVienRepository;
        private readonly IChamCongRepository _chamCongRepository;
        private readonly ILogger<SDKHelper> _logger;
        private bool _disposed = false;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private const int LOCK_TIMEOUT_MS = 30000;
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int RETRY_DELAY_MS = 1000;
        private const int READ_TIMEOUT_MS = 5000; 
        private const int READ_RETRY_ATTEMPTS = 2;

        public SDKHelper(INhanVienRepository nhanVienRepository, IChamCongRepository chamCongRepository, ILogger<SDKHelper> logger)
        {
            _deviceState = new DeviceState();
            _nhanVienRepository = nhanVienRepository;
            _chamCongRepository = chamCongRepository;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _deviceLock = new SemaphoreSlim(1, 1);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                    _deviceLock.Dispose();
                    _deviceState.Dispose();
                }
                _disposed = true;
            }
        }

        ~SDKHelper()
        {
            Dispose(false);
        }

        private async Task<T> ExecuteWithLockAsync<T>(Func<Task<T>> operation, bool allowRetry = false)
        {
            int attempts = 0;
            while (attempts < (allowRetry ? MAX_RETRY_ATTEMPTS : 1))
            {
                try
                {
                    _logger.LogInformation("Attempting to acquire device lock (attempt {Attempt}/{MaxAttempts})...", 
                        attempts + 1, allowRetry ? MAX_RETRY_ATTEMPTS : 1);
                    
                    if (!await _deviceLock.WaitAsync(LOCK_TIMEOUT_MS))
                    {
                        _logger.LogWarning("Failed to acquire device lock within {Timeout}ms (attempt {Attempt}/{MaxAttempts})", 
                            LOCK_TIMEOUT_MS, attempts + 1, allowRetry ? MAX_RETRY_ATTEMPTS : 1);
                        
                        if (attempts < (allowRetry ? MAX_RETRY_ATTEMPTS - 1 : 0))
                        {
                            attempts++;
                            await Task.Delay(RETRY_DELAY_MS * attempts);
                            continue;
                        }
                        throw new TimeoutException("Failed to acquire device lock");
                    }

                    _logger.LogInformation("Successfully acquired device lock");
                    try
                    {
                        return await operation();
                    }
                    finally
                    {
                        _deviceLock.Release();
                        _logger.LogInformation("Released device lock");
                    }
                }
                catch (Exception ex) when (allowRetry && attempts < MAX_RETRY_ATTEMPTS - 1)
                {
                    attempts++;
                    _logger.LogWarning(ex, "Operation failed (attempt {Attempt}/{MaxAttempts}). Retrying...", 
                        attempts, MAX_RETRY_ATTEMPTS);
                    await Task.Delay(RETRY_DELAY_MS * attempts);
                }
            }
            throw new Exception($"Operation failed after {MAX_RETRY_ATTEMPTS} attempts");
        }

        private async Task<bool> ReadAllWithTimeoutAsync(Func<bool> readOperation, string operationName)
        {
            int attempts = 0;
            while (attempts < READ_RETRY_ATTEMPTS)
            {
                try
                {
                    _logger.LogInformation("Starting {Operation} (attempt {Attempt}/{MaxAttempts})", 
                        operationName, attempts + 1, READ_RETRY_ATTEMPTS);

                    CancellationTokenSource cts = null;
                    try
                    {
                        cts = new CancellationTokenSource(READ_TIMEOUT_MS);
                        var readTask = Task.Run(() =>
                        {
                            try
                            {
                                return readOperation();
                            }
                            catch (AccessViolationException avEx)
                            {
                                _logger.LogCritical(avEx, "Access violation in {Operation}. Device connection may be corrupted.", operationName);
                                // Force disconnect to clear the corrupted state
                                try
                                {
                                    _deviceState.Connector?.Disconnect();
                                    _deviceState.IsConnected = false;
                                }
                                catch { }
                                throw; // Re-throw to be caught by outer catch block
                            }
                        });
                        var completedTask = await Task.WhenAny(readTask, Task.Delay(READ_TIMEOUT_MS, cts.Token));

                        if (completedTask == readTask)
                        {
                            bool result = await readTask;
                            if (result)
                            {
                                _logger.LogInformation("Successfully completed {Operation}", operationName);
                                return true;
                            }
                            _logger.LogWarning("Operation {Operation} returned false", operationName);
                        }
                        else
                        {
                            _logger.LogWarning("Operation {Operation} timed out after {Timeout}ms", 
                                operationName, READ_TIMEOUT_MS);
                        }
                    }
                    finally
                    {
                        cts?.Dispose();
                    }

                    attempts++;
                    if (attempts < READ_RETRY_ATTEMPTS)
                    {
                        await Task.Delay(1000 * attempts);
                    }
                }
                catch (AccessViolationException avEx)
                {
                    _logger.LogCritical(avEx, "Critical access violation in {Operation}. Device connection is corrupted.", operationName);
                    // Force disconnect to clear the corrupted state
                    try
                    {
                        _deviceState.Connector?.Disconnect();
                        _deviceState.IsConnected = false;
                    }
                    catch { }
                    throw new InvalidOperationException($"Device connection is corrupted during {operationName}. Please reconnect.", avEx);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Operation {Operation} was cancelled", operationName);
                    attempts++;
                    if (attempts < READ_RETRY_ATTEMPTS)
                    {
                        await Task.Delay(1000 * attempts);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during {Operation}: {Message}", operationName, ex.Message);
                    attempts++;
                    if (attempts < READ_RETRY_ATTEMPTS)
                    {
                        await Task.Delay(1000 * attempts);
                    }
                }
            }

            _logger.LogError("Operation {Operation} failed after {Attempts} attempts", 
                operationName, READ_RETRY_ATTEMPTS);
            return false;
        }

        #region Terminal Methods
        public async Task<bool> SyncDeviceTimeAsync()
        {
            return await ExecuteWithLockAsync<bool>(async () =>
            {
                if (!_deviceState.IsConnected)
                {
                    throw new InvalidOperationException("Not connected to the device.");
                }

                bool result = await Task.Run(() => _deviceState.Connector.SetDeviceTime(_deviceState.DeviceNumber));
                _logger.LogInformation(result ? "Sync device time successful" : "Sync device time unsuccessful");
                return result;
            }, allowRetry: true);
        }
        #endregion
        
        #region Connect/Disconnect Methods
        public bool GetConnectionStatus()
        {
            return _deviceState.IsConnected;
        }

        public void SetConnectionStatus(bool isConnected)
        {
            _deviceState.IsConnected = isConnected;
        }

        public int GetDeviceNumber()
        {
            return _deviceState.DeviceNumber;
        }

        public void SetDeviceNumber(int deviceNumber)
        {
            _deviceState.DeviceNumber = deviceNumber;
        }

        public async Task<string> GetDeviceSerialAsync()
        {
            return await ExecuteWithLockAsync<string>(async () =>
            {
                if (!_deviceState.IsConnected)
                {
                    return string.Empty;
                }

                return await Task.Run(() =>
                {
                    if (_deviceState.Connector.GetSerialNumber(_deviceState.DeviceNumber, out string serial))
                    {
                        _deviceState.DeviceSerial = serial;
                        return serial;
                    }
                    return string.Empty;
                });
            });
        }

        public void SetDeviceSerial(string deviceSerial)
        {
            _deviceState.DeviceSerial = deviceSerial;
        }

        public async Task<bool> EnableDeviceAsync()
        {
            return await ExecuteWithLockAsync<bool>(async () =>
            {
                if (!_deviceState.IsConnected)
                {
                    throw new InvalidOperationException("Not connected to the device.");
                }

                bool result = await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, true));
                if (!result)
                {
                    int errorCode = 0;
                    _deviceState.Connector.GetLastError(ref errorCode);
                    _logger.LogWarning("Failed to enable device. Error: {ErrorCode}", errorCode);
                }
                return result;
            });
        }

        public async Task<bool> DisableDeviceAsync()
        {
            return await ExecuteWithLockAsync<bool>(async () =>
            {
                if (!_deviceState.IsConnected)
                {
                    throw new InvalidOperationException("Not connected to the device.");
                }

                bool result = await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, false));
                if (!result)
                {
                    int errorCode = 0;
                    _deviceState.Connector.GetLastError(ref errorCode);
                    _logger.LogWarning("Failed to disable device. Error: {ErrorCode}", errorCode);
                }
                return result;
            });
        }

        public async Task DisconnectAsync()
        {
            await ExecuteWithLockAsync<bool>(async () =>
            {
                if (!_deviceState.IsConnected)
                {
                    _logger.LogInformation("Device already disconnected");
                    return true;
                }

                try
                {
                    _logger.LogInformation("Starting device disconnection...");

                    // Unregister events without taking another lock
                    try
                    {
                        _logger.LogInformation("Unregistering realtime events...");
                        await Task.Run(() =>
                        {
                            _deviceState.Connector.OnEnrollFingerEx -= new _IZKEMEvents_OnEnrollFingerExEventHandler(OnEnrollFingerExEvent);
                            _deviceState.Connector.OnAttTransactionEx -= new _IZKEMEvents_OnAttTransactionExEventHandler(OnAttTransactionEx);
                        });
                        _logger.LogInformation("Successfully unregistered realtime events");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error unregistering realtime events: {Message}", ex.Message);
                    }

                    // Then disconnect
                    await Task.Run(() => _deviceState.Connector.Disconnect());
                    _deviceState.IsConnected = false;
                    _deviceState.DeviceSerial = string.Empty;
                    _deviceState.DeviceNumber = 1;
                    
                    _logger.LogInformation("Successfully disconnected from device");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during device disconnection: {Message}", ex.Message);
                    throw;
                }
            });
        }

        public async Task<bool> ConnectAsync(string ipAddress, int port, bool isRegRealTime = false)
        {
            return await ExecuteWithLockAsync<bool>(async () =>
            {
                if (_deviceState.IsConnected)
                {
                    _logger.LogInformation("Device already connected");
                    return true;
                }

                _logger.LogInformation("Attempting to connect to device at {IP}:{Port}", ipAddress, port);
                bool result = await Task.Run(() => _deviceState.Connector.Connect_Net(ipAddress, port));
                if (result)
                {
                    _logger.LogInformation("Successfully connected to device");
                    _deviceState.IsConnected = true;
                    _deviceState.DeviceNumber = 1;

                    if (isRegRealTime)
                    {
                        // Register realtime events without taking another lock
                        try
                        {
                            _logger.LogInformation("Registering realtime events...");
                            bool registerResult = await Task.Run(() => _deviceState.Connector.RegEvent(_deviceState.DeviceNumber, 65535));
                            if (registerResult)
                            {
                                _deviceState.Connector.OnEnrollFingerEx += new _IZKEMEvents_OnEnrollFingerExEventHandler(OnEnrollFingerExEvent);
                                _deviceState.Connector.OnAttTransactionEx += new _IZKEMEvents_OnAttTransactionExEventHandler(OnAttTransactionEx);
                                _logger.LogInformation("Successfully registered realtime events");
                            }
                            else
                            {
                                _logger.LogWarning("Failed to register realtime events, but connection is still active");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error registering realtime events: {Message}", ex.Message);
                        }
                    }
                }
                else
                {
                    _logger.LogError("Failed to connect to device at {IP}:{Port}", ipAddress, port);
                }
                return result;
            }, allowRetry: true);
        }
        #endregion

        #region User Management Methods
            #region User Information Methods
            public async Task<List<Employee>> GetAllEmployeeAsync()
            {
                return await ExecuteWithLockAsync<List<Employee>>(async () =>
                {
                    if (!_deviceState.IsConnected)
                    {
                        throw new InvalidOperationException("Not connected to the device.");
                    }

                    // Add additional connection validation before making SDK calls
                    if (_deviceState.Connector == null)
                    {
                        throw new InvalidOperationException("Device connector is null.");
                    }

                    var employees = new List<Employee>();
                    
                    // Wrap the SDK call in additional error handling
                    bool readResult = false;
                    try
                    {
                        readResult = await ReadAllWithTimeoutAsync(
                            () => _deviceState.Connector.ReadAllUserID(_deviceState.DeviceNumber),
                            "ReadAllUserID");
                    }
                    catch (AccessViolationException avEx)
                    {
                        _logger.LogCritical(avEx, "Access violation occurred while reading user IDs. This may indicate a corrupted connection or device issue.");
                        // Force disconnect to clear the corrupted state
                        try
                        {
                            _deviceState.Connector.Disconnect();
                            _deviceState.IsConnected = false;
                        }
                        catch { }
                        throw new InvalidOperationException("Device connection is corrupted. Please reconnect.", avEx);
                    }
                    catch (Exception sdkEx)
                    {
                        _logger.LogError(sdkEx, "SDK error occurred while reading user IDs.");
                        throw;
                    }

                    if (readResult)
                    {
                        string dwEnrollNumber = "";
                        string Name = string.Empty;
                        string Password = string.Empty;
                        int Privilege = 0;
                        bool Enabled = false;

                        bool hasUser = false;
                        try
                        {
                            hasUser = await Task.Run(() => _deviceState.Connector.SSR_GetAllUserInfo(
                                _deviceState.DeviceNumber, 
                                out dwEnrollNumber, 
                                out Name, 
                                out Password, 
                                out Privilege, 
                                out Enabled));
                        }
                        catch (AccessViolationException avEx)
                        {
                            _logger.LogCritical(avEx, "Access violation occurred while reading user info. This may indicate a corrupted connection or device issue.");
                            // Force disconnect to clear the corrupted state
                            try
                            {
                                _deviceState.Connector.Disconnect();
                                _deviceState.IsConnected = false;
                            }
                            catch { }
                            throw new InvalidOperationException("Device connection is corrupted. Please reconnect.", avEx);
                        }

                        while (hasUser)
                        {
                            try
                            {
                                // Clean up the name by removing null characters and trimming whitespace
                                string cleanName = new string(Name.Where(c => !char.IsControl(c)).ToArray()).Trim();
                                employees.Add(new Employee
                                {
                                    employeeId = Int32.Parse(dwEnrollNumber),
                                    name = cleanName,
                                    password = Password,
                                    privilege = Privilege,
                                    enabled = Enabled
                                });

                                hasUser = await Task.Run(() => _deviceState.Connector.SSR_GetAllUserInfo(
                                    _deviceState.DeviceNumber, 
                                    out dwEnrollNumber, 
                                    out Name, 
                                    out Password, 
                                    out Privilege, 
                                    out Enabled));
                            }
                            catch (AccessViolationException avEx)
                            {
                                _logger.LogCritical(avEx, "Access violation occurred while reading user info in loop. This may indicate a corrupted connection or device issue.");
                                // Force disconnect to clear the corrupted state
                                try
                                {
                                    _deviceState.Connector.Disconnect();
                                    _deviceState.IsConnected = false;
                                }
                                catch { }
                                throw new InvalidOperationException("Device connection is corrupted. Please reconnect.", avEx);
                            }
                            catch (Exception loopEx)
                            {
                                _logger.LogError(loopEx, "Error processing user data in loop. Stopping user enumeration.");
                                break; // Exit the loop to prevent infinite errors
                            }
                        }
                    }
                    
                    return employees;
                }, allowRetry: true);
            }
            public async Task<Employee> GetUserAsync(int employeeId)
            {
                return await ExecuteWithLockAsync<Employee>(async () =>
                {
                    if (!_deviceState.IsConnected)
                    {
                        throw new InvalidOperationException("Not connected to the device.");
                    }

                    string Name = string.Empty;
                    string Password = string.Empty;
                    int Privilege = 0;
                    bool Enabled = false;

                    bool result = await Task.Run(() => _deviceState.Connector.SSR_GetUserInfo(
                        _deviceState.DeviceNumber,
                        employeeId.ToString(),
                        out Name,
                        out Password,
                        out Privilege,
                        out Enabled));

                    if (!result)
                    {
                        return null;
                    }

                    return new Employee
                    {
                        employeeId = employeeId,
                        name = Name,
                        password = Password,
                        privilege = Privilege,
                        enabled = Enabled
                    };
                });
            }
            public async Task<bool> SetUserAsync(Employee employee)
            {
                return await ExecuteWithLockAsync<bool>(async () =>
                {
                    if (!_deviceState.IsConnected)
                    {
                        throw new InvalidOperationException("Not connected to the device.");
                    }

                    if (!await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, true)))
                    {
                        return false;
                    }

                    try
                    {
                        int privilege = Math.Max(0, Math.Min(3, employee.privilege));
                        bool enabled = true;

                        bool result = await Task.Run(() => _deviceState.Connector.SSR_SetUserInfo(
                            _deviceState.DeviceNumber,
                            employee.employeeId.ToString(),
                            employee.name,
                            employee.password,
                            privilege,
                            enabled));

                        if (!result)
                        {
                            int errorCode = 0;
                            _deviceState.Connector.GetLastError(ref errorCode);
                            _logger.LogWarning("Failed to set user data. Error: {ErrorCode}", errorCode);
                        }

                        return result;
                    }
                    finally
                    {
                        await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, false));
                    }
                });
            }
            public async Task<bool> BatchSetUserAsync(List<Employee> employees)
            {
                return await ExecuteWithLockAsync<bool>(async () =>
                {
                    if (!_deviceState.IsConnected || !employees.Any())
                    {
                        return false;
                    }

                    if (!await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, true)))
                    {
                        return false;
                    }

                    try
                    {
                        if (!await Task.Run(() => _deviceState.Connector.BeginBatchUpdate(_deviceState.DeviceNumber, 1)))
                        {
                            return false;
                        }

                        foreach (var employee in employees)
                        {
                            if (employee.employeeId <= 0 || string.IsNullOrWhiteSpace(employee.name))
                            {
                                continue;
                            }

                            int privilege = Math.Max(0, Math.Min(3, employee.privilege));
                            bool enabled = true;

                            if (!_deviceState.Connector.SSR_SetUserInfo(
                                _deviceState.DeviceNumber,
                                employee.employeeId.ToString(),
                                employee.name,
                                employee.password ?? "",
                                privilege,
                                enabled))
                            {
                                int errorCode = 0;
                                _deviceState.Connector.GetLastError(ref errorCode);
                                _logger.LogWarning("Failed to add user {EmployeeId} to batch. Error: {ErrorCode}",
                                    employee.employeeId, errorCode);
                            }
                        }

                        bool batchResult = await Task.Run(() => _deviceState.Connector.BatchUpdate(_deviceState.DeviceNumber));
                        if (batchResult)
                        {
                            await Task.Run(() => _deviceState.Connector.RefreshData(_deviceState.DeviceNumber));
                        }
                        return batchResult;
                    }
                    finally
                    {
                        await Task.Run(() => _deviceState.Connector.CancelBatchUpdate(_deviceState.DeviceNumber));
                        await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, false));
                    }
                });
            }
            public async Task<bool> DeleteUserAsync(int employeeId)
            {
                return await ExecuteWithLockAsync<bool>(async () =>
                {
                    if (!_deviceState.IsConnected)
                    {
                        throw new InvalidOperationException("Not connected to the device.");
                    }

                    if (!await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, true)))
                    {
                        return false;
                    }

                    try
                    {
                        bool result = await Task.Run(() => _deviceState.Connector.SSR_DeleteEnrollData(
                            _deviceState.DeviceNumber,
                            employeeId.ToString(),
                            12));

                        if (!result)
                        {
                            int errorCode = 0;
                            _deviceState.Connector.GetLastError(ref errorCode);
                            _logger.LogWarning("Failed to delete user. Error: {ErrorCode}", errorCode);
                        }

                        return result;
                    }
                    finally
                    {
                        await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, false));
                    }
                });
            }
            #endregion
            #region User Fingerprint Methods
            public async Task<Fingerprint> GetFingerprintAsync(string employeeId, int fingerIndex)
            {
                return await ExecuteWithLockAsync<Fingerprint>(async () =>
                {
                    if (!_deviceState.IsConnected)
                    {
                        throw new InvalidOperationException("Not connected to the device.");
                    }

                    bool readResult = await Task.Run(() => _deviceState.Connector.ReadAllTemplate(_deviceState.DeviceNumber));
                    if (!readResult)
                    {
                        return null;
                    }

                    string fingerData = string.Empty;
                    int tmpLength = 0;

                    bool getResult = await Task.Run(() => _deviceState.Connector.SSR_GetUserTmpStr(
                        _deviceState.DeviceNumber,
                        employeeId,
                        fingerIndex,
                        out fingerData,
                        out tmpLength));

                    if (!getResult || string.IsNullOrEmpty(fingerData))
                    {
                        return null;
                    }

                    return new Fingerprint
                    {
                        employeeId = Int32.Parse(employeeId),
                        fingerIndex = fingerIndex,
                        fingerData = fingerData,
                        fingerLength = tmpLength
                    };
                });
            }
            public async Task<(bool Success, int TotalFound, int SavedCount)> GetAllFingerprintsAsync()
            {
                try
                {
                    _logger.LogInformation("Starting to get all fingerprints...");
                    if (!_deviceState.IsConnected)
                    {
                        _logger.LogError("Device is not connected");
                        throw new InvalidOperationException("Not connected to the device.");
                    }

                    // Lấy danh sách nhân viên trước khi acquire lock
                    var employees = await GetAllEmployeeAsync();
                    if (employees == null || !employees.Any())
                    {
                        _logger.LogWarning("No employees found on device");
                        return (false, 0, 0);
                    }
                    _logger.LogInformation("Found {Count} employees on device", employees.Count);

                    return await ExecuteWithLockAsync<(bool Success, int TotalFound, int SavedCount)>(async () =>
                    {
                        var existingFingerprints = await _nhanVienRepository.GetAllNhanVienVanTay();
                        var existingFingerprintDict = existingFingerprints
                            .ToDictionary(
                                f => (f.MaNhanVien, f.ViTriNgonTay),
                                f => f.DuLieuVanTay
                            );
                        _logger.LogInformation("Retrieved {Count} existing fingerprints from database", existingFingerprintDict.Count);

                        int totalFingerprints = 0;
                        int savedFingerprints = 0;
                        var newFingerprints = new List<NhanVienVanTay>();
                        const int BATCH_SIZE = 10;

                        foreach (var employee in employees)
                        {
                            _logger.LogInformation("Processing fingerprints for employee {EmployeeId}", employee.employeeId);
                            
                            bool readResult = await ReadAllWithTimeoutAsync(
                                () => _deviceState.Connector.ReadAllTemplate(_deviceState.DeviceNumber),
                                "ReadAllTemplate");

                            if (!readResult) continue;

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
                                    var key = (employee.employeeId, fingerIndex);

                                    if (!existingFingerprintDict.TryGetValue(key, out var existingData) || existingData != fingerData)
                                    {
                                        _logger.LogInformation("New or updated fingerprint found for employee {EmployeeId}, finger {FingerIndex}", 
                                            employee.employeeId, fingerIndex);
                                        newFingerprints.Add(new NhanVienVanTay
                                        {
                                            MaNhanVien = employee.employeeId,
                                            ViTriNgonTay = fingerIndex,
                                            DuLieuVanTay = fingerData
                                        });

                                        if (newFingerprints.Count >= BATCH_SIZE)
                                        {
                                            try
                                            {
                                                _logger.LogInformation("Saving batch of {Count} fingerprints", newFingerprints.Count);
                                                int processedCount = await _nhanVienRepository.BatchSetNhanVienVanTay(newFingerprints);
                                                savedFingerprints += processedCount;
                                                _logger.LogInformation("Successfully saved {Count} fingerprints", processedCount);
                                                newFingerprints.Clear();
                                            }
                                            catch (Exception ex)
                                            {
                                                _logger.LogError(ex, "Error batch saving fingerprints to database");
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (newFingerprints.Any())
                        {
                            try
                            {
                                _logger.LogInformation("Saving remaining {Count} fingerprints", newFingerprints.Count);
                                int processedCount = await _nhanVienRepository.BatchSetNhanVienVanTay(newFingerprints);
                                savedFingerprints += processedCount;
                                _logger.LogInformation("Successfully saved {Count} remaining fingerprints", processedCount);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error saving remaining fingerprints to database");
                            }
                        }

                        _logger.LogInformation("Fingerprint sync completed. Total found: {Total}, Saved: {Saved}", 
                            totalFingerprints, savedFingerprints);
                        return (totalFingerprints > 0, totalFingerprints, savedFingerprints);
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting all fingerprints");
                    throw;
                }
            }
            public async Task<bool> SetFingerprintAsync(Fingerprint fingerprint)
            {
                return await ExecuteWithLockAsync<bool>(async () =>
                {
                    if (!_deviceState.IsConnected)
                    {
                        throw new InvalidOperationException("Not connected to the device.");
                    }

                    if (!await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, true)))
                    {
                        return false;
                    }

                    try
                    {
                        bool result = await Task.Run(() => _deviceState.Connector.SetUserTmpExStr(
                            _deviceState.DeviceNumber,
                            fingerprint.employeeId.ToString(),
                            fingerprint.fingerIndex,
                            1,
                            fingerprint.fingerData));

                        if (!result)
                        {
                            int errorCode = 0;
                            _deviceState.Connector.GetLastError(ref errorCode);
                            _logger.LogWarning("Failed to set fingerprint. Error: {ErrorCode}", errorCode);
                        }

                        return result;
                    }
                    finally
                    {
                        await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, false));
                    }
                });
            }
            public async Task<(bool Success, int SuccessCount, int FailureCount)> BatchSetFingerprintsAsync(List<Fingerprint> fingerprints)
            {
                return await ExecuteWithLockAsync<(bool Success, int SuccessCount, int FailureCount)>(async () =>
                {
                    if (!_deviceState.IsConnected || !fingerprints.Any())
                    {
                        return (false, 0, 0);
                    }

                    if (!await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, true)))
                    {
                        return (false, 0, 0);
                    }

                    int successCount = 0;
                    int failureCount = 0;

                    try
                    {
                        if (!await Task.Run(() => _deviceState.Connector.BeginBatchUpdate(_deviceState.DeviceNumber, 2)))
                        {
                            return (false, 0, 0);
                        }

                        foreach (var fingerprint in fingerprints)
                        {
                            if (string.IsNullOrEmpty(fingerprint.fingerData) ||
                                fingerprint.fingerIndex < 0 ||
                                fingerprint.fingerIndex > 9)
                            {
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
                                _logger.LogWarning("Failed to add fingerprint to batch. Error: {ErrorCode}", errorCode);
                                failureCount++;
                            }
                            else
                            {
                                successCount++;
                            }
                        }

                        bool batchResult = await Task.Run(() => _deviceState.Connector.BatchUpdate(_deviceState.DeviceNumber));
                        if (batchResult)
                        {
                            await Task.Run(() => _deviceState.Connector.RefreshData(_deviceState.DeviceNumber));
                        }
                        else
                        {
                            failureCount += successCount;
                            successCount = 0;
                        }

                        return (batchResult, successCount, failureCount);
                    }
                    finally
                    {
                        await Task.Run(() => _deviceState.Connector.CancelBatchUpdate(_deviceState.DeviceNumber));
                        await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, false));
                    }
                });
            }
            public async Task<bool> DeleteFingerprintAsync(int employeeId, int fingerIndex)
            {
                return await ExecuteWithLockAsync<bool>(async () =>
                {
                    if (!_deviceState.IsConnected)
                    {
                        throw new InvalidOperationException("Not connected to the device.");
                    }

                    if (fingerIndex < 0 || fingerIndex > 9)
                    {
                        _logger.LogWarning("Invalid finger index {FingerIndex} for employee {EmployeeId}", 
                            fingerIndex, employeeId);
                        return false;
                    }

                    if (!await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, true)))
                    {
                        return false;
                    }

                    try
                    {
                        _logger.LogInformation("Deleting fingerprint for employee {EmployeeId}, finger {FingerIndex}", 
                            employeeId, fingerIndex);

                        bool result = await Task.Run(() => _deviceState.Connector.SSR_DelUserTmp(
                            _deviceState.DeviceNumber,
                            employeeId.ToString(),
                            fingerIndex));

                        if (!result)
                        {
                            int errorCode = 0;
                            _deviceState.Connector.GetLastError(ref errorCode);
                            _logger.LogWarning("Failed to delete fingerprint. Error: {ErrorCode}", errorCode);
                        }
                        else
                        {
                            _logger.LogInformation("Successfully deleted fingerprint for employee {EmployeeId}, finger {FingerIndex}", 
                                employeeId, fingerIndex);

                            // Xóa vân tay khỏi database
                            try
                            {
                                await _nhanVienRepository.DeleteNhanVienVanTay(employeeId, fingerIndex);
                                _logger.LogInformation("Successfully deleted fingerprint from database");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error deleting fingerprint from database: {Message}", ex.Message);
                            }
                        }

                        return result;
                    }
                    finally
                    {
                        await Task.Run(() => _deviceState.Connector.EnableDevice(_deviceState.DeviceNumber, false));
                    }
                });
            }
            public async Task<(bool Success, int FingerprintsFound, int FingerprintsSaved)> GetAllFingerprintsForEmployeeAsync(int employeeId)
            {
                return await ExecuteWithLockAsync<(bool Success, int FingerprintsFound, int FingerprintsSaved)>(async () =>
                {
                    _logger.LogInformation("Retrieving all fingerprints for employee ID: {EmployeeId}", employeeId);
                    
                    if (!_deviceState.IsConnected)
                    {
                        _logger.LogError("Cannot retrieve fingerprints: device not connected");
                        throw new InvalidOperationException("Not connected to the device.");
                    }
                    
                    // Read all fingerprint templates from the device
                    bool readResult = await ReadAllWithTimeoutAsync(
                        () => _deviceState.Connector.ReadAllTemplate(_deviceState.DeviceNumber),
                        "ReadAllTemplate");
                        
                    if (!readResult)
                    {
                        _logger.LogWarning("Failed to read templates from device for employee {EmployeeId}", employeeId);
                        return (false, 0, 0);
                    }
                    
                    // Get existing fingerprints from the database for this employee
                    var existingFingerprints = await _nhanVienRepository.GetNhanVienVanTay(employeeId);
                    var existingFingerprintDict = existingFingerprints?
                        .ToDictionary(f => f.ViTriNgonTay, f => f.DuLieuVanTay) ?? new Dictionary<int, string>();
                        
                    _logger.LogInformation("Found {Count} existing fingerprints in database for employee {EmployeeId}", 
                        existingFingerprintDict.Count, employeeId);
                    
                    int fingerprintsFound = 0;
                    int fingerprintsSaved = 0;
                    var newFingerprints = new List<NhanVienVanTay>();
                    
                    // Check each possible finger position (0-9)
                    for (int fingerIndex = 0; fingerIndex <= 9; fingerIndex++)
                    {
                        string fingerData = string.Empty;
                        int tmpLength = 0;
                        
                        try
                        {
                            bool getResult = await Task.Run(() => _deviceState.Connector.SSR_GetUserTmpStr(
                                _deviceState.DeviceNumber,
                                employeeId.ToString(),
                                fingerIndex,
                                out fingerData,
                                out tmpLength));
                                
                            if (getResult && !string.IsNullOrEmpty(fingerData))
                            {
                                fingerprintsFound++;
                                _logger.LogInformation("Found fingerprint for employee {EmployeeId}, finger index {FingerIndex}", 
                                    employeeId, fingerIndex);
                                
                                // Check if this fingerprint needs to be saved (new or updated)
                                if (!existingFingerprintDict.TryGetValue(fingerIndex, out var existingData) || 
                                    existingData != fingerData)
                                {
                                    var vanTay = new NhanVienVanTay
                                    {
                                        MaNhanVien = employeeId,
                                        ViTriNgonTay = fingerIndex,
                                        DuLieuVanTay = fingerData
                                    };
                                    
                                    newFingerprints.Add(vanTay);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error retrieving fingerprint for employee {EmployeeId}, finger {FingerIndex}", 
                                employeeId, fingerIndex);
                        }
                    }
                    
                    // Save any new or updated fingerprints to the database
                    if (newFingerprints.Any())
                    {
                        try
                        {
                            _logger.LogInformation("Saving {Count} new/updated fingerprints for employee {EmployeeId}", 
                                newFingerprints.Count, employeeId);
                                
                            foreach (var fingerprint in newFingerprints)
                            {
                                int result = await _nhanVienRepository.SetNhanVienVanTay(fingerprint);
                                if (result == - 1)
                                {
                                    fingerprintsSaved++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error saving fingerprints to database for employee {EmployeeId}", employeeId);
                        }
                    }
                    
                    _logger.LogInformation("Fingerprint sync completed for employee {EmployeeId}. Found: {Found}, Saved: {Saved}", 
                        employeeId, fingerprintsFound, fingerprintsSaved);
                        
                    return (fingerprintsFound > 0, fingerprintsFound, fingerprintsSaved);
                });
            }
        #endregion
            #region User Record Methods
            public async Task<bool> SyncAttendanceAsync()
            {
                return await ExecuteWithLockAsync<bool>(async () =>
                {
                    try
                    {
                        bool readResult = await ReadAllWithTimeoutAsync(() => _deviceState.Connector.ReadGeneralLogData(_deviceState.DeviceNumber), "ReadGeneralLogData");
                        if (!readResult)
                        {
                            _logger.LogError("Failed to read general log data from device");
                            return false;
                        }

                        string dwEnrollNumber = "";
                        int dwVerifyMode = 0;
                        int dwInOutMode = 0;
                        int dwYear = 0;
                        int dwMonth = 0;
                        int dwDay = 0;
                        int dwHour = 0;
                        int dwMinute = 0;
                        int dwSecond = 0;
                        int dwWorkcode = 0;

                        bool hasRecord = await Task.Run(() => _deviceState.Connector.SSR_GetGeneralLogData(
                            _deviceState.Connector.MachineNumber,
                            out dwEnrollNumber,
                            out dwVerifyMode,
                            out dwInOutMode,
                            out dwYear,
                            out dwMonth,
                            out dwDay,
                            out dwHour,
                            out dwMinute,
                            out dwSecond,
                            ref dwWorkcode
                            ));

                        bool finalResult = false;
                        while (hasRecord)
                        {
                            var attendanceTime = new DateTime(dwYear, dwMonth, dwDay, dwHour, dwMinute, dwSecond);
                            _logger.LogInformation("Collecting attendance record for employee {EnrollNumber} at {Time}",
                                dwEnrollNumber, attendanceTime);
                            try
                            {
                                await _chamCongRepository.SetChamCong(dwEnrollNumber, attendanceTime);
                                _logger.LogInformation("Successfully recorded attendance for employee {EnrollNumber} at {Time}",
                                dwEnrollNumber, attendanceTime);
                                finalResult = true;
                            }
                            catch (Exception ex)
                            {
                                // Kiểm tra xem có phải là lỗi SQL và có phải lỗi trùng lặp không
                                if (ex is Microsoft.Data.SqlClient.SqlException sqlEx &&
                                    sqlEx.Message.Contains("Dữ liệu quét vân tay tại thời điểm"))
                                {
                                    _logger.LogWarning("Skipped duplicate attendance record for employee {EnrollNumber} at {Time}. Reason: {ErrorMessage}",
                                        dwEnrollNumber, attendanceTime, sqlEx.Message);
                                    finalResult = true;
                                }
                                else
                                {
                                    throw ex;
                                }
                            }

                            hasRecord = await Task.Run(() => _deviceState.Connector.SSR_GetGeneralLogData(
                                _deviceState.Connector.MachineNumber,
                                out dwEnrollNumber,
                                out dwVerifyMode,
                                out dwInOutMode,
                                out dwYear,
                                out dwMonth,
                                out dwDay,
                                out dwHour,
                                out dwMinute,
                                out dwSecond,
                                ref dwWorkcode
                            ));
                        }

                        return finalResult;
                  
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error occurred during attendance sync");
                        return false;
                    }
                    finally
                    {
                        _deviceState.Connector.RefreshData(_deviceState.DeviceNumber);
                    }
                });
            }
            public async Task<bool> ClearAttendanceAsync()
            {
                return await ExecuteWithLockAsync<bool>(async () =>
                {
                    try
                    {
                        bool result = await Task.Run(() => _deviceState.Connector.ClearGLog(_deviceState.DeviceNumber));

                        return result;
                    }
                    catch(Exception ex)
                    {
                        _logger.LogError(ex, "Error occurred while clearing attendance records");
                        return false;
                    }
                });
            }
            #endregion
        #endregion

        #region Admin Methods

        public async Task<bool> ClearAdminAsync()
        {
            return await ExecuteWithLockAsync<bool>(async () =>
            {
                if (!_deviceState.IsConnected)
                {
                    throw new InvalidOperationException("Not connected to the device.");
                }
                bool result = await Task.Run(() => _deviceState.Connector.ClearAdministrators(_deviceState.DeviceNumber));
                if (!result)
                {
                    int errorCode = 0;
                    _deviceState.Connector.GetLastError(ref errorCode);
                    _logger.LogWarning("Failed to clear admin. Error: {ErrorCode}", errorCode);
                }
                return result;
            });
        }

        #endregion

        #region Real-time Data Methods
        /*public async Task<bool> RegisterRealtimeEventAsync()
        {
            return await ExecuteWithLockAsync<bool>(async () =>
            {
                if (!_deviceState.IsConnected)
                {
                    throw new InvalidOperationException("Not connected to the device.");
                }

                bool result = await Task.Run(() => _deviceState.Connector.RegEvent(_deviceState.DeviceNumber, 65535));
                if (result)
                {
                    _deviceState.Connector.OnEnrollFingerEx += new _IZKEMEvents_OnEnrollFingerExEventHandler(OnEnrollFingerExEvent);
                    _deviceState.Connector.OnAttTransactionEx += new _IZKEMEvents_OnAttTransactionExEventHandler(OnAttTransactionEx);
                }
                return result;
            });
        }*/

       /* public async Task<bool> UnregisterRealtimeEventAsync()
        {
            return await ExecuteWithLockAsync<bool>(async () =>
            {
                if (!_deviceState.IsConnected)
                {
                    _logger.LogWarning("Device not connected when unregistering events");
                    return false;
                }

                try
                {
                    _logger.LogInformation("Unregistering realtime events...");
                    await Task.Run(() => {
                        _deviceState.Connector.OnEnrollFingerEx -= new _IZKEMEvents_OnEnrollFingerExEventHandler(OnEnrollFingerExEvent);
                        _deviceState.Connector.OnAttTransactionEx -= new _IZKEMEvents_OnAttTransactionExEventHandler(OnAttTransactionEx);
                    });
                    _logger.LogInformation("Successfully unregistered realtime events");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error unregistering realtime events: {Message}", ex.Message);
                    return false;
                }
            });
        }*/
        
        private async void OnAttTransactionEx(string EnrollNumber, int IsInValid, int AttState, int VerifyMethod, int Year, int Month, int Day, int Hour, int Minute, int Second, int WorkCode)
        {
            try
            {
                if (!_deviceState.IsConnected)
                {
                    _logger.LogWarning("Device not connected when processing attendance");
                    return;
                }

                if (IsInValid != 0)
                {
                    _logger.LogWarning("Invalid attendance record for employee {EnrollNumber}", EnrollNumber);
                    return;
                }

                var attendanceTime = new DateTime(Year, Month, Day, Hour, Minute, Second);
                _logger.LogInformation("Processing attendance for employee {EnrollNumber} at {Time}", 
                    EnrollNumber, attendanceTime);

                int dbResult = await _chamCongRepository.SetChamCong(EnrollNumber, attendanceTime);

                if (dbResult <= 0 && dbResult != -1)
                {
                    _logger.LogError("Failed to record attendance for employee {EnrollNumber}. Result: {Result}", 
                        EnrollNumber, dbResult);
                }
                else
                {
                    _logger.LogInformation("Successfully recorded attendance for employee {EnrollNumber}", EnrollNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing attendance transaction: {Message}", ex.Message);
            }
            finally
            {
                await Task.Run(() => _deviceState.Connector.RefreshData(_deviceState.DeviceNumber));
            }
        }
        private async void OnEnrollFingerExEvent(string EnrollNumber, int FingerIndex, int ActionResult, int TemplateLength)
        {
            try
            {
                if (!_deviceState.IsConnected)
                {
                    _logger.LogWarning("Device not connected when processing enrollment");
                    return;
                }

                if (ActionResult != 0)
                {
                    _logger.LogWarning("Failed enrollment for employee {EnrollNumber}, finger {FingerIndex}. Action result: {Result}", 
                        EnrollNumber, FingerIndex, ActionResult);
                    return;
                }

                _logger.LogInformation("Processing new enrollment for employee {EnrollNumber}, finger {FingerIndex}", 
                    EnrollNumber, FingerIndex);

                var (success, totalFound, savedCount) = await GetAllFingerprintsAsync();
                if (!success)
                {
                    _logger.LogError("Failed to sync fingerprints after enrollment for employee {EnrollNumber}", 
                        EnrollNumber);
                }
                else
                {
                    _logger.LogInformation("Successfully synced fingerprints after enrollment. Found: {Total}, Saved: {Saved}", 
                        totalFound, savedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing enrollment event: {Message}", ex.Message);
            }
            finally
            {
                await Task.Run(() => _deviceState.Connector.RefreshData(_deviceState.DeviceNumber));
            }
        }
        #endregion
    }
}