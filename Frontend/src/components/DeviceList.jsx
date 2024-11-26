import React, { useState, useRef, useEffect } from "react"
import { ChevronDown, ChevronUp, HardDrive, Clock } from "lucide-react"
import dateFormat, { masks } from "dateformat"
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

	const addLog = (message, status) => {
		let time = dateFormat("h:MM:ss TT")

		setLogs(prev => [...prev, { message, status, time }])
	}

	const addBackupLog = log => {
		log.time = dateFormat("h:MM:ss TT")
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
			setScriptStatuses(prev => ({
				...prev,
				[deviceId]: data.status || "Unknown",
			}))
		} catch (error) {
			setScriptStatuses(prev => ({
				...prev,
				[deviceId]: "Error fetching status",
			}))
		}
	}

	const handleLogin = async () => {
		const loginData = {
			Username: "admin",
			Password: "password123",
		}

		try {
			const response = await fetch("http://localhost:5299/api/auth/login", {
				method: "POST",
				headers: {
					"Content-Type": "application/json",
				},
				body: JSON.stringify(loginData),
			})

			if (!response.ok) {
				throw new Error("Login failed")
			}

			const data = await response.json()

			if (!data.token) {
				throw new Error("Token not found in response")
			}

			// Assuming setAuthToken saves the token securely (e.g., local storage or state)
			storeAuthToken(data.token)

			alert("Login successful!")
		} catch (error) {
			console.error("Login failed:", error.message)
			alert("Login failed: " + error.message)
		}
	}

	const storeAuthToken = (token, useSessionStorage = false) => {
		try {
			if (!token) {
				throw new Error("Invalid token")
			}

			// Decide storage method: sessionStorage or localStorage
			const storage = useSessionStorage ? sessionStorage : localStorage
			setAuthToken(token)

			// Save the token
			storage.setItem("authToken", token)
			console.log("Token saved successfully.")
		} catch (error) {
			console.error("Error saving token:", error.message)
		}
	}

	const backupLogRef = useRef(null)

	return (
		<div className="w-full mx-auto p-6 space-y-4">
			<h1 className="text-2xl font-bold mb-6">Device Backup Dashboard</h1>

			{/* Login Button */}
			<button
				onClick={handleLogin}
				className="px-4 py-2 bg-green-500 text-white rounded-md hover:bg-green-600 transition-colors"
			>
				Log in
			</button>

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
											{activeBackups[device.id]
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
									className="space-y-2 max-h-52 overflow-y-scroll"
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

						{/* Display Script Status */}
						{scriptStatuses[device.id] && (
							<div className="p-4 bg-gray-100 mt-2">
								<h4 className="font-medium">Script Status</h4>
								<p className="text-sm text-gray-600">
									{scriptStatuses[device.id]}
								</p>
							</div>
						)}
					</div>
				))}
			</div>
		</div>
	)
}

export default DeviceList
