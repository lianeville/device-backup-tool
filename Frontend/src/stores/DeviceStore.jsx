import { create } from "zustand"

const useDeviceStore = create(set => {
	// Load devices from localStorage or initialize with an empty array
	const loadDevices = () => {
		try {
			const storedDevices = localStorage.getItem("devices")
			return storedDevices ? JSON.parse(storedDevices) : []
		} catch (error) {
			console.error("Failed to load devices from localStorage", error)
			return []
		}
	}

	// Save devices to localStorage
	const saveDevices = devices => {
		try {
			localStorage.setItem("devices", JSON.stringify(devices))
		} catch (error) {
			console.error("Failed to save devices to localStorage", error)
		}
	}

	return {
		devices: loadDevices(),
		addDevice: device =>
			set(state => {
				const newDevice = {
					...device,
					id: device.id || Date.now() + Math.floor(Math.random() * 1000),
					baseUrl: `https://${device.ip}`,
					status: device.status || "Offline",
				}

				const updatedDevices = [...state.devices, newDevice]
				saveDevices(updatedDevices) // Persist to localStorage

				return { devices: updatedDevices }
			}),
		removeDevice: id =>
			set(state => {
				const updatedDevices = state.devices.filter(
					device => device.id !== id
				)
				saveDevices(updatedDevices) // Persist to localStorage

				return { devices: updatedDevices }
			}),
	}
})

export default useDeviceStore
