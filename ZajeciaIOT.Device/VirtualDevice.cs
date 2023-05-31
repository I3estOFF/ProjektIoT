using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using Opc.UaFx.Client;
using Opc.UaFx;
using System;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;

namespace ZajeciaIOT.Device
{
    public class VirtualDevice
    {
        private readonly DeviceClient deviceClient;
        private readonly OpcClient opcClient;

        public VirtualDevice(DeviceClient deviceClient)
        {
            this.deviceClient = deviceClient;
            this.opcClient = new OpcClient("opc.tcp://localhost:4840/");
            this.opcClient.Connect();
        }

        #region Sending Messages
        public async Task SendMessages(int nrOfMessages, int delay)
        {
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

                    var results = this.opcClient.ReadNodes(commands);

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
                    eventMessage.ContentEncoding = "utf-8";
                    eventMessage.Properties.Add("temperatureAlert", (data.temperature > 80) ? "true" : "false");

                    Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Sending message for Device {deviceNumber}, Message: {i}, Data: [{dataString}]");

                    await this.deviceClient.SendEventAsync(eventMessage);
                    if (i < nrOfMessages - 1)
                        await Task.Delay(delay);
                }
            }

            Console.WriteLine();
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

        private async Task<MethodResponse> DirectMethodHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"\tMETHOD EXECUTED: {methodRequest.Name}");

            if (methodRequest.Name == "EmergencyStop")
            {
                Console.WriteLine("\tEmergency stop triggered!");

                await Task.Run(() =>
                {
                    var emergencyStopMethod = new OpcCallMethod("ns=2;s=Device 1", "ns=2;s=Device 1/EmergencyStop");
                    this.opcClient.CallMethod(emergencyStopMethod);
                });

                return new MethodResponse(200);
            }
            else if (methodRequest.Name == "ResetErrorStatus")
            {
                Console.WriteLine("\tResetting error status!");

                await Task.Run(() =>
                {
                    var resetErrorStatusMethod = new OpcCallMethod("ns=2;s=Device 1", "ns=2;s=Device 1/ResetErrorStatus");
                    this.opcClient.CallMethod(resetErrorStatusMethod);
                });

                return new MethodResponse(200);
            }

            return new MethodResponse(400);
        }

        private async Task<MethodResponse> DefaultServiceHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"\tMETHOD EXCUTED: {methodRequest.Name}");

            await Task.Delay(1000);

            return new MethodResponse(0);
        }
        #endregion

        public async Task InitializeHandlers()
        {
            await this.deviceClient.SetReceiveMessageHandlerAsync(OnC2dMessageReceivedAsync, this.deviceClient);

            await this.deviceClient.SetMethodDefaultHandlerAsync(DefaultServiceHandler, this.deviceClient);
            await this.deviceClient.SetMethodHandlerAsync("SendMessages", SendMessagesHandler, this.deviceClient);
            await this.deviceClient.SetMethodDefaultHandlerAsync(DirectMethodHandler, this.deviceClient);

            await this.deviceClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, this.deviceClient);
        }

        #region Receive Messages
        private async Task OnC2dMessageReceivedAsync(Message receivedMessage, object _)
        {
            Console.WriteLine($"\t{DateTime.Now}> C2D message callback = message received with Id={receivedMessage.MessageId}");
            PrintMessages(receivedMessage);
            await this.deviceClient.CompleteAsync(receivedMessage);
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
                Console.WriteLine($"\t\tProperty[{propCount++}]> Key={prop.Key} : Value={prop.Value}");
            }
        }
        #endregion

        #region Device Twin
        public async Task UpdateTwinAsync()
        {
            var twin = await this.deviceClient.GetTwinAsync();

            Console.WriteLine($"\nOdebrano początkową wartość twin:\n{JsonConvert.SerializeObject(twin, Formatting.Indented)}");
            Console.WriteLine();

            var reportedProperties = new TwinCollection();

            for (int deviceNumber = 1; deviceNumber <= 5; deviceNumber++)
            {
                var commands = new OpcReadNode[]
                {
            new OpcReadNode($"ns=2;s=Device {deviceNumber}/ProductionRate"),
            new OpcReadNode($"ns=2;s=Device {deviceNumber}/DeviceError")
                };

                var results = this.opcClient.ReadNodes(commands);

                var productionRate = Convert.ToDouble(results.ElementAt(0).Value);
                var deviceError = results.ElementAt(1).Value.ToString();

                var deviceInfo = new
                {
                    ProductionRate = productionRate,
                    DeviceError = deviceError
                };

                reportedProperties[$"Device{deviceNumber}"] = JsonConvert.SerializeObject(deviceInfo);
            }

            var twinPatch = new TwinCollection();
            if (reportedProperties.Count > 0)
            {
                twinPatch["properties"] = reportedProperties;
            }

            await this.deviceClient.UpdateReportedPropertiesAsync(twinPatch);

            Console.WriteLine($"\nStan twin po aktualizacji właściwości:\n{JsonConvert.SerializeObject(await this.deviceClient.GetTwinAsync(), Formatting.Indented)}");
            Console.WriteLine();
        }

        private async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
            Console.WriteLine($"\tDesired property changed: {desiredProperties.ToJson()}");

            await UpdateTwinAsync();
        }
        }
    #endregion
}

