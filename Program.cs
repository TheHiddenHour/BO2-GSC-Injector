using PS3Lib;
using BO2GSCCompiler;
using Irony.Parsing;
using Irony;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace bo2_gsc_injector {
    class Program {
        static void Main(string[] args) {
            if(args.Length < 1) {
                Console.WriteLine("[ERROR] No API parameter specified");
                return;
            }
            else if(args.Length < 2) {
                Console.WriteLine("[ERROR] No project directory parameter specified");
                return;
            }

            string api = args[0];
            string projectDirectory = args[1];

            // Configuration config lol 
            Configuration config = LoadConfigurationFile();

            // Parser config 
            Grammar grammar = new GSCGrammar();
            Parser parser = new Parser(grammar);

            // Check if project contains main.gsc at root 
            bool projectHasMain = ProjectContainsMain(projectDirectory);
            if(!projectHasMain) {
                Console.WriteLine("[ERROR] main.gsc not found in root of {0}", projectDirectory);
                return;
            }

            // Construct project script 
            string projectScript = ConstructProjectScript(parser, projectDirectory);
            if(projectScript == null) { // Syntax error in project script 
                return;
            }

            // Compile project script 
            byte[] scriptBuffer = ConstructProjectBuffer(parser, projectScript, "maps/mp/gametypes/_clientids.gsc");

            // Console connection 
            PS3API PS3 = ConnectAndAttach(DeterminePS3API(api));
            if(PS3 == null) { // Could not connect or attach 
                return;
            }

            // Script injection 
            InjectScript(PS3, config.MP, scriptBuffer);
            Console.WriteLine("Script injected ({0}) bytes.", scriptBuffer.Length.ToString());
        }

        static Configuration LoadConfigurationFile() {
            string config_path = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "config.json");
            try {
                string config_text = File.ReadAllText(config_path);

                return JsonConvert.DeserializeObject<Configuration>(config_text);
            }
            catch (Exception) {
                Configuration configuration = new Configuration(Configuration.GenerateDefaultMPSettings(), Configuration.GenerateDefaultZMSettings());
                string serialized_config = JsonConvert.SerializeObject(configuration, Formatting.Indented);
                File.WriteAllText(config_path, serialized_config);
                Console.WriteLine("[ERROR] Could not read config, generated a new one.");

                return configuration;
            }
        }

        static bool ProjectContainsMain(string projectDirectory) {
            string main_location = Path.Combine(projectDirectory, "main.gsc");

            return File.Exists(main_location);
        }

        static string ConstructProjectScript(Parser parser, string projectDirectory) {
            // Add main.gsc to the top of the list 
            List<string> projectFiles = new List<string>(Directory.GetFiles(projectDirectory, "*.gsc", SearchOption.AllDirectories));
            for (int i = 0; i < projectFiles.Count; i++) {
                string file = projectFiles[i];
                if (Path.GetFileName(file) == "main.gsc") {
                    string main = projectFiles[i];

                    projectFiles.RemoveAt(i);
                    projectFiles.Insert(0, main);

                    break;
                }
            }

            // Actually construct script 
            string projectScript = "";
            foreach (string file in projectFiles) {
                string fileContents = File.ReadAllText(file);
                ParseTree _tree = parser.Parse(fileContents);
                if (_tree.ParserMessages.Count > 0) { // If there are any messages at all (messages are parser errors) 
                    // Syntax checking 
                    LogMessage msg = _tree.ParserMessages[0];
                    string msgStr = string.Format("[ERROR] Bad syntax at line {0} in {1}.", msg.Location.Line.ToString(), file);
                    Console.WriteLine(msgStr);

                    return null;
                }

                projectScript += fileContents + "\n";
            }

            return projectScript;
        }

        static byte[] ConstructProjectBuffer(Parser parser, string projectScript, string scriptPath) {
            ParseTree tree = parser.Parse(projectScript);
            Compiler compiler = new Compiler(tree, scriptPath);

            return compiler.CompileScript();
        }

        static SelectAPI DeterminePS3API(string api) {
            switch(api) {
                default:
                case "tm":
                case "TM":
                case "tmapi":
                case "TMAPI":
                    return SelectAPI.TargetManager;
                case "cc":
                case "CC":
                case "ccapi":
                case "CCAPI":
                    return SelectAPI.ControlConsole;
                    break;
            }
        }
        
        static PS3API ConnectAndAttach(SelectAPI api) {
            PS3API PS3 = new PS3API(api);

            try {
                if (!PS3.ConnectTarget()) {
                    Console.WriteLine("[ERROR] Could not connect and attach.");
                    return null;
                }
                if (!PS3.AttachProcess()) {
                    Console.WriteLine("[ERROR] Could not attach to process.");
                    return null;
                }
                Console.WriteLine("Connected and attached to {0}.", PS3.GetConsoleName());

                return PS3;
            }
            catch {
                Console.WriteLine("[ERROR] Could not connect or attach. Check internet connection.");
                return null;
            }
        }

        static void InjectScript(PS3API console, Configuration.Gametype gametype, byte[] scriptBuffer) {
            console.Extension.WriteUInt32(gametype.Defaults.PointerAddress, gametype.Customs.BufferAddress); // Overwrite script pointer 
            console.Extension.WriteBytes(gametype.Customs.BufferAddress, scriptBuffer); // Write script in memory 
        }
    }
}
