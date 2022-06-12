using RPITest.Client;
using RPITest.Server;

if (args.Length == 0)
{
    return;
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
