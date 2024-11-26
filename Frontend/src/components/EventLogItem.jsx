import React, { useRef, useEffect } from "react"
import PropTypes from "prop-types"
import { Clock } from "lucide-react"

const StatusIndicator = ({ status, key }) => {
	const getStatusColor = status => {
		switch (status.toLowerCase()) {
			case "error":
				return "bg-red-500"
			case "complete":
				return "bg-green-500"
			default:
				return "bg-blue-500"
		}
	}

	return (
		<span
			className={`inline-block w-2 h-2 rounded-full mr-2 ${getStatusColor(
				status
			)}`}
		/>
	)
}

const EventLogItem = ({ log }) => {
	return (
		<div className="flex items-center space-x-2 text-sm">
			<div className="space-x-2 flex items-center flex-shrink-0">
				<Clock size={14} className="text-gray-400" />
				<span className="text-gray-500">{log.time}</span>
				<StatusIndicator status={log.status} />
			</div>
			<span
				title={log.message}
				className={`w-100 truncate overflow-hidden ${
					log.status === "error" ? "text-red-600" : ""
				}`}
			>
				{log.message}
			</span>
		</div>
	)
}

EventLogItem.propTypes = {
	log: PropTypes.shape({
		time: PropTypes.string.isRequired, // The time the log was recorded
		status: PropTypes.string.isRequired, // The status of the log (e.g., "error", "success")
		message: PropTypes.string.isRequired, // The message to display for the log
	}).isRequired,
}

export default EventLogItem
