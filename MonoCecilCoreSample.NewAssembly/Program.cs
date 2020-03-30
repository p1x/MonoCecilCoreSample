using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoCecilCoreSample.Shared;

namespace MonoCecilCoreSample.NewAssembly {
    public static class Program {
        public static void Main() {
            // Get reference assemblies pack version. We will use it later for loading reference assemblies.  
            var refVersion = SdkHelper.GetInstalledCoreRefsVersions().Last();
            
            var assemblyName = @"NewAssembly";
            var assemblyNamespace = @"NewAssembly";

            var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(assemblyName, new Version(1, 0, 0, 0)),
                assemblyName + ".dll",
                new ModuleParameters {
                    Architecture = TargetArchitecture.I386,
                    Kind = ModuleKind.Console,
                    Runtime = TargetRuntime.Net_4_0,
                }
            );
            var module = assembly.MainModule;
            
            // According to https://github.com/jbevain/cecil/issues/646#issuecomment-581165522
            // we create and add reference to 'System.Runtime' as early as possible
            var runtimeFileName = SdkHelper.GetCoreAssemblyPath(refVersion, "System.Runtime");
            var runtimeDefinition = AssemblyDefinition.ReadAssembly(runtimeFileName);
            module.AssemblyReferences.Add(runtimeDefinition.Name);

            // create the program type and add it to the module
            // use 'TypeSystem.Object' to not avoid implementation assemblies
            var programType = new TypeDefinition(
                assemblyNamespace,
                "Program",
                TypeAttributes.Class | TypeAttributes.Public,
                module.TypeSystem.Object
            );
            module.Types.Add(programType);

            // add an empty constructor
            // again, we use 'TypeSystem.Void'
            var ctor = new MethodDefinition(
                ".ctor",
                MethodAttributes.Public |
                MethodAttributes.HideBySig |
                MethodAttributes.SpecialName |
                MethodAttributes.RTSpecialName,
                module.TypeSystem.Void
            );

            // create the constructor's method body
            var il = ctor.Body.GetILProcessor();

            il.Append(il.Create(OpCodes.Ldarg_0));

            // get 'object' 'TypeDefinition' from 'System.Runtime' it by name
            // so we can get its methods without resolving it into implementation (from System.Private.CoreLib) 
            var systemObject = runtimeDefinition.MainModule.GetType(typeof(object).FullName);

            // get base ctor and call it
            var systemObjectCtor = systemObject.GetConstructors().First();
            var systemObjectCtorRef = module.ImportReference(systemObjectCtor);
            il.Append(il.Create(OpCodes.Call, systemObjectCtorRef));

            il.Append(il.Create(OpCodes.Nop));
            il.Append(il.Create(OpCodes.Ret));

            programType.Methods.Add(ctor);

            // define the new 'Main' method and add it to our new 'Program' type
            // once again, use 'TypeSystem' to avoid importing anything
            var mainMethod = new MethodDefinition(
                "Main",
                MethodAttributes.Public | MethodAttributes.Static,
                module.TypeSystem.Void
            );
            programType.Methods.Add(mainMethod);

            // construct string array type using 'TypeSystem' for 'args' parameter
            // and add it to the method
            var stringArrayType = module.TypeSystem.String.MakeArrayType();
            var argParameterType = module.ImportReference(stringArrayType);
            var argsParameter = new ParameterDefinition(
                "args",
                ParameterAttributes.None,
                argParameterType
            );
            mainMethod.Parameters.Add(argsParameter);

            // create the method body
            il = mainMethod.Body.GetILProcessor();
            
            il.Append(il.Create(OpCodes.Nop));
            il.Append(il.Create(OpCodes.Ldstr, "Hello World!"));

            // get path to 'System.Console' reference assembly and read it
            var consoleFileName = SdkHelper.GetCoreAssemblyPath(refVersion, "System.Console");
            var consoleDefinition = AssemblyDefinition.ReadAssembly(consoleFileName);

            // get 'System.Console' type and 'Console.WriteLine(string)' method and import it
            var consoleWriteLine = consoleDefinition.MainModule.GetType(typeof(Console).FullName)
                .GetMethods()
                .FirstOrDefault(x =>
                    string.Equals(x.Name, nameof(Console.WriteLine), StringComparison.InvariantCultureIgnoreCase) &&
                    x.Parameters.Count == 1 &&
                    string.Equals(x.Parameters[0].ParameterType.FullName, typeof(string).FullName, StringComparison.InvariantCultureIgnoreCase)
                );
            var consoleWriteLineRef = module.ImportReference(consoleWriteLine);
            
            // call the method
            var writeLineMethod = il.Create(OpCodes.Call, consoleWriteLineRef);
            il.Append(writeLineMethod);
            
            il.Append(il.Create(OpCodes.Nop));
            il.Append(il.Create(OpCodes.Ret));

