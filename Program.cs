using Newtonsoft.Json;

namespace DynamicModuleSystem
{
    public class PanelInfo
    {
        public String Name { get; }
        public List<Oid> Oids { get; }
        public String Port { get; }
        public bool Status { get; }

        public PanelInfo(String name, List<Oid> oids, String port, bool status)
        {
            Name = name;
            Oids = oids;
            Port = port;
            Status = status;
        }
    }

    public class BaseModule : IHardwareModule
    {
        protected SerialPortManager _communication = new SerialPortManager();
        protected string serialData;
        protected string previousData;
        protected Oid oidTarget = Oid.Undefined;
        private readonly Dictionary<string, PanelInfo> _panels = new Dictionary<string, PanelInfo>();

        protected void Log(string message)
        {
            Console.WriteLine(message);
        }

        public virtual bool Initialize(IConfig config)
        {
            return true;
        }

        public virtual bool Connect(IConfig config)
        {
            return true;
        }

        public virtual void Disconnect()
        {
        }

        protected void RegisterPanel(Panel panel)
        {
            if (!_panels.ContainsKey(panel.Name))
            {
                var panelInfo = new PanelInfo(panel.Name, panel.Oids, panel.Port, panel.Status);
                _panels[panel.Name] = panelInfo;
            }
            else
            {
                throw new ArgumentException($"Panel '{panel.Name}' is already registered");
            }
        }

        public void OnDataReceived(String panelName, string data)
        {
            Log($"Module name: {panelName} has received: {data}");

            serialData = data;
            ProcessState();

            Console.WriteLine();
        }

        protected bool ProcessState(State state = null)
        {
            if (string.IsNullOrEmpty(serialData) || serialData == previousData)
            {
                return true;
            }

            Log($"Received data: {serialData}");
            string[] parts = serialData.Split(' ');

            if (parts.Length != 3)
            {
                Log($"Received invalid data format: {serialData}");
                return false;
            }

            ParsedData parsed = new ParsedData(parts[0], parts[1], parts[2]);
            foreach (var panelEntry in _panels)
            {
                var currOids = panelEntry.Value.Oids;

                foreach (var currOid in currOids)
                {
                    if (currOid.ToString() == parsed.Link)
                    {
                        oidTarget = currOid;
                        break;
                    }
                }
            }

            if (oidTarget == Oid.Undefined)
            {
                Log($"Invalid Oid: {parsed.Link}");
                return false;
            }

            if (state == null)
            {
                state = new State();
            }

            return UpdateState(state, parsed);
        }

        protected virtual bool UpdateState(State state, ParsedData parsed)
        {
            Console.WriteLine("changing state and writing to arduino.....");
            Console.WriteLine("Parsed: " + parsed);
            Console.WriteLine("oidTarget: " + oidTarget);

            string jsonString = JsonConvert.SerializeObject(parsed);

            try
            {
                switch (parsed.Type)
                {
                    case "I":
                        state.Set(oidTarget, int.Parse(parsed.Value));
                        _communication.WriteToPort(jsonString);
                        Log($"Set {oidTarget} to {state.GetInt(oidTarget)}");
                        break;
                    case "F":
                        state.Set(oidTarget, float.Parse(parsed.Value));
                        _communication.WriteToPort(jsonString);
                        Log($"Set {oidTarget} to {state.GetFloat(oidTarget)}");
                        break;
                    case "B":
                        state.Set(oidTarget, bool.Parse(parsed.Value));
                        _communication.WriteToPort(jsonString);
                        Log($"Set {oidTarget} to {state.GetBool(oidTarget)}");
                        break;
                    default:
                        Log($"Unknown type {parsed.Type} for Oid {oidTarget}");
                        return false;
                }
            }
            catch (FormatException ex)
            {
                Log($"Format error while parsing value: {parsed.Value} Exception: {ex.Message}");
                return false;
            }

            previousData = serialData;
            return true;
        }
    }

    public class Module : BaseModule
    {
        public string Name { get; private set; }
        public List<Panel> Panels { get; private set; }

        public Module(string name)
        {
            Name = name;
            Panels = new List<Panel>();
        }

        public void AddPanel(Panel panel)
        {
            Panels.Add(panel);
            RegisterPanel(panel);
        }

        public void InitializeAll(IConfig config)
        {
            Initialize(config);
            foreach (var panel in Panels)
            {
                panel.Initialize(config);
            }
        }

