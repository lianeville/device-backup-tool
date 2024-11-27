import React from "react"
import { Clock, AlertTriangle, Download } from "lucide-react"
import PropTypes from "prop-types"
import dateFormat from "dateformat"

const BackupLogItem = ({ log }) => {
	log.Alerts.forEach((alert, i) => {
		let rawTime = alert.time
		if (!rawTime || isNaN(parseInt(rawTime))) {
			return
		}
		let time = new Date(parseInt(rawTime))
		time = dateFormat(time, "mmm dd, yyyy HH:MM")
		log.Alerts[i].time = time
	})

	const handleDownloadSource = () => {
		// Create a blob from the source JSON
		const sourceBlob = new Blob([JSON.stringify(log.SourceText, null, 2)], {
			type: "application/json",
		})

		// Create a download link
		const downloadLink = document.createElement("a")
		downloadLink.href = URL.createObjectURL(sourceBlob)
		downloadLink.download = `${log.Hostname}_backup_Unencrypted.json`

		// Trigger the download
		document.body.appendChild(downloadLink)
		downloadLink.click()

		// Clean up
		document.body.removeChild(downloadLink)
		URL.revokeObjectURL(downloadLink.href)
	}

	const handleDownloadOriginalBackup = () => {
		if (!log.OriginalBackupFile) return

		// Decode Base64 content
		const decodedContent = atob(log.OriginalBackupFile.Base64Content)

		// Create a blob from the decoded content
		const blob = new Blob([decodedContent], {
			type: "application/octet-stream",
		})

		// Create a download link
		const downloadLink = document.createElement("a")
		downloadLink.href = URL.createObjectURL(blob)
		downloadLink.download =
			log.OriginalBackupFile.FileName || `${log.Hostname}_original_backup`

		// Trigger the download
		document.body.appendChild(downloadLink)
		downloadLink.click()

		// Clean up
		document.body.removeChild(downloadLink)
		URL.revokeObjectURL(downloadLink.href)
	}

	return (
		<div className="py-2">
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
					<ul className="space-y-1 pl-2 border-l-2 border-gray-100">
						{log.Alerts.map((alert, index) => (
							<li
								key={`${alert.key}-${index}`}
								className="text-sm text-gray-600 flex items-center w-full"
							>
								<span className="mr-2 text-xs text-gray-500">•</span>
								{alert.key.replace(/_/g, " ")}
								<div className="mx-2 flex-grow border-t border-gray-200 h-px"></div>
								<span className="text-xs text-gray-400">
									@ {alert.time}
								</span>
							</li>
						))}
						<li className="text-sm text-gray-600 flex items-center">
							<span className="mr-2 text-xs text-gray-500">•</span>
							... {log.RemainingAlerts} more.
						</li>
					</ul>
				</div>
			)}

			<div className="mt-4 flex space-x-2">
				{log.SourceText && (
					<button
						onClick={handleDownloadSource}
						className="flex items-center text-sm px-2 py-1 bg-blue-500 text-white rounded-md hover:bg-blue-600 transition-colors"
					>
						<Download className="mr-2" size={16} />
						Download Decrypted Backup (JSON)
					</button>
				)}

				{log.OriginalBackupFile && (
					<button
						onClick={handleDownloadOriginalBackup}
						className="flex items-center text-sm px-2 py-1 bg-green-500 text-white rounded-md hover:bg-green-600 transition-colors"
					>
						<Download className="mr-2" size={16} />
						Download Original Backup File (UNF)
					</button>
				)}
			</div>
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
		SourceText: PropTypes.string,
		OriginalBackupFile: PropTypes.shape({
			FileName: PropTypes.string,
			Base64Content: PropTypes.string,
		}),
	}).isRequired,
}

export default BackupLogItem
