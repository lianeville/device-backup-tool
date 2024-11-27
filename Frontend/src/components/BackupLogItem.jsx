import React from "react"
import { Clock, AlertTriangle, Server } from "lucide-react"
import PropTypes from "prop-types"
import dateFormat from "dateformat"

const BackupLogItem = ({ log }) => {
	log.Alerts.forEach((alert, i) => {
		const time = alert.time * 1000
		log.Alerts[i].time = dateFormat(time, "mmm dd, yyyy HH:MM")
	})

	return (
		<div className="pt-2">
			<div className="flex items-center">
				<Clock className="mr-2 text-gray-500" size={16} />
				<p className="text-sm text-gray-600">
					Backup Retrieved: {log.date}
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
								className="text-sm text-gray-600 flex items-center w-full"
							>
								<span className="mr-2 text-xs text-yellow-600">•</span>
								{alert.key.replace(/_/g, " ")}
								<div className="mx-2 flex-grow border-t border-gray-200 h-px"></div>
								<span className="text-xs text-gray-400">
									@ {alert.time}
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
