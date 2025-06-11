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
        private static readonly SemaphoreSlim _deviceLock = new SemaphoreSlim(1, 1);
        private readonly DeviceState _deviceState;
        private readonly INhanVienRepository _nhanVienRepository;
        private readonly IChamCongRepository _chamCongRepository;
        private readonly ILogger<SDKHelper> _logger;
        private bool _disposed = false;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private const int LOCK_TIMEOUT_MS = 30000; // 30 seconds timeout
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int RETRY_DELAY_MS = 1000;
        private const int READ_TIMEOUT_MS = 5000; // 5 seconds timeout for read operations
        private const int READ_RETRY_ATTEMPTS = 2;

        public SDKHelper(INhanVienRepository nhanVienRepository, IChamCongRepository chamCongRepository, ILogger<SDKHelper> logger)
        {
            _deviceState = new DeviceState();
            _nhanVienRepository = nhanVienRepository;
            _chamCongRepository = chamCongRepository;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                        var readTask = Task.Run(readOperation);
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
                        await Task.Run(() => {
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

        public async Task<bool> ConnectAsync(string ipAddress, int port)
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
                try
                {
                    if (!_deviceState.IsConnected)
                    {
                        throw new InvalidOperationException("Not connected to the device.");
                    }

                    return await ExecuteWithLockAsync<List<Employee>>(async () =>
                    {
                        var employees = new List<Employee>();
                        bool readResult = await ReadAllWithTimeoutAsync(
                            () => _deviceState.Connector.ReadAllUserID(_deviceState.DeviceNumber),
                            "ReadAllUserID");

                        if (readResult)
                        {
                            string dwEnrollNumber = "";
                            string Name = string.Empty;
                            string Password = string.Empty;
                            int Privilege = 0;
                            bool Enabled = false;

                            bool hasUser = await Task.Run(() => _deviceState.Connector.SSR_GetAllUserInfo(
                                _deviceState.DeviceNumber, 
                                out dwEnrollNumber, 
                                out Name, 
                                out Password, 
                                out Privilege, 
                                out Enabled));

                            while (hasUser)
                            {
                                employees.Add(new Employee
                                {
                                    employeeId = Int32.Parse(dwEnrollNumber),
                                    name = Name,
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
                        }
                        
                        return employees;
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting all employees");
                    throw;
                }
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

                    var employees = await GetAllEmployeeAsync();
                    if (employees == null || !employees.Any())
                    {
                        _logger.LogWarning("No employees found on device");
                        return (false, 0, 0);
                    }
                    _logger.LogInformation("Found {Count} employees on device", employees.Count);

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
                        await ExecuteWithLockAsync<bool>(async () =>
                        {
                            bool readResult = await ReadAllWithTimeoutAsync(
                                () => _deviceState.Connector.ReadAllTemplate(_deviceState.DeviceNumber),
                                "ReadAllTemplate");

                            if (!readResult) return false;

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
                            return true;
                        });
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
        public async Task<bool> RegisterRealtimeEventAsync()
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
        }

        public async Task<bool> UnregisterRealtimeEventAsync()
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
        }
        
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
        }
        #endregion
    }
}