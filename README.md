# Printer Support Toolkit 🖨️⚡

> **A high-performance, standalone Windows utility for thermal, label, POS, and industrial printer diagnostics, driver management, automated configuration backups, and multi-section preference captures.**

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20%7C%20Server-blue?logo=windows)](https://microsoft.com/windows)
[![Runtime](https://img.shields.io/badge/.NET-4.8%20(Built--in)-purple)](#zero-dependency-build)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Architecture](https://img.shields.io/badge/Architecture-Native%20WinForms-orange)](#features)

---

## 📌 Overview

**Printer Support Toolkit** is an all-in-one desktop management tool designed specifically for field technicians, system administrators, and warehouse operators. It overcomes the limitations of standard Windows print dialogs by providing hardware-aware diagnostics, direct raw command execution (ZPL, EPL, TSPL, ESC-POS), complete driver cleanups from the Windows Driver Store, and automated multi-tab/multi-section screenshot capturing of printer preferences.

Built as a lightweight, single-binary application with **zero external dependencies**, it compiles directly using the built-in Windows C# compiler (`csc.exe`).

---

## ✨ Key Features

### 1. 🔍 Dynamic Hardware & Brand Recognition
- Automatically detects **30+ printer manufacturers** (Zebra, TSC, Epson, Citizen, Datamax-O'Neil, Honeywell, Godex, Argox, Brother, SATO, Bixolon, Star Micronics, Posiflex, Xprinter, etc.).
- Inspects model numbers (e.g., Zebra ZD230, ZD420, ZT230, GK420t), native DPI (203/300/600 DPI), printer category (Desktop, Industrial, Mobile, POS Receipt), and primary command language.

### 2. 📸 Automated Preferences & Properties Screenshot Capture
- **Solves single-view driver limitations**: Many modern thermal drivers (like Zebra ZDesigner) condense all settings into a single tab with an internal vertical navigation sidebar.
- Automatically enumerates and traverses all internal sections (**Page Setup**, **Print Options**, **Operation Mode**, **Graphic Options**, **Custom Commands**, **Maintenance**, **Units**, **Help & About**) via Win32 messaging (`BM_CLICK`).
- Captures standard `SysTabControl32` property pages as well as custom sidebars.
- Generates a styled high-resolution **Visual Configuration Card** (PNG) detailing queue settings, driver paths, and port configurations.

### 3. 🛠️ Print Spooler & Queue Repair
- One-click restart of the Windows Print Spooler service (`spoolsv.exe`).
- Automatic purging of stuck, corrupted, or pending print jobs from `%SystemRoot%\System32\spool\PRINTERS`.
- Detection and repair of orphaned registry queue handles and offline port locks.

### 4. 🧹 Deep Driver Removal & Driver Store Cleanup
- Removes printer drivers completely from the Windows Driver Store using `pnputil.exe` (deleting published `oem*.inf` packages).
- Eliminates locked driver files, registry remnants, and corrupted package installations.
- Provides 1-click links to official vendor driver repositories.

### 5. 💾 Backup & 1-Click Settings Restore
- Backs up full `DEVMODE` binary configuration (`.dat`) containing precise label dimensions, darkness, print speed, sensor offsets, and media types.
- Exports printer registry configurations, INF manifests, and Windows `PrintBrm` migration packages.
- 1-click restore re-applies configurations instantly across identical printer models.

### 6. ⚡ Direct RAW Command Console (ZPL / EPL / TSPL / ESC-POS)
- Sends raw command strings and custom control files directly to the printer queue via `winspool.drv` (bypassing Windows GDI rendering).
- Includes one-click operational presets:
  - **Calibrate Media / Gap Sensor**
  - **Print Configuration Label**
  - **Feed Single Label**
  - **Print Head Clean / Test Pattern**
  - **Barcode & QR Code Test**

### 7. 🩺 Sanitized Health & Diagnostic Reports
- Generates deep diagnostic health checks with automated error flagging (offline status, paper jams, door open, spooler errors).
- Exports formatted reports to **Markdown** and **HTML**.
- Sanitizes large base64 print ticket payloads (`PrintTicketXML`) to keep reports clean, concise, and readable.

### 8. 🔌 Real-Time USB Plug & Play Monitor
- Listens for Windows `WM_DEVICECHANGE` hardware events to immediately notify when USB printers are plugged in, re-connected, or disconnected.

---

## 🎨 User Interface

- Modern **Dark Glassmorphism UI** tailored for high-contrast usability.
- Custom GDI+ rendering engine with no external component libraries.
- Fluid, responsive layouts with horizontal flow navigation that adapt cleanly to all display scaling factors and DPI modes.

---

## 🚀 Getting Started

### Prerequisites
- **Operating System**: Windows 10, Windows 11, or Windows Server 2016+
- **Privileges**: Administrator rights (required for Print Spooler and Driver Store operations; automatically requested via `app.manifest`).
- **Framework**: .NET Framework 4.8 (pre-installed on all modern Windows installations).

### Zero-Dependency Build

No Visual Studio installation required! Compile directly from the Windows Command Prompt or PowerShell:

```cmd
git clone https://github.com/<your-username>/printer-support-toolkit.git
cd printer-support-toolkit
build.bat
```

The compiled binary `PrinterToolkit.exe` (~200 KB) will be generated immediately in the root folder.

---

## 📂 Project Structure

```
printer-support-toolkit/
├── .gitignore               # Standard exclusions (Output/, binaries, temp files)
├── README.md                # Project documentation
├── build.bat                # Standalone C# compiler script (uses .NET 4.8 csc.exe)
├── app.ico                  # Application icon
├── ZD230.bat                # Legacy reference automation script
└── src/
    ├── app.manifest         # Elevation manifest (requireAdministrator) & DPI awareness
    ├── Program.cs           # Application entry point
    ├── MainForm.cs          # Main application window & responsive tabbed interface
    ├── Theme.cs             # Dark theme design system, typography, and palettes
    ├── Controls.cs          # Custom GDI+ UI controls (Buttons, Cards, Badges, Tabs)
    ├── Native.cs            # Win32 RAW spooler APIs, window traversal, and screen capture
    ├── ModelDatabase.cs     # 30+ printer brand definitions & command template library
    └── PrinterService.cs    # WMI discovery, Spooler management, pnputil, and backup engine
```

---

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request or open an Issue for:
- Adding support for additional industrial printer brands.
- Expanding command templates (ZPL, EPL, TSPL, CPCL, ESC-POS).
- Improving driver detection heuristics.

---

## 📄 License

Distributed under the **MIT License**. See `LICENSE` for details.
