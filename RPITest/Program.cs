using System.Diagnostics;

using RPITest.Client;
using RPITest.Server;

if (args.Length == 0)
{
    return;
}

// Set process priority
using (var process = Process.GetCurrentProcess())
{
    process.PriorityClass = ProcessPriorityClass.High;
    process.ProcessorAffinity = new IntPtr(1);
    Thread.CurrentThread.Priority = ThreadPriority.Highest;
}

switch (args[0])
{
    case "client":
        new ClientApp(args).Run();
        break;

    case "server":
        new ServerApp(args).Run();
        break;
}
