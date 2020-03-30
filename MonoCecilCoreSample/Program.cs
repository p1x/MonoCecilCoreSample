using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace MonoCecilCoreSample {
    public static class Program {
        public static void Main() {
            // Get reference assemblies pack version. We will use it later for loading reference assemblies.  
            var refVersion = SdkHelper.GetInstalledCoreRefsVersions().Last();
            
            // We are in ./MonoCecilCoreSample/bin/Debug/netcoreapp3.1/
            var subjectPath = @"../../../../MonoCecilCoreSample.Subject/bin/Debug/netcoreapp3.1/";
            Environment.CurrentDirectory = Path.GetFullPath(subjectPath);
            
            var subjectName = @"MonoCecilCoreSample.Subject";
            var subjectNamespace = @"MonoCecilCoreSample.Subject";

            // read subject assembly with symbols
            var assembly = AssemblyDefinition.ReadAssembly(
                subjectName + ".dll",
                new ReaderParameters {
                    // netcore uses portable pdb, so we provide appropriate reader
                    SymbolReaderProvider = new PortablePdbReaderProvider(),
                    // read symbols
                    ReadSymbols = true,
                    SymbolStream = File.Open(subjectName + ".pdb", FileMode.Open),
                    // we will write to another file, so we don't need this
                    ReadWrite = false,
                    // read everything at once
                    ReadingMode = ReadingMode.Immediate,
                }
            );

            // rename the assembly
            var modifiedName = "Modified";
            assembly.Name = new AssemblyNameDefinition(modifiedName, new Version(1, 0, 0, 0));
            
            // rename the main (and only) module
            var module = assembly.MainModule;
            module.Name = modifiedName + ".dll";
            
            // create the program type and add it to the module
            // use 'TypeSystem.Object' to not avoid implementation assemblies
            var programType = new TypeDefinition(
                subjectNamespace,
                "ProgramModified",
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

            // get path to 'System.Runtime' reference assembly and obtain 'object' 'TypeDefinition' from it by name
            var runtimeFileName = SdkHelper.GetCoreAssemblyPath(refVersion, "System.Runtime");
            var runtimeDefinition = AssemblyDefinition.ReadAssembly(runtimeFileName);
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
                "MainModified",
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
            il.Append(il.Create(OpCodes.Ldstr, "Hello Modified World!"));

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

            // edit debug symbols:
            
            // create document entry for our new method
            // use unmodified source for now
            var modifiedDocumentUrl = Path.GetFullPath("../../../Program.cs");
            var document = new Document(modifiedDocumentUrl);

            // add sequence point with the document and some position in source text
            mainMethod.DebugInformation.SequencePoints.Add(
                new SequencePoint(
                    writeLineMethod,
                    document
                ) {
                    StartLine = 6,
                    EndLine = 6,
                    StartColumn = 13,
                    EndColumn = 46
                }
            );
            
            // add scope for thr method, it could contain local variables name if we had any
            mainMethod.DebugInformation.Scope = new ScopeDebugInformation(il.Body.Instructions.First(), il.Body.Instructions.Last());
            
            // use existed import scope from, it works fine at least for now
            var oldType = assembly.MainModule.GetType(subjectNamespace + ".Program");
            var oldDebugInfo = oldType
                .GetMethods()
                .First()
                .DebugInformation;
            mainMethod.DebugInformation.Scope.Import = oldDebugInfo.Scope.Import;
            
            // set the entry point
            assembly.EntryPoint = mainMethod;

            // ensure we referencing only ref assemblies
            var systemPrivateCoreLib = module.AssemblyReferences.FirstOrDefault(x => x.Name.StartsWith("System.Private.CoreLib", StringComparison.InvariantCultureIgnoreCase));
            Debug.Assert(systemPrivateCoreLib == null, "systemPrivateCoreLib == null");

            // save modified assembly and symbols to new file            
            assembly.Write(
                modifiedName + ".dll",
                new WriterParameters {
                    SymbolStream = File.Create(modifiedName + ".pdb"),
                    // write symbols 
                    WriteSymbols = true,
                    // net core uses portable pdb
                    SymbolWriterProvider = new PortablePdbWriterProvider()
                }
            );

            // don't forget to create runtime config json
            // or copy existed and modify if necessary 
            var runtimeConfigJsonExt = ".runtimeConfig.json";
            File.Copy(subjectName + runtimeConfigJsonExt, modifiedName + runtimeConfigJsonExt, true);
            
            // done, we can run it using 'dotnet modified.dll'
            // or create exe using Microsoft.NET.HostModel.AppHost.HostWriter.CreateAppHost()
            // and path to '/AppHostTemplate/appHost.exe' template
        }
    }
}