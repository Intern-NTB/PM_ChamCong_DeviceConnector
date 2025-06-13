# PM_ChamCong_DeviceConnector

This project provides a connector for timekeeping devices, allowing communication and data synchronization (employee data, fingerprints, attendance records) with a backend system.

## Table of Contents

- [Project Structure](#project-structure)
- [Installation and Setup](#installation-and-setup)
  - [Prerequisites](#prerequisites)
  - [NuGet Package Restoration](#nuget-package-restoration)
- [Local Network (LAN) Setup for Device Connectivity](#local-network-lan-setup-for-device-connectivity)
  - [Configure Your Computer's IP Address](#configure-your-computers-ip-address)
  - [Configure the Timekeeping Device (if applicable)](#configure-the-timekeeping-device-if-applicable)
  - [Test Connectivity](#test-connectivity)
- [User Secrets Configuration](#user-secrets-configuration)
  - [Setting up User Secrets](#setting-up-user-secrets)
  - [Example `secrets.json`](#example-secretsjson)
- [Running the Application](#running-the-application)

## Project Structure

The solution consists of three main projects:

1. **DeviceConnector** (.NET 8.0)
   - Main web application project
   - Implements gRPC services for device and employee management
   - Uses modern .NET 8.0 features

2. **SDKHelper** (.NET Framework 4.8)
   - Library project for device SDK integration
   - Uses .NET Framework 4.8 for compatibility with device SDKs
   - Handles direct communication with timekeeping devices

3. **Shared** (.NET Framework 4.8)
   - Shared library project
   - Contains common interfaces and models
   - Uses .NET Framework 4.8 for compatibility

---

## Installation and Setup

### Prerequisites

Ensure you have the following installed:

*   **.NET SDK**: Version 8.0 or higher (for DeviceConnector project)
*   **.NET Framework**: Version 4.8 (for SDKHelper and Shared projects)
*   **Visual Studio 2022** (recommended) or a compatible IDE
*   **NuGet Package Manager**: Usually comes with Visual Studio

### NuGet Package Restoration

The project uses NuGet packages for managing dependencies. When you restore packages, it will automatically download and install all required packages from NuGet Gallery.

You can restore packages in one of these ways:

**Using `dotnet` CLI (recommended):**
```bash
dotnet restore
```
This command will:
- Read project files to identify required NuGet packages
- Download packages from NuGet Gallery
- Install packages to the solution's packages directory
- Restore project references

**Using Visual Studio:**
1. Right-click on the solution in Solution Explorer
2. Select "Restore NuGet Packages"
3. Visual Studio will automatically handle the download and installation process

Note: Make sure you have a stable internet connection during the restore process as it needs to download packages from NuGet Gallery.

## Local Network (LAN) Setup for Device Connectivity

To connect your computer to the timekeeping device locally, both your computer and the device must be on the same network subnet. This usually involves setting a static IP address on your computer.

**Assumptions:**
*   **Timekeeping Device IP**: We will assume the timekeeping device's IP address is `192.168.2.202`. (You might need to verify this on your specific device.)
*   **Subnet Mask**: `255.255.255.0`

### Configure Your Computer's IP Address

1.  **Open Network Connections:**
    *   **Windows**: Go to `Control Panel` -> `Network and Internet` -> `Network and Sharing Center`. Click on `Change adapter settings` (on the left pane).
    *   **macOS**: Go to `System Settings` (or `System Preferences`) -> `Network`. Select your active network adapter (e.g., Wi-Fi or Ethernet).

2.  **Select Network Adapter:**
    *   Right-click (Windows) or select (macOS) the active network adapter you are using to connect to the device (e.g., `Ethernet` for wired, `Wi-Fi` for wireless).
    *   Select `Properties` (Windows) or `Details`/`TCP/IP` tab (macOS).

3.  **Configure IPv4 Properties:**
    *   **Windows**: In the properties window, select `Internet Protocol Version 4 (TCP/IPv4)` and click `Properties`.
    *   **macOS**: Select `Manually` for `Configure IPv4`.

4.  **Set Static IP Address:**
    *   Select "Use the following IP address" (Windows) or enter the details manually (macOS).
    *   **IP Address**: Set this to an IP address within the same subnet as your device, but *different* from the device's IP. For example, `192.168.2.100`.
    *   **Subnet Mask**: `255.255.255.0`
    *   **Default Gateway**: You can leave this empty if you're only connecting to the device directly and don't need internet access. If you need internet, use your router's IP (e.g., `192.168.2.1`).
    *   **DNS Servers**: You can leave these empty or use public DNS servers (e.g., 8.8.8.8 for Google DNS) if you need internet access.

5.  **Save Changes:** Click `OK` or `Apply` to save your network settings.

### Configure the Timekeeping Device (if applicable)

Access the timekeeping device's menu/settings (refer to your device's manual) and ensure its network settings are configured as follows:

*   **IP Address**: `192.168.2.202` (or whatever its actual static IP is).
*   **Subnet Mask**: `255.255.255.0`
*   **Gateway**: Should be the same as your computer's gateway (if any).

### Test Connectivity

Open a command prompt (Windows) or Terminal (macOS/Linux) and ping the timekeeping device's IP address:

```bash
ping 192.168.2.202
```

You should see successful replies, indicating that your computer can communicate with the timekeeping device.

## User Secrets Configuration

The application uses User Secrets to store sensitive information (like connection strings) and dynamic configurations (like the list of timekeeping devices). This keeps sensitive data out of your source code repository.

### Setting up User Secrets

1.  **Open User Secrets:**
    *   In Visual Studio, right-click on the `DeviceConnector` project in the Solution Explorer.
    *   Select `Manage User Secrets`.
    *   This will open a `secrets.json` file in your editor. If it doesn't exist, Visual Studio will create it for you.

2.  **Add Configuration:**
    Add your device configurations and any other sensitive settings (like database connection strings) to this `secrets.json` file.

### Example `secrets.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=your_sql_server_address;Database=your_database_name;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Encrypt=False;"
  },
  "Devices": [
    {
      "IpAddress": "192.168.2.202",
      "Port": 4370
    },
    {
      "IpAddress": "192.168.2.203",
      "Port": 4370
    },
    {
      "IpAddress": "192.168.2.204",
      "Port": 4370
    }
    // Add more device configurations as needed
  ]
}
```

*   **`ConnectionStrings:DefaultConnection`**: Replace `your_sql_server_address` and `your_database_name` with your actual SQL Server details.
*   **`Devices`**: This array contains objects, where each object represents a timekeeping device.
    *   `IpAddress`: The IP address of your timekeeping device.
    *   `Port`: The communication port of your timekeeping device (e.g., 4370 is common for ZKTeco devices).

## Running the Application

After configuring the network and user secrets:

1.  **Build the Project:** Build the `DeviceConnector` project.
2.  **Run the Application:** Run the `DeviceConnector` project from Visual Studio or using `dotnet run` in your terminal.

The `RealTimeService` background service will automatically attempt to connect to the configured devices and register for real-time events. You can interact with the gRPC services (DeviceService, EmployeeService) using a gRPC client, providing the JWT token in the `Authorization` metadata header.

---