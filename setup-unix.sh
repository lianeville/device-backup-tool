#!/bin/bash

# Navigate to the root directory and install dependencies
echo "Installing root dependencies..."
npm install

# Navigate to the Frontend directory and install dependencies
echo "Installing frontend dependencies..."
cd ./Frontend || exit
npm install
cd .. || exit

# Build and run the ASP.NET backend
echo "Building and running backend..."
cd ./DeviceManagerAPI || exit
dotnet build
dotnet run &
cd .. || exit

# Run "npm run start" from the root folder
echo "Starting application..."
npm run start

echo "Setup completed successfully. Run `npm run start` to start the application."