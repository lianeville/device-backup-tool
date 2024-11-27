# Device Backup Manager

## Overview

Device Backup Manager is a web application designed to facilitate automated backup management for network devices. The application provides a user-friendly interface for generating backups, monitoring backup processes, and checking script statuses. It maintains persistent SSH connections to network devices and performs SSH commands to execute backup procedures securely.

![Adding Device Demo]("demo/Adding_Device_Example.gif")

## Features

### Device Management

-  View device details (name, IP address, status)
-  Support for multiple devices
-  Real-time backup status tracking
-  Secure SSH connection management

### Backup Functionality

-  One-click backup generation
-  Server-Sent Events (SSE) for real-time backup progress
-  Backup file decrypting and parsing
-  Detailed event and backup logging
-  Automated SSH command execution for backup processes

### Script Status Monitoring

-  Check backup script running status
-  View last script execution details
-  Monitor SSH connection and command execution logs

## Tech Stack

### Frontend

-  React
-  Tailwind CSS

### Backend

-  ASP.NET Core Web API
-  SSH.NET

## Prerequisites

-  Node.js
-  .NET Core SDK
-  Supported Controller Type (Unifi)
-  [Unifi backup Script](https://github.com/gebn/unifibackup)

## Installation

1. Clone the repository

```bash
git clone git@github.com:lianeville/device-backup-tool.git
```

2. Run the install scripts

   -  Windows:

   ```bash
   ./setup-windows.bat
   ```

   -  Unix:

   ```bash
   ./setup-unix.sh
   ```

3. Start the dev environment

```bash
npm run start
```

## Usage

Add a device, and enter it's IP and credentials.

## Device Type Support

-  Unifi Controllers
