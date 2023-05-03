using ZajeciaIOT.Device;
using Microsoft.Azure.Devices.Client;

string deviceConnectionString = "HostName=zajecia-uniwerek.azure-devices.net;DeviceId=test;SharedAccessKey=0g9nbQE7u2C/g+ECjmNuu9pD0+JX8qdiIT42bvlNgTY=";

using var deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Mqtt);
await deviceClient.OpenAsync();
var device = new VirtualDevice(deviceClient);
Console.WriteLine("Connection success");
await device.InitializeHandlers();
await device.UpdateTwinAsync();

await device.SendMessages(1, 1000);

Console.WriteLine("Finished! Press key to close...");
Console.ReadLine();