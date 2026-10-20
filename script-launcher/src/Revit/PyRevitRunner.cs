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
    /// Runs a .py file through pyRevit, exactly the way a pyRevit ribbon button
    /// would, so scripts keep using `from pyrevit import revit, DB, forms`.
    ///
    /// pyRevit turns every button into a generated subclass of
    /// PyRevitLabs.PyRevit.Runtime.ScriptCommand. We create one of those
    /// commands, point its ScriptData at the chosen file, add the file's folder
    /// to its search paths and call its Execute. pyRevit then supplies
    /// __revit__, __file__, its libraries and its output window as usual.
    /// Only reflection is used, so no pyRevit DLL is needed to build or load
    /// this add-in.
    /// </summary>
    static class PyRevitRunner
    {
        const string ScriptCommandTypeName = "PyRevitLabs.PyRevit.Runtime.ScriptCommand";
        const string RuntimeAssemblyPrefix = "pyRevitLabs.PyRevit.Runtime";

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

            object command;
            try
            {
                command = CreateCommand();
            }
            catch (Exception ex)
            {
                result.Problem = "pyRevit could not prepare the script.\n\n" + Unwrap(ex).Message;
                return result;
            }

            if (command == null)
            {
                result.Problem =
                    "pyRevit is not loaded in this Revit session, so the Python script can't run.\n\n" +
                    "Install pyRevit for this Revit version (or use pyRevit > Reload), then try again.";
                return result;
            }

            try
            {
                Prepare(command, scriptPath);
            }
            catch (Exception ex)
            {
                result.Problem =
                    "This pyRevit version is not supported by EK Script Launcher.\n\n" + Unwrap(ex).Message;
                return result;
            }

            MethodInfo execute = command.GetType().GetMethod(
                "Execute",
                new[] { typeof(ExternalCommandData), typeof(string).MakeByRefType(), typeof(ElementSet) });
            if (execute == null)
            {
                result.Problem = "This pyRevit version is not supported by EK Script Launcher (no Execute method).";
                return result;
            }

            var args = new object[] { commandData, "", elements };
            try
            {
                object outcome = execute.Invoke(command, args);
                result.Started = true;
                // pyRevit reports script errors in its own output window and
                // returns Cancelled for them, so no second message is needed.
                result.Succeeded = outcome is Result && (Result)outcome == Result.Succeeded;
            }
            catch (Exception ex)
            {
                result.Problem = "pyRevit failed while running the script.\n\n" + Unwrap(ex).Message;
            }

            return result;
        }

        // -- finding a pyRevit command --------------------------------------

        static Type cachedCommandType;

        static object CreateCommand()
        {
            if (cachedCommandType != null)
            {
                try
                {
                    object cached = Activator.CreateInstance(cachedCommandType);
                    if (IsPythonCommand(cached))
                        return cached;
                }
                catch
                {
                }
                cachedCommandType = null;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            Type baseType = null;
            Assembly runtimeAssembly = null;
            for (int i = assemblies.Length - 1; i >= 0 && baseType == null; i--)
            {
                if (!SafeName(assemblies[i]).StartsWith(RuntimeAssemblyPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                baseType = assemblies[i].GetType(ScriptCommandTypeName, false);
                runtimeAssembly = assemblies[i];
            }
            if (baseType == null)
                return null;

            // Generated command assemblies reference the pyRevit runtime; look
            // at the most recently loaded ones first (the current pyRevit session).
            for (int i = assemblies.Length - 1; i >= 0; i--)
            {
                Assembly assembly = assemblies[i];
                if (assembly == runtimeAssembly || !MayContainCommands(assembly))
                    continue;

                foreach (Type type in LoadableTypes(assembly))
                {
                    if (type.IsAbstract || !baseType.IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null)
                        continue;

                    object instance;
                    try
                    {
                        instance = Activator.CreateInstance(type);
                    }
                    catch
                    {
                        continue;
                    }

                    if (IsPythonCommand(instance))
                    {
                        cachedCommandType = type;
                        return instance;
                    }
                }
            }

            return null;
        }

        static bool MayContainCommands(Assembly assembly)
        {
            if (assembly.IsDynamic)
                return true;

            string name = SafeName(assembly);
            if (name.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Autodesk", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Revit", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Presentation", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Windows", StringComparison.OrdinalIgnoreCase) ||
                name == "mscorlib" || name == "netstandard")
                return false;

            try
            {
                return assembly.GetReferencedAssemblies().Any(
                    r => r.Name.StartsWith(RuntimeAssemblyPrefix, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return true;
            }
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

        /// <summary>A donor command whose own script is Python, so its search paths include pyRevit's libraries.</summary>
        static bool IsPythonCommand(object command)
        {
            object data = GetMember(command, "ScriptData");
            var path = data == null ? null : GetMember(data, "ScriptPath") as string;
            return path != null && path.EndsWith(".py", StringComparison.OrdinalIgnoreCase);
        }

        // -- pointing it at our script --------------------------------------

        static void Prepare(object command, string scriptPath)
        {
            object data = Require(command, "ScriptData");
            object configs = Require(command, "ScriptRuntimeConfigs");

            string folder = Path.GetDirectoryName(scriptPath);
            string name = ScriptCatalogName(scriptPath);

            SetMember(data, "ScriptPath", scriptPath);
            SetMember(data, "ConfigScriptPath", scriptPath);
            SetMember(data, "CommandName", name);
            SetMember(data, "CommandBundle", Path.GetFileName(scriptPath));
            SetMember(data, "CommandExtension", "EK Script Launcher");
            SetMember(data, "CommandUniqueId", "ek-script-launcher-" + name.ToLowerInvariant().Replace(' ', '-'));
            SetMember(data, "CommandContext", "");
            SetMember(data, "HelpSource", "");
            SetMember(data, "Tooltip", "");

            // The script's own folder first, so it can import modules next to it.
            var searchPaths = GetMember(configs, "SearchPaths") as IList;
            if (searchPaths != null && !string.IsNullOrEmpty(folder) && !searchPaths.Contains(folder))
                searchPaths.Insert(0, folder);

            var arguments = GetMember(configs, "Arguments") as IList;
            if (arguments != null)
                arguments.Clear();

            // Default engine options (no forced clean/persistent engine).
            SetMember(configs, "EngineConfigs", "{}", optional: true);
        }

        static string ScriptCatalogName(string path)
        {
            return Core.ScriptCatalog.DisplayName(path);
        }

        // -- reflection helpers ---------------------------------------------

        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public;

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

        static void SetMember(object target, string name, object value, bool optional = false)
        {
            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(name, Instance);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value, null);
                return;
            }
            FieldInfo field = type.GetField(name, Instance);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }
            if (!optional && name == "ScriptPath")
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
