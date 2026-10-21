using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace EKScriptLauncher.Revit
{
    /// <summary>
    /// Runs a .py file through pyRevit, the way a pyRevit ribbon button would,
    /// so scripts keep using `from pyrevit import revit, DB, forms`.
    ///
    /// 1. Preferred: pyRevit turns every button into a generated subclass of
    ///    PyRevitLabs.PyRevit.Runtime.ScriptCommand. We create one of those,
    ///    point its ScriptData at the chosen file and call its Execute, so the
    ///    script gets exactly the search paths and setup pyRevit's own buttons get.
    /// 2. Fallback: call pyRevit's ScriptExecutor.ExecuteScript directly with
    ///    pyrevitlib / site-packages as search paths.
    ///
    /// pyRevit is found by its ScriptCommand type, not by assembly name: the
    /// Python loader names the runtime "&lt;hash&gt;_PyRevitLabs.PyRevit.Runtime",
    /// the C# loader "pyRevitLabs.PyRevit.Runtime.&lt;year&gt;". Only reflection
    /// is used, so no pyRevit DLL is needed to build or load this add-in.
    /// </summary>
    static class PyRevitRunner
    {
        const string RuntimeNamespace = "PyRevitLabs.PyRevit.Runtime";
        const string ScriptCommandTypeName = RuntimeNamespace + ".ScriptCommand";

        public sealed class RunResult
        {
            public bool Started;
            public bool Succeeded;
            public string Problem = "";
        }

        public static RunResult Run(string scriptPath, ExternalCommandData commandData, ElementSet elements)
        {
            var result = new RunResult();

            if (!File.Exists(scriptPath))
            {
                result.Problem = "The script file no longer exists:\n\n" + scriptPath;
                return result;
            }

            var log = new List<string>();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            List<Type> baseTypes = FindScriptCommandTypes(assemblies);
            if (baseTypes.Count == 0)
            {
                result.Problem =
                    "pyRevit is not loaded in this Revit session, so the Python script can't run.\n\n" +
                    "Install pyRevit for this Revit version (or use pyRevit > Reload), then try again." +
                    Details(assemblies, log);
                return result;
            }
            log.Add("runtime: " + string.Join(", ", baseTypes.Select(t => SafeName(t.Assembly))));

            // 1. Borrow one of pyRevit's own generated button commands.
            try
            {
                object command = CreateDonorCommand(assemblies, log);
                if (command != null)
                {
                    PrepareDonor(command, scriptPath);
                    return ExecuteDonor(command, commandData, elements);
                }
            }
            catch (Exception ex)
            {
                log.Add("command: " + Unwrap(ex).Message);
            }

            // 2. Call pyRevit's script executor directly.
            foreach (Type baseType in baseTypes)
            {
                try
                {
                    return ExecuteDirect(baseType.Assembly, assemblies, scriptPath, commandData, elements);
                }
                catch (Exception ex)
                {
                    log.Add("executor: " + Unwrap(ex).Message);
                }
            }

            result.Problem = "pyRevit is loaded, but EK Script Launcher could not hand the script to it." +
                             Details(assemblies, log);
            return result;
        }

        // -- finding pyRevit ------------------------------------------------

        static List<Type> FindScriptCommandTypes(Assembly[] assemblies)
        {
            var found = new List<Type>();
            for (int i = assemblies.Length - 1; i >= 0; i--)
            {
                if (IsFrameworkAssembly(assemblies[i]))
                    continue;
                try
                {
                    Type type = assemblies[i].GetType(ScriptCommandTypeName, false);
                    if (type != null)
                        found.Add(type);
                }
                catch
                {
                }
            }
            return found;
        }

        static Type cachedDonorType;

        static object CreateDonorCommand(Assembly[] assemblies, List<string> log)
        {
            if (cachedDonorType != null)
            {
                object cached = TryCreate(cachedDonorType);
                if (cached != null && IsPythonCommand(cached))
                    return cached;
                cachedDonorType = null;
            }

            int commandTypes = 0;

            // Most recently loaded assemblies first (the current pyRevit session).
            for (int i = assemblies.Length - 1; i >= 0; i--)
            {
                Assembly assembly = assemblies[i];
                if (IsFrameworkAssembly(assembly))
                    continue;

                foreach (Type type in LoadableTypes(assembly))
                {
                    if (type.IsAbstract || !DerivesFromScriptCommand(type) || type.GetConstructor(Type.EmptyTypes) == null)
                        continue;

                    commandTypes++;
                    object instance = TryCreate(type);
                    if (instance != null && IsPythonCommand(instance))
                    {
                        cachedDonorType = type;
                        return instance;
                    }
                }
            }

            log.Add("pyRevit button commands found: " + commandTypes);
            return null;
        }

        static bool DerivesFromScriptCommand(Type type)
        {
            // Compared by name, so it also works if pyRevit was loaded more than once.
            for (Type t = type.BaseType; t != null; t = t.BaseType)
            {
                if (t.FullName == ScriptCommandTypeName)
                    return true;
            }
            return false;
        }

        static bool IsFrameworkAssembly(Assembly assembly)
        {
            string name = SafeName(assembly);
            return name.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Autodesk", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("RevitAPI", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("AdWindows", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Presentation", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("WindowsBase", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("EKScriptLauncher", StringComparison.OrdinalIgnoreCase) ||
                   name == "mscorlib" || name == "netstandard";
        }

        static IEnumerable<Type> LoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null);
            }
            catch
            {
                return Enumerable.Empty<Type>();
            }
        }

        static object TryCreate(Type type)
        {
            try
            {
                return Activator.CreateInstance(type);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>A donor command whose own script is Python, so its search paths include pyRevit's libraries.</summary>
        static bool IsPythonCommand(object command)
        {
            object data = GetMember(command, "ScriptData");
            var path = data == null ? null : GetMember(data, "ScriptPath") as string;
            return path != null && path.EndsWith(".py", StringComparison.OrdinalIgnoreCase);
        }

        // -- 1. donor command -----------------------------------------------

        static void PrepareDonor(object command, string scriptPath)
        {
            object data = Require(command, "ScriptData");
            object configs = Require(command, "ScriptRuntimeConfigs");

            FillScriptData(data, scriptPath);

            // The script's own folder first, so it can import modules next to it.
            string folder = Path.GetDirectoryName(scriptPath);
            var searchPaths = GetMember(configs, "SearchPaths") as IList;
            if (searchPaths != null && !string.IsNullOrEmpty(folder) && !searchPaths.Contains(folder))
                searchPaths.Insert(0, folder);

            var arguments = GetMember(configs, "Arguments") as IList;
            if (arguments != null)
                arguments.Clear();

            // Default engine options (no forced clean/persistent engine).
            SetMember(configs, "EngineConfigs", "{}");
        }

        static RunResult ExecuteDonor(object command, ExternalCommandData commandData, ElementSet elements)
        {
            MethodInfo execute = command.GetType().GetMethod(
                "Execute",
                new[] { typeof(ExternalCommandData), typeof(string).MakeByRefType(), typeof(ElementSet) });
            if (execute == null)
                throw new MissingMethodException(command.GetType().Name, "Execute");

            object outcome = execute.Invoke(command, new object[] { commandData, "", elements });

            // pyRevit reports script errors in its own output window and
            // returns Cancelled for them, so no second message is needed.
            return new RunResult
            {
                Started = true,
                Succeeded = outcome is Result && (Result)outcome == Result.Succeeded,
            };
        }

        // -- 2. direct executor ---------------------------------------------

        static RunResult ExecuteDirect(
            Assembly runtime, Assembly[] assemblies, string scriptPath,
            ExternalCommandData commandData, ElementSet elements)
        {
            Type dataType = RequireType(runtime, "ScriptData");
            Type configsType = RequireType(runtime, "ScriptRuntimeConfigs");
            Type executorType = RequireType(runtime, "ScriptExecutor");

            object data = Activator.CreateInstance(dataType);
            FillScriptData(data, scriptPath);

            var searchPaths = new List<string>();
            string folder = Path.GetDirectoryName(scriptPath);
            if (!string.IsNullOrEmpty(folder))
                searchPaths.Add(folder);
            string root = FindPyRevitRoot(assemblies);
            if (root != null)
            {
                foreach (string sub in new[] { "pyrevitlib", "site-packages" })
                {
                    string path = Path.Combine(root, sub);
                    if (Directory.Exists(path))
                        searchPaths.Add(path);
                }
            }

            object configs = Activator.CreateInstance(configsType);
            SetMember(configs, "CommandData", commandData);
            SetMember(configs, "SelectedElements", elements);
            SetMember(configs, "SearchPaths", searchPaths);
            SetMember(configs, "Arguments", new List<string>());
            SetMember(configs, "EngineConfigs", "{}");
            SetMember(configs, "ExecutedFromUI", true);

            MethodInfo execute = executorType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "ExecuteScript")
                .FirstOrDefault(m =>
                {
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length >= 2 && p[0].ParameterType == dataType && p[1].ParameterType == configsType;
                });
            if (execute == null)
                throw new MissingMethodException(executorType.Name, "ExecuteScript");

            ParameterInfo[] parameters = execute.GetParameters();
            var args = new object[parameters.Length];
            args[0] = data;
            args[1] = configs;
            for (int i = 2; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;
                args[i] = parameters[i].HasDefaultValue && parameters[i].DefaultValue != null
                    ? parameters[i].DefaultValue
                    : type.IsValueType ? Activator.CreateInstance(type) : TryCreate(type);
            }

            object code = execute.Invoke(null, args);
            int value = code is int ? (int)code : -1;

            // 14 = executor not initialised: pyRevit hasn't finished loading.
            if (value == 14)
                throw new InvalidOperationException("pyRevit's script executor is not initialised");

            return new RunResult { Started = true, Succeeded = value == 0 };
        }

        static string FindPyRevitRoot(Assembly[] assemblies)
        {
            foreach (Assembly assembly in assemblies)
            {
                if (assembly.IsDynamic || SafeName(assembly).IndexOf("pyrevit", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string location;
                try
                {
                    location = assembly.Location;
                }
                catch
                {
                    continue;
                }
                if (string.IsNullOrEmpty(location))
                    continue;

                string dir = Path.GetDirectoryName(location);
                for (int depth = 0; depth < 6 && !string.IsNullOrEmpty(dir); depth++)
                {
                    if (Directory.Exists(Path.Combine(dir, "pyrevitlib")))
                        return dir;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            return null;
        }

        // -- shared ---------------------------------------------------------

        static void FillScriptData(object data, string scriptPath)
        {
            string name = Core.ScriptCatalog.DisplayName(scriptPath);

            SetMember(data, "ScriptPath", scriptPath, required: true);
            SetMember(data, "ConfigScriptPath", scriptPath);
            SetMember(data, "CommandName", name);
            SetMember(data, "CommandBundle", Path.GetFileName(scriptPath));
            SetMember(data, "CommandExtension", "EK Script Launcher");
            SetMember(data, "CommandUniqueId", "ek-script-launcher-" + name.ToLowerInvariant().Replace(' ', '-'));
            SetMember(data, "CommandControlId", "ek-script-launcher");
            SetMember(data, "CommandContext", "");
            SetMember(data, "HelpSource", "");
            SetMember(data, "Tooltip", "");
        }

        static string Details(Assembly[] assemblies, List<string> log)
        {
            var names = assemblies
                .Select(SafeName)
                .Where(n => n.IndexOf("pyrevit", StringComparison.OrdinalIgnoreCase) >= 0)
                .Distinct()
                .Take(8)
                .ToList();

            var lines = new List<string>(log);
            lines.Add("pyRevit assemblies: " + (names.Count > 0 ? string.Join(", ", names) : "none"));
            return "\n\nDetails:\n" + string.Join("\n", lines);
        }

        // -- reflection helpers ---------------------------------------------

        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public;

        static Type RequireType(Assembly assembly, string name)
        {
            Type type = assembly.GetType(RuntimeNamespace + "." + name, false);
            if (type == null)
                throw new TypeLoadException(name + " not found in pyRevit runtime");
            return type;
        }

        static object GetMember(object target, string name)
        {
            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(name, Instance);
            if (property != null && property.CanRead)
                return property.GetValue(target, null);
            FieldInfo field = type.GetField(name, Instance);
            return field != null ? field.GetValue(target) : null;
        }

        static object Require(object target, string name)
        {
            object value = GetMember(target, name);
            if (value == null)
                throw new MissingMemberException(target.GetType().Name, name);
            return value;
        }

        static void SetMember(object target, string name, object value, bool required = false)
        {
            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(name, Instance);
            if (property != null && property.CanWrite && property.PropertyType.IsInstanceOfType(value))
            {
                property.SetValue(target, value, null);
                return;
            }
            FieldInfo field = type.GetField(name, Instance);
            if (field != null && field.FieldType.IsInstanceOfType(value))
            {
                field.SetValue(target, value);
                return;
            }
            if (required)
                throw new MissingMemberException(type.Name, name);
        }

        static string SafeName(Assembly assembly)
        {
            try
            {
                return assembly.GetName().Name ?? "";
            }
            catch
            {
                return "";
            }
        }

        static Exception Unwrap(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null)
                ex = ex.InnerException;
            return ex;
        }
    }
}
