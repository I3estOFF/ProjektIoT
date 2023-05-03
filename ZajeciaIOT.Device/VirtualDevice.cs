using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using Opc.UaFx.Client;
using Opc.UaFx;
using System.Net.Mime;
using System.Text;

namespace ZajeciaIOT.Device
{
    public class VirtualDevice
    {
        private readonly DeviceClient deviceClient;

        public VirtualDevice(DeviceClient deviceClient)
            {
            this.deviceClient = deviceClient;
        }
        #region Sending Messages
        public async Task SendMessages(int nrOfMessages, int delay)
        {
            using (var client = new OpcClient("opc.tcp://localhost:4840/"))
            {
                client.Connect();

                Console.WriteLine($"Device sending {nrOfMessages} messages to IoTHub...\n");

                for (int deviceNumber = 1; deviceNumber <= 5; deviceNumber++)
                {
                    for (int i = 0; i < nrOfMessages; i++)
                    {
                        var commands = new OpcReadNode[]
                        {
                    new OpcReadNode($"ns=2;s=Device {deviceNumber}/ProductionStatus"),
                    new OpcReadNode($"ns=2;s=Device {deviceNumber}/ProductionRate"),
                    new OpcReadNode($"ns=2;s=Device {deviceNumber}/WorkorderId"),
                    new OpcReadNode($"ns=2;s=Device {deviceNumber}/Temperature"),
                    new OpcReadNode($"ns=2;s=Device {deviceNumber}/GoodCount"),
                    new OpcReadNode($"ns=2;s=Device {deviceNumber}/BadCount"),
                    new OpcReadNode($"ns=2;s=Device {deviceNumber}/DeviceError")
                        };

                        var results = client.ReadNodes(commands);

                        var productionStatus = results.ElementAt(0).Value.ToString();
                        var productionRate = Convert.ToDouble(results.ElementAt(1).Value);
                        var workorderId = results.ElementAt(2).Value.ToString();
                        var temperature = Convert.ToDouble(results.ElementAt(3).Value);
                        var goodCount = Convert.ToInt32(results.ElementAt(4).Value);
                        var badCount = Convert.ToInt32(results.ElementAt(5).Value);
                        var deviceError = results.ElementAt(6).Value.ToString();

                        var data = new
                        {
                            productionStatus,
                            productionRate,
                            workorderId,
                            temperature,
                            goodCount,
                            badCount,
                            deviceError
                        };

                        var dataString = JsonConvert.SerializeObject(data);

                        Message eventMessage = new Message(Encoding.UTF8.GetBytes(dataString));
                        eventMessage.ContentType = MediaTypeNames.Application.Json;
                        eventMessage.ContentEncoding = "utd-8";
                        eventMessage.Properties.Add("temperatureAlert", (data.temperature > 30) ? "true" : "false");
                        Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Sending message for Device {deviceNumber}, Message: {i}, Data: [{dataString}]");

                        await deviceClient.SendEventAsync(eventMessage);

                        if (i < nrOfMessages - 1)
                            await Task.Delay(delay);
                    }
                }
            }

            Console.WriteLine();
        }


        #endregion
        #region Receive Messages
        private async Task OnC2dMessageReceivedAsync(Message receivedMessage, object _)
        {
            Console.WriteLine($"\t{DateTime.Now}> C2D message callback = message received with Id={receivedMessage.MessageId}");
            PrintMessages(receivedMessage);
            await deviceClient.CompleteAsync(receivedMessage);
            Console.WriteLine($"\t{DateTime.Now}> Completed C2D message with Id={receivedMessage.MessageId}.");

            receivedMessage.Dispose();
        }

        private void PrintMessages(Message receivedMessage)
        {
            string messageData = Encoding.ASCII.GetString(receivedMessage.GetBytes());
            Console.WriteLine($"\t\tReceived message: {messageData}");

            int propCount = 0;
            foreach (var prop in receivedMessage.Properties)
            {
                Console.WriteLine($"\t\tProperty[{propCount++}> Key={prop.Key} : Valude={prop.Value}");
            }
        }

        #endregion
        #region Direct Methods
        private async Task<MethodResponse> SendMessagesHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"\tMETHOD EXCUTED: {methodRequest.Name}");

            var payload = JsonConvert.DeserializeAnonymousType(methodRequest.DataAsJson, new { nrOfMessages = default(int), delay = default(int) });
            await SendMessages(payload.nrOfMessages, payload.delay);

            return new MethodResponse(0);

        }

        private async Task<MethodResponse> DefaultServiceHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"\tMETHOD EXCUTED: {methodRequest.Name}");

            await Task.Delay(1000);

            return new MethodResponse(0);
        }

        #endregion
        #region Device Twin
        public async Task UpdateTwinAsync()
        {
            var twin = await deviceClient.GetTwinAsync();

            Console.WriteLine($"\n Initial twin value received: \n{JsonConvert.SerializeObject(twin, Formatting.Indented)}");
            Console.WriteLine();

            var reportedProperties = new TwinCollection();
            reportedProperties["DateTimeLastAppLaunch"] = DateTime.Now;

            await deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
        }

        private async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object _)
        {
            Console.WriteLine($"\tDesired property change:\n\t {JsonConvert.SerializeObject(desiredProperties)}");
            Console.WriteLine("\tSending current time as repreted property");
            TwinCollection reportedProperties = new TwinCollection();
            reportedProperties["DateTimeLastDesiredPropertyChangedReceived"] = DateTime.Now;

            await deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
        }
        #endregion
        public async Task InitializeHandlers()
        {
            await deviceClient.SetReceiveMessageHandlerAsync(OnC2dMessageReceivedAsync, deviceClient);

            await deviceClient.SetMethodDefaultHandlerAsync(DefaultServiceHandler, deviceClient);
            await deviceClient.SetMethodHandlerAsync("SendMessages", SendMessagesHandler, deviceClient);

            await deviceClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, deviceClient);
        }
    }
}