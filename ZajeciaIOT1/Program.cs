using ZajeciaIOT.Device;
using Microsoft.Azure.Devices.Client;
using System.IO;
using System.Xml;

//string deviceConnectionString = "HostName=zajecia-uniwerek.azure-devices.net;DeviceId=test;SharedAccessKey=0g9nbQE7u2C/g+ECjmNuu9pD0+JX8qdiIT42bvlNgTY=";

const string iniFilePath = "config.ini";
const string exampleConfig = @"
    opcServerUrl = test 123
    delay = 5000
    deviceCount = 3
    connectionString1 = bla bla
    connectionString2 = testsetrestr
    connectionString3 = 123123123123123
";

if (File.Exists(iniFilePath))
{
    try
    {
        var values = new Dictionary<string, string>();
        var lines = File.ReadAllLines(iniFilePath);
        foreach (var line in lines)
        {
            var split = line.Split("=", 2);
            values.Add(split[0].Trim(), split[1].Trim());
        }

        var url = values["opcServerUrl"];
        var deviceCount = Int32.Parse(values["deviceCount"]);
        var delay = Int32.Parse(values["delay"]);

        var connectionStrings = new string[deviceCount];
        for (int i = 0; i < deviceCount; i++)
        {
            connectionStrings[i] = values[String.Format("connectionString{0}", i + 1)];
        }

        var virtualDevices = new VirtualDevice[deviceCount];

        for (int i = 0; i < deviceCount; i++)
        {
            var connectionString = connectionStrings[i];
            var deviceClient = DeviceClient.CreateFromConnectionString(connectionString, TransportType.Mqtt);
            await deviceClient.OpenAsync();

            virtualDevices[i] = new VirtualDevice(deviceClient, url, i+1);
            await virtualDevices[i].InitializeHandlers();
        }

        var tasks = new Task[deviceCount];
        while (true)
        {
            for (int i = 0; i < deviceCount; i++ ) {
                tasks[i] = virtualDevices[i].UpdateStatus();
            }
            await Task.Delay(delay);
        }
    }
    catch (Exception exc)
    {
        Console.WriteLine("Error while running the program. Error - {0}", exc.Message);
    }
}
else
{
    Console.WriteLine("Could not find the config.xml file. It is required for the program to run.");
    return;
}
