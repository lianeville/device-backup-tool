import React, { useState, useRef, useEffect } from "react"
import { ChevronDown, ChevronUp, HardDrive } from "lucide-react"
import dateFormat from "dateformat"
import EventLogItem from "./EventLogItem"
import BackupLogItem from "./BackupLogItem"

const DeviceList = () => {
	const [devices, setDevices] = useState([
		{
			id: 1,
			name: "Unifi Controller",
			ip: "192.168.1.1",
			status: "Online",
		},
	])

	const [expandedDevices, setExpandedDevices] = useState({})
	const [activeBackups, setActiveBackups] = useState({})
	const [scriptStatuses, setScriptStatuses] = useState({})
	const [authToken, setAuthToken] = useState(null) // To store the JWT token
	const [logs, setLogs] = useState([])
	const [isGenerating, setIsGenerating] = useState(false)

	const addLog = (message, status) => {
		let time = dateFormat("h:MM:ss TT")

		setLogs(prev => [...prev, { message, status, time }])
	}

	const addBackupLog = log => {
		log.date = dateFormat("mmm dd, yyyy HH:MM")
		log.isBackup = true

		console.log(log)
		setLogs(prev => [...prev, log])
	}

	useEffect(() => {
		if (backupLogRef.current) {
			backupLogRef.current.scrollTop = backupLogRef.current.scrollHeight
		}
	}, [logs])

	const startBackup = async deviceId => {
		try {
			// Set backup as active
			setIsGenerating(true)

			setActiveBackups(prev => ({
				...prev,
				[deviceId]: true,
			}))

			// First, initiate the backup process
			const response = await fetch(
				"http://localhost:5299/api/device/unifi/backup/start",
				{
					method: "POST",
					credentials: "include",
					headers: {
						Authorization: `Bearer ${authToken}`,
					},
				}
			)

			if (!response.ok) {
				throw new Error(`HTTP error! status: ${response.status}`)
			}

			const { backupId } = await response.json()
			const regenerate = "false"
			const eventSource = new EventSource(
				`http://localhost:5299/api/device/unifi/backup/status/${backupId}/${regenerate}`,
				{
					withCredentials: true,
				}
			)

			eventSource.onopen = () => {
				addLog("Connection established", "progress", deviceId)
			}

			eventSource.onmessage = event => {
				try {
					const data = JSON.parse(event.data)
					addLog(data.message, data.status, deviceId)

					if (data.status === "complete" || data.status === "error") {
						eventSource.close()
						addBackupLog(data.data)
						setIsGenerating(false)

						if (data.status === "complete" && data.data) {
							console.log("Backup completed with data:", data.data)
						}
					}
				} catch (error) {
					console.error("Error parsing SSE data:", error)
					addLog("Error processing server message", "error", deviceId)
				}
			}

			eventSource.onerror = error => {
				console.error("EventSource failed:", error)
				addLog("Connection error occurred", "error", deviceId)
				eventSource.close()
				setIsGenerating(false)
				setActiveBackups(prev => ({
					...prev,
					[deviceId]: false,
				}))
			}

			return () => {
				if (eventSource) {
					eventSource.close()
				}
			}
		} catch (error) {
			addLog(`Failed to start backup: ${error.message}`, "error", deviceId)
			setActiveBackups(prev => ({
				...prev,
				[deviceId]: false,
			}))
		}
	}

	const toggleDevice = deviceId => {
		setExpandedDevices(prev => ({
			...prev,
			[deviceId]: !prev[deviceId],
		}))

		startBackup(deviceId)
	}

	const getScriptStatus = async deviceId => {
		console.log(authToken)
		try {
			const response = await fetch(
				"http://localhost:5299/api/device/script/status",
				{
					headers: {
						Authorization: `Bearer ${authToken}`, // Include token in header
					},
					credentials: "include",
				}
			)
			if (!response.ok) {
				throw new Error(`HTTP error! status: ${response.status}`)
			}
			const data = await response.json()

			let message,
				status = ""
			if (data.status) {
				message = "Script has been running since "
				status = "complete"
			} else {
				message = "Script has been inactive since "
				status = "error"
			}
			message += dateFormat(data.dateChanged, "mmm dd, yyyy HH:MM")

			addLog(message, status)
		} catch (error) {
			setScriptStatuses(prev => ({
				...prev,
				[deviceId]: "Error fetching status",
			}))
		}
	}

	const backupLogRef = useRef(null)

	return (
		<div className="w-full mx-auto p-6 space-y-4">
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

								<div className="flex space-x-2">
									<button
										onClick={() => toggleDevice(device.id)}
										className="flex items-center space-x-2 px-4 py-2 bg-blue-500 text-white rounded-md hover:bg-blue-600 transition-colors"
										disabled={activeBackups[device.id]}
									>
										<span>
											{isGenerating
												? "Backup in Progress"
												: "Generate Backup"}
										</span>
										{expandedDevices[device.id] ? (
											<ChevronUp size={16} />
										) : (
											<ChevronDown size={16} />
										)}
									</button>

									<button
										onClick={() => getScriptStatus(device.id)}
										className="flex items-center space-x-2 px-4 py-2 bg-gray-500 text-white rounded-md hover:bg-gray-600 transition-colors"
									>
										<span>Get Script Status</span>
									</button>
								</div>
							</div>
						</div>

						{logs.length > 0 && (
							<div className="border-t p-4 bg-gray-50">
								<h4 className="font-medium mb-2">Backup Events</h4>
								<div
									ref={backupLogRef}
									className="space-y-2 max-h-52 overflow-y-scroll px-2"
								>
									{logs.map((log, index) =>
										log.isBackup ? (
											<BackupLogItem log={log} key={index} />
										) : (
											<EventLogItem log={log} key={index} />
										)
									)}
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
