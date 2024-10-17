using Newtonsoft.Json;

namespace DynamicModuleSystem
{
    public class Panel : IHardwareModule
    {
        protected SerialPortManager _communication = new SerialPortManager();

        protected string serialData;
        protected string previousData;

        protected Oid oidTarget = Oid.Undefined;
        protected List<Oid> Oids = new List<Oid>();

        public string Name;
        public string Port { get; }
        public bool Status { get; }

        public override String ToString()
        {
            return "Name: " + Name + " Port: " + Port + " Status: " + Status;
        }

        public Panel(string name, List<Oid> oids, string port, bool status)
        {
            Name = name;
            Oids = oids;
            Port = port;
            Status = status;
        }

        public virtual bool Initialize(IConfig config)
        {
            Console.WriteLine($"Initializing panel {Name}");
            return true;
        }

        public virtual bool Connect(IConfig config)
        {
            Console.WriteLine($"Connecting panel {Name}");
            _communication?.ScanForSerialPorts();
            _communication?.OpenPort(Port);
            _communication.DataReceived += (portName, data) => { serialData = data; };

            return true;
        }

        public virtual void Disconnect()
        {
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
            foreach (var currOid in Oids)
            {
                if (currOid.ToString() == parsed.Link)
                {
                    oidTarget = currOid;
                    break;
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

        protected void Log(string message)
        {
            Console.WriteLine(message);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var json = File.ReadAllText("./Config/oids.json");
            var modulesConfig = JsonConvert.DeserializeObject<ModulesConfig>(json);
            List<Panel> panels = new List<Panel>();

            foreach (var moduleEntry in modulesConfig.Modules)
            {
                var parentPanelName = moduleEntry.Key;
                foreach (var panelEntry in moduleEntry.Value)
                {
                    var panelName = panelEntry.Key;
                    var oids = panelEntry.Value.Oids;
                    var port = panelEntry.Value.Port;
                    var status = panelEntry.Value.Status;

                    if (!status)
                    {
                        Console.WriteLine($"{panelName} is not enabled");
                        continue;
                    }

                    var oidEnumList = oids.Select(oid => Enum.TryParse<Oid>(oid, out var parsedOid) ? parsedOid : Oid.Undefined).ToList();

                    var panel = new Panel(parentPanelName + "." + panelName, oidEnumList, port, status);
                    panels.Add(panel);
                }

                foreach (var panel in panels)
                {
                    Console.WriteLine("panel: " + panel);
                    panel.Initialize(new Config());
                    panel.Connect(new Config());

                    if (panel.Name == "Pedestal.Trim")
                    {
                        panel.OnDataReceived(panel.Name, "OverheadBrightForOledStep I 100");
                    }
                    else if (panel.Name == "Pedestal.Takis")
                    {
                        panel.OnDataReceived(panel.Name, "oid1 F 12.5");
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
        public event Action<string, string> DataReceived;

        private void OnDataReceived(object sender, EventArgs e)
        {
            Console.WriteLine("subscribed to OnDataReceived");
        }

        public void OpenPort(string port)
        {
            Console.WriteLine($"Opening port {port}...");
        }

        public void ScanForSerialPorts()
        {
            Console.WriteLine("Scanning for ports...");
        }

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
