import React, { useState } from "react"
import { ChevronDown, ChevronUp, HardDrive, Clock } from "lucide-react"

const DeviceList = () => {
	// Sample device data
	const [devices, setDevices] = useState([
		{
			id: 1,
			name: "Production Server",
			ip: "192.168.1.101",
			status: "Online",
		},
		{ id: 2, name: "Database Server", ip: "192.168.1.102", status: "Online" },
		{ id: 3, name: "Dev Environment", ip: "192.168.1.103", status: "Online" },
	])

	// Track which devices are expanded and their logs
	const [expandedDevices, setExpandedDevices] = useState({})
	const [backupLogs, setBackupLogs] = useState({})

	// Simulate backup process
	const generateBackup = async deviceId => {
		const logs = []
		const updateLogs = message => {
			logs.push({ time: new Date().toLocaleTimeString(), message })
			setBackupLogs(prev => ({
				...prev,
				[deviceId]: [...logs],
			}))
		}

		updateLogs("Initializing backup process...")
		await new Promise(resolve => setTimeout(resolve, 1000))

		updateLogs("Preparing SSH connection...")
		await new Promise(resolve => setTimeout(resolve, 1500))

		updateLogs("Connected successfully")
		await new Promise(resolve => setTimeout(resolve, 1000))

		updateLogs("Fetching backup file...")
		await new Promise(resolve => setTimeout(resolve, 2000))

		updateLogs("Backup completed successfully")
	}

	const toggleDevice = deviceId => {
		setExpandedDevices(prev => ({
			...prev,
			[deviceId]: !prev[deviceId],
		}))

		if (!backupLogs[deviceId]) {
			generateBackup(deviceId)
		}
	}

	return (
		<div className="w-full mx-auto p-6 space-y-4">
			<h1 className="text-2xl font-bold mb-6">Device Backup Dashboard</h1>

			<div className="space-y-4">
				{devices.map(device => (
					<div key={device.id} className="border rounded-lg shadow-sm">
						<div className="p-4 bg-white">
							<div className="flex items-center justify-between">
								<div className="flex items-center space-x-4">
									<HardDrive className="text-gray-500" />
									<div>
										<h3 className="font-medium">{device.name}</h3>
										<p className="text-sm text-gray-500">
											{device.ip}
										</p>
									</div>
								</div>

								<button
									onClick={() => toggleDevice(device.id)}
									className="flex items-center space-x-2 px-4 py-2 bg-blue-500 text-white rounded-md hover:bg-blue-600 transition-colors"
								>
									<span>Generate Backup</span>
									{expandedDevices[device.id] ? (
										<ChevronUp size={16} />
									) : (
										<ChevronDown size={16} />
									)}
								</button>
							</div>
						</div>

						{expandedDevices[device.id] && (
							<div className="border-t p-4 bg-gray-50">
								<h4 className="font-medium mb-2">Backup Events</h4>
								<div className="space-y-2">
									{backupLogs[device.id]?.map((log, index) => (
										<div
											key={index}
											className="flex items-center space-x-2 text-sm"
										>
											<Clock size={14} className="text-gray-400" />
											<span className="text-gray-500">
												{log.time}
											</span>
											<span>{log.message}</span>
										</div>
									))}
								</div>
							</div>
						)}
					</div>
				))}
			</div>
		</div>
	)
}

export default DeviceList
