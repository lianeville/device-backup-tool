import React, { useState, useRef, useEffect } from "react"
import { ChevronDown, ChevronUp, HardDrive } from "lucide-react"
import dateFormat from "dateformat"
import EventLogItem from "./EventLogItem"
import BackupLogItem from "./BackupLogItem"
import useDeviceStore from "../stores/DeviceStore"

const DeviceList = () => {
	const { devices } = useDeviceStore()

	const [expandedDevices, setExpandedDevices] = useState({})
	const [activeBackups, setActiveBackups] = useState({})
	const [scriptStatuses, setScriptStatuses] = useState({})
	const [authToken, setAuthToken] = useState(null)
	const [deviceLogs, setDeviceLogs] = useState({})
	const [isGenerating, setIsGenerating] = useState(false)

	// Reference to the device logs container
	const deviceLogsRefs = useRef({})

	const addLog = (deviceId, message, status) => {
		let time = dateFormat("h:MM:ss TT")

		setDeviceLogs(prev => {
			const newLogs = {
				...prev,
				[deviceId]: [...(prev[deviceId] || []), { message, status, time }],
			}
			return newLogs
		})
	}

	const addBackupLog = (deviceId, log) => {
		log.date = dateFormat("mmm dd, yyyy HH:MM")
		log.isBackup = true

		setDeviceLogs(prev => {
			const newLogs = {
				...prev,
				[deviceId]: [...(prev[deviceId] || []), log],
			}
			return newLogs
		})
	}

	useEffect(() => {
		Object.keys(deviceLogs).forEach(deviceId => {
			const logsContainer = deviceLogsRefs.current[deviceId]
			if (logsContainer) {
				logsContainer.scrollTop = logsContainer.scrollHeight
			}
		})
	}, [deviceLogs])

	const startBackup = async deviceId => {
		const device = devices.find(d => d.id === deviceId)
		if (!device) {
			addLog(deviceId, "Device not found", "error")
			return
		}

		try {
			setIsGenerating(true)

			setActiveBackups(prev => ({
				...prev,
				[deviceId]: true,
			}))

			// Modify the URL to include credentials as query parameters
			const startBackupResponse = await fetch(
				`http://localhost:5299/api/device/unifi/backup/start?Username=${device.username}&Password=${device.password}&BaseUrl=${device.baseUrl}&Ip=${device.ip}`,
				{
					method: "POST",
					credentials: "include",
					headers: {
						"Content-Type": "application/json",
						Authorization: `Bearer ${authToken}`,
					},
					body: JSON.stringify({
						Username: device.username,
						Password: device.password,
						baseUrl: device.baseUrl,
						Ip: device.ip,
					}),
				}
			)

			if (!startBackupResponse.ok) {
				throw new Error(`HTTP error! status: ${startBackupResponse.status}`)
			}

			const { backupId } = await startBackupResponse.json()
			const regenerate = "true"

			// Modify EventSource URL to include credentials as query parameters
			const eventSource = new EventSource(
				`http://localhost:5299/api/device/unifi/backup/status/${backupId}/${regenerate}?Username=${device.username}&Password=${device.password}&BaseUrl=${device.baseUrl}&Ip=${device.ip}`,
				{
					withCredentials: true,
				}
			)

			eventSource.onopen = () => {
				addLog(deviceId, "Connection established", "progress")
			}

			eventSource.onmessage = event => {
				try {
					const data = JSON.parse(event.data)
					addLog(deviceId, data.message, data.status)

					if (data.status === "complete" || data.status === "error") {
						eventSource.close()
						addBackupLog(deviceId, data.data)
						setIsGenerating(false)

						if (data.status === "complete" && data.data) {
							console.log("Backup completed with data:", data.data)
						}
					}
				} catch (error) {
					console.error("Error parsing SSE data:", error)
					addLog(deviceId, "Error processing server message", "error")
				}
			}

			eventSource.onerror = error => {
				console.error("EventSource failed:", error)
				addLog(deviceId, "Connection error occurred", "error")
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
			addLog(deviceId, `Failed to start backup: ${error.message}`, "error")
			setActiveBackups(prev => ({
				...prev,
				[deviceId]: false,
			}))
			setIsGenerating(false)
		}
	}

	const toggleDevice = deviceId => {
		setExpandedDevices(prev => ({
			...prev,
			[deviceId]: !prev[deviceId],
		}))

		startBackup(deviceId)
	}

	const getScriptStatus = async device => {
		addLog(device.id, "Getting backup script status...", "progress")
		const deviceId = device.id
		try {
			// Modify the URL to include credentials as query parameters
			const response = await fetch(
				`http://localhost:5299/api/device/script/status?Username=${device.username}&Password=${device.password}&BaseUrl=${device.baseUrl}&Ip=${device.ip}`,
				{
					headers: {
						Authorization: `Bearer ${authToken}`,
						"Content-Type": "application/json",
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
				message = "Backup script has been running since "
				status = "complete"
			} else {
				message = "Backup script has been inactive since "
				status = "error"
			}
			message += dateFormat(data.dateChanged, "mmm dd, yyyy HH:MM")

			addLog(deviceId, message, status)
		} catch (error) {
			setScriptStatuses(prev => ({
				...prev,
				[deviceId]: "Error fetching status",
			}))
		}
	}

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
										onClick={() => getScriptStatus(device)}
										className="flex items-center space-x-2 px-4 py-2 bg-gray-500 text-white rounded-md hover:bg-gray-600 transition-colors"
									>
										<span>Get Script Status</span>
									</button>
								</div>
							</div>
						</div>

						{deviceLogs[device.id] &&
							deviceLogs[device.id].length > 0 && (
								<div className="border-t p-4 bg-gray-50">
									<h4 className="font-medium mb-2">Backup Events</h4>
									<div
										ref={el =>
											(deviceLogsRefs.current[device.id] = el)
										}
										className="space-y-2 max-h-[325px] overflow-y-scroll px-2"
									>
										{deviceLogs[device.id].map((log, index) =>
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
