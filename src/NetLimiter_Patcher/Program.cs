using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using dnlib.DotNet;
using dnlib.DotNet.Writer;
using OpCodes = dnlib.DotNet.Emit.OpCodes;

namespace NetLimiter_Patcher
{
    internal static class Program
    {
        private static readonly string SupposedDirectory = $@"{Environment.ExpandEnvironmentVariables("%ProgramW6432%")}\Locktime Software\NetLimiter 4";
        private static readonly string File2Patch = $@"{SupposedDirectory}\NetLimiter.dll";
        private static readonly string File2PatchBackup = $"{File2Patch}.bak";

        private static string _registrationName = "Mrakovic-ORG";

        public static void Main()
        {
            Console.Title = "NetLimiter Patcher";
            SetupInstruction();

            Console.ReadKey();
            Environment.Exit(0);
        }

        private static void SetupInstruction(string message = null)
        {
            Console.Clear();

            // In case there is an message display it. (it is supposed to be an issue message)
            if (message != null) Console.WriteLine($"{message}\nTry again.\n");

            // If it cannot find the supposed default DAEMON directory manually asks for the path
            if (!Directory.Exists(SupposedDirectory)) ManualPatchApp();

            // Else asks if it should patch automatically
            Console.Write(
                $"Successfully found the application directory at '{SupposedDirectory}'\nAre you willing to proceed to an automatic patch?\n\n0: No\n1: Yes\n# ");
            var choice = Console.ReadKey().Key;

            switch (choice)
            {
                case ConsoleKey.D0:
                case ConsoleKey.NumPad0:
                    Console.Clear();
                    ManualPatchApp();
                    break;
                case ConsoleKey.D1:
                case ConsoleKey.NumPad1:
                    Console.Clear();
                    AutomaticPatchApp();
                    break;
                default:
                    Console.Clear();
                    SetupInstruction();
                    break;
            }
        }

        private static void SaveModule(ModuleDefMD module, string fileName)
        {
            try
            {
                module.NativeWrite(fileName, new NativeModuleWriterOptions(module, false)
                {
                    Logger = DummyLogger.NoThrowInstance, MetadataOptions = {Flags = MetadataFlags.PreserveAll}
                });

                Console.WriteLine("Successfully patched.");
            }
            catch (Exception err)
            {
                Console.WriteLine($"\nFailed to save file.\n{err.Message}");
            }
        }

        private static void PatchApp(string inputModulePath, string outputModulePath)
        {
            // Throw back at setup if the file we looking for is not found
            if (!File.Exists(File2Patch))
            {
                SetupInstruction($@"Unable to locate {File2Patch} within that directory.");
            }

            // Check if the file is writable
            if (!WriteAccess(File2Patch)) TryElevatePrivilege("Insufficient permission to modify the file.");

            // Make a backup in case there is none
            try
            {
                if (!File.Exists(File2PatchBackup))
                {
                    Console.WriteLine($@"No backup file detected backing-up...");
                    File.Move(File2Patch, File2PatchBackup);
                }
                else Console.WriteLine("An backup is already existing, Skipping...");
            }
            catch
            {
                TryElevatePrivilege($"Could not make a backup at '{File2PatchBackup}'");
            }

            // Ask for a registration name
            Console.Write("Registration Name: ");
            _registrationName = Console.ReadLine();

            // Load module from backup file
            // TODO: Load/Write file without making any backup (without being memory dependent)
            var module = ModuleDefMD.Load(inputModulePath);

            // Loop true all the methods
            foreach (var type in module.GetTypes())
            {
                // Loop true methods
                foreach (var method in type.Methods)
                {
                    // Unlock features
                    // We are doing so by changing the ProductCode which is getting checked and unlocking features
                    // At NLClientApp.Core.dll NLClientApp.Core.ViewModels.MainVM::UpdateSupportedFeatures()
                    if (method.Name == "get_ProductCode")
                    {
                        Console.WriteLine($"Patching: {method.FullName}");
                        var methodInstr = method.Body.Instructions;

                        methodInstr.Clear();
                        methodInstr.Add(OpCodes.Ldstr.ToInstruction("nl4pro"));
                        methodInstr.Add(OpCodes.Ret.ToInstruction());
                    }

                    // Change license quantity to something more leet
                    if (method.Name == "get_Quantity")
                    {
                        Console.WriteLine($"Patching: {method.FullName}");
                        var methodInstr = method.Body.Instructions;

                        methodInstr.Clear();
                        methodInstr.Add(OpCodes.Ldc_I4.ToInstruction(1337));
                        methodInstr.Add(OpCodes.Ret.ToInstruction());
                    }

                    // Change default license type to enterprise
                    if (method.Name == "get_LicenseType")
                    {
                        Console.WriteLine($"Patching: {method.FullName}");
                        var methodInstr = method.Body.Instructions;

                        methodInstr.Clear();
                        methodInstr.Add(OpCodes.Ldc_I4_2.ToInstruction());
                        methodInstr.Add(OpCodes.Ret.ToInstruction());
                    }

                    // Change default license registration name
                    if (method.Name == "get_RegistrationName")
                    {
                        Console.WriteLine($"Patching: {method.FullName}");
                        var methodInstr = method.Body.Instructions;

                        methodInstr.Clear();
                        methodInstr.Add(OpCodes.Ldstr.ToInstruction(_registrationName));
                        methodInstr.Add(OpCodes.Ret.ToInstruction());
                    }

                    // Force IsRegistered
                    if (method.Name == "get_IsRegistered")
                    {
                        Console.WriteLine($"Patching: {method.FullName}");
                        var methodInstr = method.Body.Instructions;

                        methodInstr.Clear();
                        methodInstr.Add(OpCodes.Ldc_I4_1.ToInstruction());
                        methodInstr.Add(OpCodes.Ret.ToInstruction());
                    }

                    // Disable Expiration
                    if (method.Name == "get_HasExpiration" || method.Name == "get_IsExpired")
                    {
                        Console.WriteLine($"Patching: {method.FullName}");
                        var methodInstr = method.Body.Instructions;

                        methodInstr.Clear();
                        methodInstr.Add(OpCodes.Ldc_I4_0.ToInstruction());
                        methodInstr.Add(OpCodes.Ret.ToInstruction());
                    }
                }
            }

            // Finally save the module
            SaveModule(module, outputModulePath);
        }

