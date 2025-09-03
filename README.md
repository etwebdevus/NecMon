# Peer Viewer - Peer-to-Peer Network Screenshot Viewer

A C# Windows Forms application built with .NET Framework 4.8 that enables peer-to-peer network communication and screenshot viewing across connected machines.

## Features

- **Peer Discovery**: Automatically discovers other machines running the application on the local network
- **Real-time Screenshots**: View live screenshots from connected peers
- **Network Communication**: TCP-based peer-to-peer communication
- **User-friendly Interface**: Split-pane design with peer list and screenshot viewer
- **Automatic Reconnection**: Handles network disconnections gracefully

## Architecture

The application consists of several key components:

### Core Components

- **PeerDiscovery**: Handles UDP broadcast discovery and TCP service listening
- **PeerConnection**: Manages individual peer connections and data transfer
- **ScreenshotService**: Captures and manages screenshots locally and remotely
- **MainForm**: Main user interface with peer list and screenshot viewer

### Network Protocol

- **Discovery Port**: 8888 (UDP broadcast)
- **Service Port**: 8889 (TCP connections)
- **Message Format**: Simple text-based protocol with binary screenshot data

## Requirements

- Windows operating system
- .NET Framework 4.8
- Network access (for peer discovery and communication)
- Windows Firewall may need to allow the application

## Building the Application

1. Open the solution file `PeerViewer.sln` in Visual Studio
2. Ensure .NET Framework 4.8 is installed
3. Build the solution (Build > Build Solution)
4. Run the application (Debug > Start Debugging)

## Usage

### Starting the Application

1. Launch the application on multiple machines on the same network
2. The application will automatically start peer discovery
3. Peers will appear in the left panel as they are discovered

### Connecting to Peers

1. Select a peer from the list in the left panel
2. Click the "Connect" button to establish a connection
3. Once connected, screenshots will automatically be received and displayed
4. Use the "Disconnect" button to close the connection

### Viewing Screenshots

- Screenshots are displayed in the right panel
- The status bar shows screenshot information and timestamps
- Screenshots are automatically updated when received from peers

### Network Discovery

- The application broadcasts discovery messages every 30 seconds
- Peers are automatically detected and added to the list
- Offline peers are automatically removed after 2 minutes of inactivity

## Network Configuration

### Ports Used

- **Port 8888**: UDP broadcast for peer discovery
- **Port 8889**: TCP for screenshot data transfer

### Firewall Considerations

If you encounter connection issues:

1. Ensure Windows Firewall allows the application
2. Check that ports 8888 and 8889 are not blocked
3. Verify network policies allow peer-to-peer communication

## Troubleshooting

### Common Issues

1. **No Peers Discovered**
   - Check network connectivity
   - Verify firewall settings
   - Ensure multiple instances are running on different machines

2. **Connection Failures**
   - Check if target machine is online
   - Verify network permissions
   - Check for antivirus interference

3. **Screenshot Not Displaying**
   - Verify peer connection is established
   - Check if screenshot capture is working on target machine
   - Ensure sufficient network bandwidth

### Debug Information

The status bar at the bottom of the application provides real-time information about:
- Connection status
- Peer discovery events
- Error messages
- Screenshot information

## Security Considerations

- The application runs on local network only
- No encryption is implemented for screenshot data
- Screenshots may contain sensitive information
- Use only on trusted networks

## Development

### Project Structure

```
PeerViewer/
├── Models/           # Data models
├── Network/          # Network communication classes
├── Properties/       # Assembly and settings files
├── MainForm.cs       # Main user interface
├── Program.cs        # Application entry point
└── PeerViewer.csproj # Project file
```

### Key Classes

- **PeerInfo**: Represents peer connection information
- **ScreenshotData**: Contains screenshot data and metadata
- **PeerConnection**: Manages individual peer connections
- **PeerDiscovery**: Handles network discovery and service hosting

## License

This project is provided as-is for educational and development purposes.

## Contributing

Feel free to submit issues, feature requests, or pull requests to improve the application.

## Support

For technical support or questions, please refer to the troubleshooting section or create an issue in the project repository.