using Opc.Ua;
using Opc.Ua.Server;

namespace Ietp.OpcUa.Server;

public sealed class IetpServer(ServerOptions options) : StandardServer
{
    public EnergyNodeManager? EnergyNodeManager { get; private set; }

    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
    {
        EnergyNodeManager = new EnergyNodeManager(server, configuration, options);
        return new MasterNodeManager(server, configuration, null, EnergyNodeManager);
    }
}
