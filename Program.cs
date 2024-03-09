﻿using System.IO.Ports;
using System.Net.Sockets;
using System.Text;

var device = args.Length > 0 ? args[0] : Environment.OSVersion.Platform is PlatformID.Unix ? "/dev/ttyUSB0" : "COM4";
var baudRate = args.Length > 1 && int.TryParse(args[1], out var b) ? b : 9600;

using var cts = new CancellationTokenSource();

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

using var serialPort = new SerialPort(device, baudRate);

using var udp = new UdpClient(11880)
{
    DontFragment = true,
};

try
{
    await Loop(udp, serialPort, cts);
}
catch (OperationCanceledException)
{
    Console.WriteLine("INFO: Cancellation requested, exiting.");
}
catch (TimeoutException t)
{
    Console.Error.WriteLine("ERR: Timeout: {0}", t.Message);
}
catch (Exception e)
{
    Console.Error.WriteLine("ERR: Unexpected exception {0}", e.Message);
}

static async Task Loop(UdpClient udp, SerialPort serialPort, CancellationTokenSource cts)
{
    serialPort.Open();
    var stream = serialPort.BaseStream;

    var readBuffer = new byte[100];

    while (!cts.IsCancellationRequested)
    {
        var req = await udp.ReceiveAsync(cts.Token);

        await stream.WriteAsync(req.Buffer, cts.Token);

#if DEBUG
        Console.WriteLine("<< {0}", CommandToDisplayString(req.Buffer));

        if (":e1\r"u8.SequenceEqual(req.Buffer))
        {
            Console.WriteLine("INFO: Connection to {0} initialized from {1}", udp.Client.LocalEndPoint, req.RemoteEndPoint);
        }
#endif
        int bytesRead = 0;
        do
        {
            bytesRead += await serialPort.BaseStream.ReadAtLeastAsync(readBuffer.AsMemory(bytesRead), 1, true, cts.Token);
        } while (readBuffer[bytesRead - 1] != '\r');

        var sendTask = udp.SendAsync(readBuffer, bytesRead, req.RemoteEndPoint);

#if DEBUG
        var sentMsg = CommandToDisplayString(readBuffer.AsSpan(0, bytesRead));
        Console.WriteLine(">> {0}", sentMsg);
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
}

static string CommandToDisplayString(ReadOnlySpan<byte> bytes) => Encoding.ASCII.GetString(bytes).Replace("\r", "\\r").Replace("\n", "\n");