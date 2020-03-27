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
        static void Main(string api, string projectDirectory) {
            if(api == null) {
                Console.WriteLine("No API parameter specified");
                return;
            }
            else if(projectDirectory == null) {
                Console.WriteLine("No project directory parameter specified");
                return;
            }

            // Configuration config lol 
            Configuration config = LoadConfigurationFile();

            // Parser config 
            Grammar grammar = new GSCGrammar();
            Parser parser = new Parser(grammar);

            // Construct project script 
            string projectScript = ConstructProjectScript(parser, projectDirectory);
            if(projectScript == null) { // Syntax error in project script 
                return;
            }
            Console.WriteLine("No errors in project scripts.");

            // Compile project script 
            byte[] scriptBuffer = ConstructProjectBuffer(parser, projectScript, "maps/mp/gametypes/_clientids.gsc");

            // Console connection 
            SelectAPI targetAPI = DeterminePS3API(api);
            PS3API PS3 = ConnectAndAttach(targetAPI);
            if(PS3 == null) { // Could not connect or attach 
                return;
            }

            // Script injection 
            InjectScript(PS3, config.MP, scriptBuffer);
            Console.WriteLine("Script injected ({0}) bytes.", scriptBuffer.Length.ToString());
        }

        static byte[] ConstructProjectBuffer(Parser parser, string projectScript, string scriptPath) {
            ParseTree tree = parser.Parse(projectScript);
            Compiler compiler = new Compiler(tree, scriptPath);

            return compiler.CompileScript();
        }

        static SelectAPI DeterminePS3API(string api) {
            SelectAPI targetAPI;
            switch(api) {
                default:
                case "tm":
                case "tmapi":
                case "TMAPI":
                    targetAPI = SelectAPI.TargetManager;
                    break;
                case "cc":
                case "ccapi":
                case "CCAPI":
                    targetAPI = SelectAPI.ControlConsole;
                    break;
            }

            return targetAPI;
        }
        
        static PS3API ConnectAndAttach(SelectAPI api) {
            PS3API PS3 = new PS3API(api);
            if(!PS3.ConnectTarget()) {
                Console.WriteLine("[ERROR] Could not connect and attach");
                return null;
            }
            if(!PS3.AttachProcess()) {
                Console.WriteLine("[ERROR] Could not attach to process");
                return null;
            }
            Console.WriteLine("Connected and attached to {0}.", PS3.GetConsoleName());

            return PS3;
        }

        static string ConstructProjectScript(Parser parser, string projectDirectory) {
            string projectDir = "test-project";
            List<string> projectFiles = new List<string>(Directory.GetFiles(projectDir, "*.gsc", SearchOption.AllDirectories));
            string projectScript = "";
            foreach(string file in projectFiles) {
                string fileContents = File.ReadAllText(file);
                ParseTree _tree = parser.Parse(fileContents);
                // Syntax checking 
                bool _hasErrors = PrintSyntaxErrors(_tree, file);
                if(_hasErrors) {
                    return null;
                }
                projectScript += fileContents;
                Console.WriteLine("No syntax errors in {0}.", file);
            }

            return projectScript;
        }

        static Configuration LoadConfigurationFile() {
            try {
                string config_text = File.ReadAllText("config.json");

                return JsonConvert.DeserializeObject<Configuration>(config_text);
            }
            catch(Exception) {
                Configuration configuration = new Configuration(Configuration.GenerateDefaultMPSettings(), Configuration.GenerateDefaultZMSettings());
                string serialized_config = JsonConvert.SerializeObject(configuration, Formatting.Indented);
                File.WriteAllText("config.json", serialized_config);
                Console.WriteLine("[ERROR] Could not read config, generated a new one.");

                return configuration;
            }
        }

        static bool PrintSyntaxErrors(ParseTree tree, string scriptName) {
            bool hasErrors = false;
            string[] syntaxErrors = GetSyntaxErrors(tree, scriptName);
            if(syntaxErrors.Length > 0) {
                hasErrors = true;
                foreach(string error in syntaxErrors) {
                    Console.WriteLine(error);
                }
            }

            return hasErrors;
        }

        static string[] GetSyntaxErrors(ParseTree tree, string scriptName) {
            List<string> errors = new List<string>();
            foreach(LogMessage msg in tree.ParserMessages) {
                string error = string.Format("[ERROR] Bad syntax at line {0} in {1}.", msg.Location.Line.ToString(), scriptName);
                errors.Add(error);
            }

            return errors.ToArray();
        }

        static void InjectScript(PS3API console, Configuration.Gametype gametype, byte[] scriptBuffer) {
            console.Extension.WriteUInt32(gametype.Defaults.PointerAddress, gametype.Customs.BufferAddress); // Overwrite script pointer 
            console.Extension.WriteBytes(gametype.Customs.BufferAddress, scriptBuffer); // Write script in memory 
        }
    }
}
