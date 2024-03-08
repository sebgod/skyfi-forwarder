using System.IO.Ports;
using System.Net.Sockets;
using System.Text;

var device = "COM4"; //"/dev/ttyUSB0";
var baudRate = 9600; //115200;
var cts = new CancellationTokenSource();

Console.CancelKeyPress += (sender, args) =>
{
    if (cts.IsCancellationRequested)
    {
        Environment.Exit(-1);
    }
    cts.Cancel(args.SpecialKey == ConsoleSpecialKey.ControlBreak);
    args.Cancel = true;
};

Console.WriteLine("INFO: Starting to wait for commands on {0} using a baud rate of {1}", device, baudRate);

var serialPort = new SerialPort(device, baudRate)
{
    Encoding = Encoding.ASCII,
    ReadTimeout = 1000,
    WriteTimeout = 1000,
};
serialPort.Open();

var udp = new UdpClient(11880)
{
    DontFragment = true,
};

var readBuffer = new byte[100];

while (!cts.IsCancellationRequested)
{
    var req = await udp.ReceiveAsync(cts.Token);

    serialPort.Write(req.Buffer, 0, req.Buffer.Length);

#if DEBUG
    Console.WriteLine("<< {0}", CommandToDisplayString(req.Buffer));
#endif

    int bytesToRead;
    int waitedMs = 0;
    int bytesRead = 0;

    bool hasSeenError = false;
    bool hasSeenPound = false;

    while (!hasSeenError && !hasSeenPound && waitedMs < 5000)
    {
        while ((bytesToRead = serialPort.BytesToRead) == 0 && !cts.IsCancellationRequested && waitedMs < 5000)
        {
            await Task.Delay(1);
            waitedMs++;
        }

        bytesRead += serialPort.Read(readBuffer, bytesRead, bytesToRead);

        hasSeenError = readBuffer[0] == '!';
        hasSeenPound =  readBuffer[bytesRead - 1] == '\r';
    }

    var sendTask = udp.SendAsync(readBuffer, bytesRead, req.RemoteEndPoint);

#if DEBUG
    var sentMsg = CommandToDisplayString(readBuffer.AsSpan(0, bytesRead));
    Console.WriteLine(">> {0} [{1} ms]", sentMsg, waitedMs);
#endif
    var bytesSent = await sendTask;
    if (bytesSent != bytesRead)
    {
#if RELEASE
        var sentMsg = CommandToDisplayString(readBuffer.AsSpan(0, bytesRead));
#endif
        Console.Error.WriteLine("ERR: While sending {0}, expected length = {1} but sent {2}", sentMsg, bytesRead, bytesSent);
    }
}

string CommandToDisplayString(ReadOnlySpan<byte> bytes) => Encoding.ASCII.GetString(bytes).Replace("\r", "\\r").Replace("\n", "\n");