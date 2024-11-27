import React, { useState } from "react"
import { Plus, ChevronDown, ChevronUp } from "lucide-react"
import useDeviceStore from "../stores/DeviceStore"

const AddDevice = () => {
	const { addDevice } = useDeviceStore()
	const [isExpanded, setIsExpanded] = useState(false)
	const [deviceInfo, setDeviceInfo] = useState({
		name: "",
		ip: "",
		username: "",
		password: "",
	})

	const handleInputChange = e => {
		const { name, value } = e.target
		setDeviceInfo(prev => ({
			...prev,
			[name]: value,
		}))
	}

	const handleSubmit = e => {
		e.preventDefault()

		// Prepare device object to match store's addDevice expectations
		const newDevice = {
			name: deviceInfo.name,
			ip: deviceInfo.ip,
			username: deviceInfo.username,
			password: deviceInfo.password,
			// Optional: You might want to add more fields if needed
			// id will be auto-generated in the store
			// status will default to "Offline" in the store
		}

		// Add the device using the store's addDevice method
		addDevice(newDevice)

		// Reset form
		setDeviceInfo({
			name: "",
			ip: "",
			username: "",
			password: "",
		})
		setIsExpanded(false)
	}

	return (
		<div className="w-full max-w-md mx-auto">
			<button
				onClick={() => setIsExpanded(!isExpanded)}
				className="w-full flex items-center justify-between bg-blue-500 text-white px-4 py-2 rounded-lg hover:bg-blue-600 transition-colors"
			>
				<span className="flex items-center">
					<Plus className="mr-2" />
					Add New Device
				</span>
				{isExpanded ? <ChevronUp /> : <ChevronDown />}
			</button>

			{isExpanded && (
				<form
					onSubmit={handleSubmit}
					className="mt-4 p-4 bg-white shadow-md rounded-lg space-y-4"
				>
					<div className="flex">
						<div className="flex-shrink-0 w-1/2 pr-1">
							<label
								htmlFor="ip"
								className="block text-sm font-medium text-gray-700 mb-1"
							>
								Name
							</label>
							<input
								type="text"
								id="name"
								name="name"
								value={deviceInfo.name}
								onChange={handleInputChange}
								placeholder="Unifi Controller"
								className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
								required
							/>
						</div>

						<div className="flex-shrink-0 w-1/2 pl-1">
							<label
								htmlFor="ip"
								className="block text-sm font-medium text-gray-700 mb-1"
							>
								IP Address
							</label>
							<input
								type="text"
								id="ip"
								name="ip"
								value={deviceInfo.ip}
								onChange={handleInputChange}
								placeholder="192.168.1.1"
								className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
								required
							/>
						</div>
					</div>
					<div className="flex">
						<div className="flex-shrink-0 w-1/2 pr-1">
							<label
								htmlFor="username"
								className="block text-sm font-medium text-gray-700 mb-1"
							>
								Username
							</label>
							<input
								type="text"
								id="username"
								name="username"
								value={deviceInfo.username}
								onChange={handleInputChange}
								placeholder="admin"
								className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
								required
							/>
						</div>

						<div className="flex-shrink-0 w-1/2 pl-1">
							<label
								htmlFor="password"
								className="block text-sm font-medium text-gray-700 mb-1"
							>
								Password
							</label>
							<input
								type="password"
								id="password"
								name="password"
								value={deviceInfo.password}
								onChange={handleInputChange}
								placeholder="Enter password"
								className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
								required
							/>
						</div>
					</div>

					<div className="flex justify-end space-x-2">
						<button
							type="button"
							onClick={() => setIsExpanded(false)}
							className="px-4 py-2 text-gray-600 hover:bg-gray-100 rounded-md"
						>
							Cancel
						</button>
						<button
							type="submit"
							className="px-4 py-2 bg-blue-500 text-white rounded-md hover:bg-blue-600 transition-colors"
						>
							Add Device
						</button>
					</div>
				</form>
			)}
		</div>
	)
}

export default AddDevice