        public void ConnectAll(IConfig config)
        {
            Connect(config);
            foreach (var panel in Panels)
            {
                panel.Connect(config);
            }
        }

        public void DisconnectAll()
        {
            Disconnect();
            foreach (var panel in Panels)
            {
                panel.Disconnect();
            }
        }
    }

    public class Panel : BaseModule
    {
        public string Name { get; private set; }
        public List<string> OidsStrings { get; private set; }
        public List<Oid> Oids = new List<Oid>();
        public string Port { get; private set; }
        public bool Status { get; private set; }

        public Panel(string name, List<string> oids, string port, bool status)
        {
            Name = name;
            OidsStrings = oids;
            Port = port;
            Status = status;

            foreach (var oid in OidsStrings)
            {
                if (Enum.TryParse(oid, out Oid parsedOid))
                {
                    if (Oids.Contains(parsedOid))
                    {
                        throw new ArgumentException($"OID '{parsedOid}' is already assigned to this panel '{Name}'. OIDs must be unique within each panel.");
                    }

                    Oids.Add(parsedOid);
                }
                else
                {
                    Log($"Warning: OID '{oid}' could not be parsed into Oid");
                }
            }
        }

        public override string ToString()
        {
            return $"{Name} on {Port} with OIDs: {string.Join(", ", Oids)}";
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var json = File.ReadAllText("./Config/oids.json");
            var modulesConfig = JsonConvert.DeserializeObject<ModulesConfig>(json);

            foreach (var moduleEntry in modulesConfig.Modules)
            {
                var module = new Module(moduleEntry.Key);
                foreach (var panelEntry in moduleEntry.Value)
                {
                    var panelName = panelEntry.Key;
                    var oids = panelEntry.Value.Oids;
                    var port = panelEntry.Value.Port;
                    var status = panelEntry.Value.Status;

                    var panel = new Panel(module.Name + "." + panelName, oids, port, status);
                    module.AddPanel(panel);
                }

                module.InitializeAll(new Config());
                module.ConnectAll(new Config());

                foreach (var panel in module.Panels)
                {
                    if (!panel.Status)
                    {
                        Console.WriteLine($"{panel.Name} is not enabled");
                        continue;
                    }

                    if (panel.Name == "Pedestal.Trim")
                    {
                        module.OnDataReceived(panel.Name, "OverheadBrightForOledStep I 100");
                    }
                    else if (panel.Name == "Pedestal.Takis")
                    {
                        module.OnDataReceived(panel.Name, "oid1 F 12.5");
                    }
                }

                Console.WriteLine();
            }
        }
    }

    public class ModulesConfig
    {
        public Dictionary<string, Dictionary<string, PanelConfig>> Modules { get; set; }
    }

    public class PanelConfig
    {
        public List<string> Oids { get; set; }
        public string Port { get; set; }
        public bool Status { get; set; }
    }

    public class State
    {
        private Dictionary<Oid, object> stateData = new Dictionary<Oid, object>();

        public void Set(Oid oid, int value) => stateData[oid] = value;
        public void Set(Oid oid, float value) => stateData[oid] = value;
        public void Set(Oid oid, bool value) => stateData[oid] = value;

        public int GetInt(Oid oid) => (int)stateData[oid];
        public float GetFloat(Oid oid) => (float)stateData[oid];
        public bool GetBool(Oid oid) => (bool)stateData[oid];
    }

    public enum Oid
    {
        Undefined,
        OverheadBrightForOledStep,
        OverheadBrightForLedStep,
        oid1,
        oid2,
        oidA1,
        oidA2,
        oidB1,
        oidB2,
        Takis,
    }

    public class ParsedData
    {
        public string Link { get; }
        public string Type { get; }
        public string Value { get; }

        public ParsedData(string link, string type, string value)
        {
            Link = link;
            Type = type;
            Value = value;
        }

        public override String ToString()
        {
            return Link + " " + Type + " " + Value;
        }
    }

    public class SerialPortManager
    {
        public void WriteToPort(string text)
        {
            Console.WriteLine("Writing to port...: " + text);
        }
    }

    public interface IHardwareModule
    {
        bool Initialize(IConfig config);
        bool Connect(IConfig config);
        void Disconnect();
    }

    public class Config : IConfig { }

    public interface IConfig
    {

    }
}
