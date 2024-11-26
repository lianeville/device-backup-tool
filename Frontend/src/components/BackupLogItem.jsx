import React from "react"
import { Clock, AlertTriangle, Server } from "lucide-react"
import PropTypes from "prop-types"

const BackupLogItem = ({ log }) => {
	// Convert timestamp to readable date

	console.log(log)
	const formatDate = isoString => {
		return new Date(isoString).toLocaleString("en-US", {
			year: "numeric",
			month: "long",
			day: "numeric",
			hour: "2-digit",
			minute: "2-digit",
			second: "2-digit",
			timeZoneName: "short",
		})
	}

	// Convert millisecond timestamp to readable datetime
	const formatAlertTime = milliseconds => {
		return new Date(parseInt(milliseconds)).toLocaleString("en-US", {
			month: "short",
			day: "numeric",
			hour: "2-digit",
			minute: "2-digit",
			second: "2-digit",
		})
	}

	return (
		<div className="pt-2">
			{/* <div className="flex items-center mb-3">
				<Server className="mr-2 text-blue-500" size={20} />
				<h2 className="text-lg font-semibold text-gray-800">
					Backup Log: {log.Hostname}
				</h2>
			</div> */}

			<div className="flex items-center">
				<Clock className="mr-2 text-gray-500" size={16} />
				<p className="text-sm text-gray-600">
					Backup Retrieved: {formatDate(log.date)}
				</p>
			</div>

			{log.Alerts && log.Alerts.length > 0 && (
				<div className="mt-2">
					<div className="flex items-center mb-2">
						<AlertTriangle className="mr-2 text-yellow-500" size={16} />
						<h3 className="text-md font-medium text-gray-700">Alerts</h3>
					</div>
					<ul className="space-y-1 pl-6 border-l-2 border-gray-100">
						{log.Alerts.map((alert, index) => (
							<li
								key={`${alert.key}-${index}`}
								className="text-sm text-gray-600 flex items-center"
							>
								<span className="mr-2 text-xs text-yellow-600">•</span>
								{alert.key.replace(/_/g, " ")}
								<span className="ml-2 text-xs text-gray-500">
									@ {formatAlertTime(alert.time)}
								</span>
							</li>
						))}
						<li className="text-sm text-gray-600 flex items-center">
							<span className="mr-2 text-xs text-yellow-600">•</span>
							... {log.RemainingAlerts} more.
						</li>
					</ul>
				</div>
			)}
		</div>
	)
}

BackupLogItem.propTypes = {
	log: PropTypes.shape({
		Hostname: PropTypes.string.isRequired,
		date: PropTypes.string.isRequired,
		Alerts: PropTypes.arrayOf(
			PropTypes.shape({
				key: PropTypes.string.isRequired,
				time: PropTypes.string.isRequired,
			})
		),
		RemainingAlerts: PropTypes.number,
	}).isRequired,
}

export default BackupLogItem
