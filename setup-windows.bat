@echo off

:: Navigate to the root directory and install dependencies
echo Installing root dependencies...
npm install

:: Navigate to the Frontend directory and install dependencies
echo Installing frontend dependencies...
cd Frontend
npm install
cd ..

:: Build and run the ASP.NET backend
echo Building and running backend...
cd DeviceManagerAPI
dotnet build
start /B dotnet run
cd ..

echo Setup completed successfully. Run `npm run start` to start the application.
