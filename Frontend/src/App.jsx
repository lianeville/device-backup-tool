import AddDevice from "./components/AddDevice"
import DeviceList from "./components/DeviceList"

function App() {
	return (
		<div className="h-lvh w-lvw max-w-lvw">
			<DeviceList />
			<AddDevice />
		</div>
	)
}

export default App