            // add assembly attributes
            // add [assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
            var runtimeCompatibilityAttribute = runtimeDefinition.MainModule.GetType(
                typeof(System.Runtime.CompilerServices.RuntimeCompatibilityAttribute).FullName
            );
            assembly.CustomAttributes.Add(
                new CustomAttribute(
                    assembly.MainModule.ImportReference(
                        runtimeCompatibilityAttribute.GetConstructors().First(x => !x.HasParameters)
                    )
                ) {
                    Properties = {
                        new CustomAttributeNamedArgument(
                            nameof(System.Runtime.CompilerServices.RuntimeCompatibilityAttribute.WrapNonExceptionThrows),
                            new CustomAttributeArgument(
                                assembly.MainModule.TypeSystem.Boolean,
                                true
                            )
                        )
                    }
                }
            );
            
            // add [assembly: Debuggable(DebuggableAttribute.DebuggingModes.Default | DebuggableAttribute.DebuggingModes.DisableOptimizations | DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints | DebuggableAttribute.DebuggingModes.EnableEditAndContinue)]
            // Cecil uses different divider for nested types
            var debuggingModeTypeName = typeof(DebuggableAttribute.DebuggingModes).FullName.Replace('+', '/');
            assembly.CustomAttributes.Add(
                new CustomAttribute(
                    assembly.MainModule.ImportReference(
                        runtimeDefinition.MainModule.GetType(typeof(DebuggableAttribute).FullName)
                            .GetConstructors()
                            .First(x =>
                                x.Parameters.Count == 1 &&
                                x.Parameters[0].ParameterType.FullName == debuggingModeTypeName
                            )
                    )
                ) {
                    ConstructorArguments = {
                        new CustomAttributeArgument(
                            runtimeDefinition.MainModule.GetType(debuggingModeTypeName),
                            DebuggableAttribute.DebuggingModes.Default |
                            DebuggableAttribute.DebuggingModes.DisableOptimizations |
                            DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints
                        )
                    }
                }
            );
            // add [assembly: TargetFramework(".NETCoreApp,Version=v3.1")]
            assembly.CustomAttributes.Add(
                new CustomAttribute(
                    assembly.MainModule.ImportReference(
                        runtimeDefinition.MainModule
                            .GetType(typeof(System.Runtime.Versioning.TargetFrameworkAttribute).FullName)
                            .GetConstructors()
                            .First(x =>
                                x.Parameters.Count == 1 &&
                                x.Parameters[0].ParameterType.FullName == typeof(string).FullName
                            )
                    )
                ) {
                    ConstructorArguments = {
                        new CustomAttributeArgument(
                            assembly.MainModule.TypeSystem.String,
                            ".NETCoreApp,Version=v3.1"
                        )
                    }
                }
            );
            
            // create debug symbols:
            
            // define method scope
            // we will ignore SequencePoints and Document for now
            // <Method Token="6000001" LocalVariablesSignatureToken="11000000">...</Method> 
            mainMethod.DebugInformation.Scope = new ScopeDebugInformation(
                il.Body.Instructions.First(),
                il.Body.Instructions.Last()
            );
            
            // add import scopes, basically 'using System;' 
            // xml examples is from dotPeek symbols view
            // <ImportScope Index="1" Parent="null" />
            var rootImportScope = new ImportDebugInformation();
            
            /*
                <ImportScope Index="2" Parent="1">
                    <Import>System</Import>
                </ImportScope>
            */
            var documentImportScope = new ImportDebugInformation() {
                Parent = rootImportScope
            };
            documentImportScope.Targets.Add(
                new ImportTarget(ImportTargetKind.ImportNamespace) {
                    Namespace = "System"
                }
            );
            
            // <ImportScope Index="3" Parent="2" />
            mainMethod.DebugInformation.Scope.Import = new ImportDebugInformation {
                Parent = documentImportScope
            };
            
            // set the entry point
            assembly.EntryPoint = mainMethod;

            // ensure we referencing only ref assemblies
            var systemPrivateCoreLib = module.AssemblyReferences.FirstOrDefault(x => x.Name.StartsWith("System.Private.CoreLib", StringComparison.InvariantCultureIgnoreCase));
            Debug.Assert(systemPrivateCoreLib == null, "systemPrivateCoreLib == null");
            var mscorlib = module.AssemblyReferences.FirstOrDefault(x => x.Name.StartsWith("mscorlib", StringComparison.InvariantCultureIgnoreCase));
            Debug.Assert(mscorlib == null, "mscorlib == null");

            // save modified assembly and symbols to new file            
            assembly.Write(
                module.Name,
                new WriterParameters {
                    SymbolStream = File.Create(assemblyName + ".pdb"),
                    // write symbols 
                    WriteSymbols = true,
                    // net core uses portable pdb
                    SymbolWriterProvider = new PortablePdbWriterProvider()
                }
            );

            // don't forget to create runtime config json
            var runtimeConfigJsonExt = ".runtimeConfig.json";
            var runtimeConfigContent = @"{
  ""runtimeOptions"": {
    ""tfm"": ""netcoreapp3.1"",
    ""framework"": {
      ""name"": ""Microsoft.NETCore.App"",
      ""version"": ""3.1.0""
    }
  }
}";

            File.WriteAllText(assemblyName + runtimeConfigJsonExt, runtimeConfigContent);
            
            // done, we can run it using 'dotnet modified.dll'
            // or create exe using Microsoft.NET.HostModel.AppHost.HostWriter.CreateAppHost()
            // and path to '/AppHostTemplate/appHost.exe' template
        }
    }
}