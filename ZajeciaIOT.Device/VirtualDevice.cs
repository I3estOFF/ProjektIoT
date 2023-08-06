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
        private readonly int deviceNumber;

        private int lastError = 0;
        private double lastProductionRate = 0;
        private int lastGood = 0;
        private int lastBad = 0;

        public VirtualDevice(DeviceClient deviceClient, string opcServerUrl, int deviceNumber)
        {
            this.deviceClient = deviceClient;
            this.opcClient = new OpcClient(opcServerUrl);
            this.deviceNumber = deviceNumber;
            this.opcClient.Connect();

            var commands = new OpcReadNode[]
                {
                    new OpcReadNode($"ns=2;s=Device {deviceNumber}/ProductionRate"),
                    new OpcReadNode($"ns=2;s=Device {deviceNumber}/GoodCount"),
                    new OpcReadNode($"ns=2;s=Device {deviceNumber}/BadCount"),
                    new OpcReadNode($"ns=2;s=Device {deviceNumber}/DeviceError")
                };

            var results = this.opcClient.ReadNodes(commands);
            var productionRate = Convert.ToDouble(results.ElementAt(0).Value);
            var goodCount = Convert.ToInt32(results.ElementAt(1).Value);
            var badCount = Convert.ToInt32(results.ElementAt(2).Value);
            var deviceError = Convert.ToInt32(results.ElementAt(3).Value.ToString());

            lastError = deviceError;
            lastGood = goodCount;
            lastBad = badCount;
            lastProductionRate = productionRate;
        }


        #region Sending Messages
        public async Task UpdateStatus()
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
            var deviceError = Convert.ToInt32(results.ElementAt(6).Value.ToString());

            var data = new
            {
                productionStatus,
                workorderId,
                temperature,
                goodDelta = goodCount - lastGood,
                badDelta = badCount - lastBad,
                type = "telemetry"
            };
            var dataString = JsonConvert.SerializeObject(data);
            Message eventMessage = new Message(Encoding.UTF8.GetBytes(dataString))
            {
                ContentType = MediaTypeNames.Application.Json,
                ContentEncoding = "utf-8"
            };
            eventMessage.Properties.Add("temperatureAlert", (data.temperature > 80) ? "true" : "false");
            Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Sending message for Device {deviceNumber}, Data: [{dataString}]");
            await this.deviceClient.SendEventAsync(eventMessage);

            if (deviceError != lastError)
            {
                var emergencyStop = (deviceError & 1) != (lastError & 1);
                var powerFailure = (deviceError & 2) != (lastError & 2);
                var sensorFailure = (deviceError & 4) != (lastError & 4);
                var unknown = (deviceError & 8) != (lastError & 8);

                var errorData = new { deviceError, emergencyStop, powerFailure, sensorFailure, unknown, type = "error" };
                var errorMessage = new Message(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(errorData))) {
                    ContentType = MediaTypeNames.Application.Json,
                    ContentEncoding = "utf-8"
                };
                Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Sending error message for Device {deviceNumber}, Error: {deviceError}");
                await this.deviceClient.SendEventAsync(errorMessage);
            }

            await UpdateDeviceTwin(productionRate, deviceError);

            lastError = deviceError;
            lastProductionRate = productionRate;
            lastGood = goodCount;
            lastBad = badCount;
        }

        private async Task UpdateDeviceTwin(double productionRate, int deviceError)
        {
            var updatedAny = false;
            var patch = new TwinCollection();

            if (lastProductionRate != productionRate)
            {
                updatedAny = true;
                patch["productionRate"] = productionRate;

            }

            if (lastError != deviceError) {
                updatedAny = true;
                patch["deviceError"] = deviceError;
            }

            if (updatedAny)
            {
                await deviceClient.UpdateReportedPropertiesAsync(patch);
            }
        }
        #endregion

        #region Direct Methods
        private async Task<MethodResponse> DirectMethodHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"\tMETHOD EXECUTED: {methodRequest.Name}");

            if (methodRequest.Name == "EmergencyStop")
            {
                Console.WriteLine("\tEmergency stop triggered!");

                await Task.Run(() =>
                {
                    var emergencyStopMethod = new OpcCallMethod($"ns=2;s=Device {deviceNumber}", $"ns=2;s=Device {deviceNumber}/EmergencyStop");
                    this.opcClient.CallMethod(emergencyStopMethod);
                });

                return new MethodResponse(200);
            }
            else if (methodRequest.Name == "ResetErrorStatus")
            {
                Console.WriteLine("\tResetting error status!");

                await Task.Run(() =>
                {
                    var resetErrorStatusMethod = new OpcCallMethod($"ns=2;s=Device {deviceNumber}", $"ns=2;s=Device {deviceNumber}/ResetErrorStatus");
                    this.opcClient.CallMethod(resetErrorStatusMethod);
                });

                return new MethodResponse(200);
            }

            return new MethodResponse(400);
        }

        #endregion

        public async Task InitializeHandlers()
        {
            await this.deviceClient.SetReceiveMessageHandlerAsync(OnC2dMessageReceivedAsync, this.deviceClient);

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
        private async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
            if (desiredProperties.Contains("productionRate"))
            {
                var newProductionRate = desiredProperties["productionRate"];
                var writeNode = new OpcWriteNode($"ns=2;s=Device {deviceNumber}/ProductionRate", (int) newProductionRate);
                this.opcClient.WriteNode(writeNode);
                Console.WriteLine($"newProductionRate is {newProductionRate}");
                Console.WriteLine($"\t{DateTime.Now}> Updating desired production rate from cloud. New production rate: {newProductionRate}");
            }
        }
        #endregion
    }
}