        private static void AutomaticPatchApp()
        {
            PatchApp(File2PatchBackup, File2Patch);
        }

        private static void ManualPatchApp()
        {
            // PatchApp welcome message
            Console.Write("Application Path: ");

            // Parse path by console line
            var getLine = Console.ReadLine();
            var directoryName = Path.GetFullPath(getLine?.Replace("\"", ""));

            // In case the path is not a directory replace directoryName to the file directory name
            if (!Directory.Exists(directoryName)) directoryName = Path.GetDirectoryName(directoryName);

            // Patch the app
            PatchApp(File2PatchBackup, File2Patch);
        }

        private static void TryElevatePrivilege(string message = null)
        {
            Console.Clear();

            if (message != null) Console.WriteLine(message);
            Console.Write("Try to run the process with an higher privilege ?\n\n0: No\n1: Yes\n# ");
            var choice = Console.ReadKey().Key;

            switch (choice)
            {
                case ConsoleKey.D0:
                case ConsoleKey.NumPad0:
                    SetupInstruction();
                    break;
                case ConsoleKey.D1:
                case ConsoleKey.NumPad1:
                    if (ElevatePrivilege())
                    {
                        Console.Clear();

                        Console.WriteLine("Successfully elevated process privilege.\nLeaving current process in 3 seconds...");
                        Thread.Sleep(3000);
                        Environment.Exit(0);
                    }
                    else TryElevatePrivilege("Failed to elevate process privilege.");

                    break;
                default:
                    TryElevatePrivilege("Could not register your choice.");
                    break;
            }
        }

        /// <summary>
        /// Try to elevate process privilege
        /// </summary>
        /// <returns>BOOL</returns>
        private static bool ElevatePrivilege()
        {
            bool success = false;
            try
            {
                if (!new WindowsPrincipal(WindowsIdentity.GetCurrent())
                    .IsInRole(WindowsBuiltInRole.Administrator))
                {
                    var exeName = Process.GetCurrentProcess().MainModule?.FileName;
                    var startInfo = new ProcessStartInfo(exeName) {Verb = "runas"};
                    Process.Start(startInfo);
                    success = true;
                }
            }
            catch
            {
                // dont throw exception
            }

            return success;
        }

        /// <summary> Checks for write access for the given file.
        /// </summary>
        /// <param name="fileName">The filename.</param>
        /// <returns>true, if write access is allowed, otherwise false</returns>
        private static bool WriteAccess(string fileName)
        {
            if ((File.GetAttributes(fileName) & System.IO.FileAttributes.ReadOnly) != 0)
                return false;

            // Get the access rules of the specified files (user groups and user names that have access to the file)
            var rules = File.GetAccessControl(fileName).GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier));

            // Get the identity of the current user and the groups that the user is in.
            var groups = WindowsIdentity.GetCurrent().Groups;
            string sidCurrentUser = WindowsIdentity.GetCurrent().User.Value;

            // Check if writing to the file is explicitly denied for this user or a group the user is in.
            if (rules.OfType<FileSystemAccessRule>().Any(r =>
                (groups.Contains(r.IdentityReference) || r.IdentityReference.Value == sidCurrentUser) && r.AccessControlType == AccessControlType.Deny &&
                (r.FileSystemRights & FileSystemRights.WriteData) == FileSystemRights.WriteData))
                return false;

            // Check if writing is allowed
            return rules.OfType<FileSystemAccessRule>().Any(r =>
                (groups.Contains(r.IdentityReference) || r.IdentityReference.Value == sidCurrentUser) && r.AccessControlType == AccessControlType.Allow &&
                (r.FileSystemRights & FileSystemRights.WriteData) == FileSystemRights.WriteData);
        }
    }
}