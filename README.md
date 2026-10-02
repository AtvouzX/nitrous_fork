<div align="center">

<img src="assets/logo.png" alt="Nitrous Logo" width="80">

# Nitrous

**Pure, zero-bloat hardware control for Acer Nitro laptops.**

<p align="center">
  <img src="assets/dashboard.png" alt="Main Dashboard" width="30%" />
  <img src="assets/gpu.png" alt="GPU" width="30%" />
  <img src="assets/settings.png" alt="App Settings" width="30%" />
</p>

---

</div>

Nitrous bypasses heavy, bloated OEM telemetry services by communicating directly with your Acer Nitro’s Embedded Controller (EC) via WMI. It gives you a ultra-fast, lightweight dashboard for raw, instant hardware control.

### Key Features
* **Dedicated GPU OC/UC Tab:** Fine-tune core and memory offsets to overclock or underclock your GPU.
* **In-Depth GPU Telemetry:** Monitor real-time GPU metrics, clock speeds, usage, and thermal performance alongside your tuning controls.
* **Auto-Apply OC Profiles:** Automatically trigger default GPU overclocks based on active power profiles (mimicking native NitroSense behavior) or automatically apply your custom OC profile on system boot.
* **Granular Fan Control:** Set precise fan speeds from 0% to 100%, or toggle Auto and Max modes via verified 64-bit WMI payloads.
* **Smart Automation:** Automatically applies quiet modes and 60Hz screen refresh on battery, then restores performance and high refresh rate on AC power.
* **Battery Protection:** Hardware-level 80% charge limit to extend battery lifespan.
* **Dynamic Power Profiles:** Toggle instantly between Quiet, Balanced, Performance, and Turbo TDP modes.
* **Silent Boot:** Bypasses Windows UAC using Task Scheduler to start silently with Windows.
* **Built-in Auto-Updater:** Detects and installs updates directly from GitHub.

### Quick Start

Nitrous is a portable application (1MB) with no setup wizard needed.

1. Download **`Nitrous.exe`** from [Releases](https://github.com/AtvouzX/nitrous_fork/releases).
2. Save it anywhere on your PC (e.g., `C:\Tools`).
3. Launch the executable.

> **Usage:** Nitrous runs silently in your System Tray. Click the tray icon or press your keyboard's dedicated Nitro key to open the dashboard. Configure automation rules in the **Settings** menu.

### Compatibility

Designed for modern Acer Nitro laptops (2021+) using `AcerGamingFunction` WMI classes.

**Confirmed Models:**
* Acer Nitro 16S (`AN16S-61`)
* Acer Nitro V 15 (`ANV15-41`<!--, `ANV15-52` — *Thanks [@Baymax0251](https://github.com/Baymax0251) for testing!* -->)

---

*Disclaimer: Unofficial open-source utility. Not affiliated with Acer. Use at your own risk.*
