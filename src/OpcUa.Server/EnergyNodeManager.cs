using Opc.Ua;
using Opc.Ua.Server;

namespace Ietp.OpcUa.Server;

public sealed class EnergyNodeManager : CustomNodeManager2
{
    private readonly ServerOptions _options;
    private readonly Dictionary<string, BaseDataVariableState> _variables = new(StringComparer.Ordinal);

    public EnergyNodeManager(IServerInternal server, ApplicationConfiguration configuration, ServerOptions options)
        : base(server, configuration, options.NamespaceUri)
    {
        _options = options;
        SystemContext.NodeIdFactory = this;
    }

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (Lock)
        {
            var root = new FolderState(null)
            {
                SymbolicName = _options.ServerId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType,
                NodeId = new NodeId($"server/{_options.ServerId}", NamespaceIndex),
                BrowseName = new QualifiedName(_options.ServerId, NamespaceIndex),
                DisplayName = $"IETP {_options.ServerId}"
            };
            root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            AddRootReference(externalReferences, root.NodeId);
            AddPredefinedNode(SystemContext, root);

            var asset = new FolderState(root)
            {
                SymbolicName = _options.AssetId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType,
                NodeId = new NodeId($"asset/{_options.AssetId}", NamespaceIndex),
                BrowseName = new QualifiedName(_options.AssetId, NamespaceIndex),
                DisplayName = _options.AssetId
            };
            root.AddChild(asset);
            AddPredefinedNode(SystemContext, asset);

            foreach (var (name, initialValue) in MeasurementCatalog.For(_options.Profile))
            {
                var variable = CreateVariable(asset, name, initialValue);
                _variables.Add(name, variable);
                AddPredefinedNode(SystemContext, variable);
            }
        }
    }

    public void Apply(IReadOnlyDictionary<string, object> measurements, DateTimeOffset timestamp, bool available)
    {
        lock (Lock)
        {
            foreach (var (name, variable) in _variables)
            {
                if (measurements.TryGetValue(name, out var value))
                {
                    variable.Value = value;
                }

                variable.Timestamp = timestamp.UtcDateTime;
                variable.StatusCode = available ? StatusCodes.Good : StatusCodes.BadNoCommunication;
                variable.ClearChangeMasks(SystemContext, false);
            }
        }
    }

    private BaseDataVariableState CreateVariable(NodeState parent, string name, object initialValue)
    {
        var variable = new BaseDataVariableState(parent)
        {
            SymbolicName = name,
            ReferenceTypeId = ReferenceTypeIds.HasComponent,
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
            NodeId = new NodeId($"asset/{_options.AssetId}/{name}", NamespaceIndex),
            BrowseName = new QualifiedName(name, NamespaceIndex),
            DisplayName = name.Replace('_', ' '),
            DataType = initialValue switch
            {
                double => DataTypeIds.Double,
                bool => DataTypeIds.Boolean,
                string => DataTypeIds.String,
                _ => throw new InvalidOperationException($"Unsupported OPC UA value type {initialValue.GetType().Name}.")
            },
            ValueRank = ValueRanks.Scalar,
            AccessLevel = AccessLevels.CurrentRead,
            UserAccessLevel = AccessLevels.CurrentRead,
            MinimumSamplingInterval = _options.UpdateIntervalMilliseconds,
            Historizing = false,
            Value = initialValue,
            StatusCode = StatusCodes.UncertainInitialValue,
            Timestamp = DateTime.UtcNow
        };
        parent.AddChild(variable);
        return variable;
    }

    private static void AddRootReference(IDictionary<NodeId, IList<IReference>> externalReferences, NodeId rootId)
    {
        if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var references))
        {
            externalReferences[ObjectIds.ObjectsFolder] = references = [];
        }

        references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, rootId));
    }
}
