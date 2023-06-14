using ZajeciaIOT.Device;
using Microsoft.Azure.Devices.Client;
using System.IO;

//string deviceConnectionString = "HostName=zajecia-uniwerek.azure-devices.net;DeviceId=test;SharedAccessKey=0g9nbQE7u2C/g+ECjmNuu9pD0+JX8qdiIT42bvlNgTY=";
string deviceConnectionString = string.Empty;
string opcServerUrl = string.Empty;

string iniFilePath = "config.ini";

if (File.Exists(iniFilePath))
{
    var lines = File.ReadAllLines(iniFilePath);
    if (lines.Length >= 2)
    {
        opcServerUrl = lines[0];
        deviceConnectionString = lines[1];
    }
}
else
{
    Console.WriteLine("Enter the OPC server URL:");
    opcServerUrl = Console.ReadLine();

    Console.WriteLine("Enter the IoT Hub device connection string:");
    deviceConnectionString = Console.ReadLine();

    File.WriteAllLines(iniFilePath, new[] { opcServerUrl, deviceConnectionString });
}


using var deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Mqtt);
await deviceClient.OpenAsync();
var device = new VirtualDevice(deviceClient, opcServerUrl);

Console.WriteLine("Connection success");
await device.InitializeHandlers();
await device.UpdateTwinAsync();

await device.SendMessages(10, 1000);

Console.WriteLine("Finished! Press key to close...");
Console.ReadLine();