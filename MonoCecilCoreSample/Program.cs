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
            // we are in ./MonoCecilCoreSample/bin/Debug/netcoreapp3.1/
            var subjectPath = @"../../../../MonoCecilCoreSample.Subject/bin/Debug/netcoreapp3.1/";
            Environment.CurrentDirectory = Path.GetFullPath(subjectPath);
            
            var subjectName = @"MonoCecilCoreSample.Subject";
            var subjectNamespace = @"MonoCecilCoreSample.Subject";

            var assembly = AssemblyDefinition.ReadAssembly(
                subjectName + ".dll",
                new ReaderParameters {
                    SymbolReaderProvider = new PortablePdbReaderProvider(),
                    ReadSymbols = true,
                    SymbolStream = File.Open(subjectName + ".pdb", FileMode.Open),
                    ReadWrite = false,
                    ReadingMode = ReadingMode.Immediate,
                }
            );

            var modifiedName = "Modified";
            assembly.Name = new AssemblyNameDefinition(modifiedName, new Version(1, 0, 0, 0));
            
            var module = assembly.MainModule;
            module.Name = modifiedName + ".dll";
            
            // create the program type and add it to the module
            var programType = new TypeDefinition(subjectNamespace,
                "ProgramModified",
                TypeAttributes.Class | TypeAttributes.Public,
                module.TypeSystem.Object);

            module.Types.Add(programType);

            // add an empty constructor
            var ctor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig
                                                   | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);

            // create the constructor's method body
            var il = ctor.Body.GetILProcessor();

            il.Append(il.Create(OpCodes.Ldarg_0));

            var runtimeFileName = @"c:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\3.1.0\ref\netcoreapp3.1\System.Runtime.dll";
            var runtimeDefinition = AssemblyDefinition.ReadAssembly(runtimeFileName);
            var systemObject = runtimeDefinition.MainModule.GetType(typeof(object).FullName);

            var systemObjectCtor = systemObject.GetConstructors().First();
            var systemObjectCtorRef = module.ImportReference(systemObjectCtor);
            il.Append(il.Create(OpCodes.Call, systemObjectCtorRef));

            il.Append(il.Create(OpCodes.Nop));
            il.Append(il.Create(OpCodes.Ret));

            programType.Methods.Add(ctor);

            // define the 'Main' method and add it to 'Program'
            var mainMethod = new MethodDefinition("MainModified",
                MethodAttributes.Public | MethodAttributes.Static,
                module.TypeSystem.Void);

            programType.Methods.Add(mainMethod);

            // add the 'args' parameter
            var stringArrayType = module.TypeSystem.String.MakeArrayType();
            var argParameterType = module.ImportReference(stringArrayType);
            var argsParameter = new ParameterDefinition("args",
                ParameterAttributes.None,
                argParameterType);

            mainMethod.Parameters.Add(argsParameter);

            // create the method body
            il = mainMethod.Body.GetILProcessor();
            
            il.Append(il.Create(OpCodes.Nop));
            il.Append(il.Create(OpCodes.Ldstr, "Hello Modified World!"));

            var consoleFileName = @"c:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\3.1.0\ref\netcoreapp3.1\System.Console.dll";
            var consoleDefinition = AssemblyDefinition.ReadAssembly(consoleFileName);

            var consoleWriteLine = consoleDefinition.MainModule.GetType(typeof(Console).FullName)
                .GetMethods()
                .FirstOrDefault(x => {
                    return string.Equals(x.Name, nameof(Console.WriteLine), StringComparison.InvariantCultureIgnoreCase) &&
                           x.Parameters.Count == 1 &&
                           string.Equals(x.Parameters[0].ParameterType.FullName, typeof(string).FullName, StringComparison.InvariantCultureIgnoreCase);
                });
            var consoleWriteLineRef = module.ImportReference(consoleWriteLine);

            var writeLineMethod = il.Create(OpCodes.Call, consoleWriteLineRef);
            
            // call the method
            il.Append(writeLineMethod);
            
            il.Append(il.Create(OpCodes.Nop));
            il.Append(il.Create(OpCodes.Ret));

            // use unmodified source for now
            var modifiedDocumentUrl = Path.GetFullPath("../../../Program.cs");
            mainMethod.DebugInformation.SequencePoints.Add(
                new SequencePoint(
                    writeLineMethod,
                    new Document(modifiedDocumentUrl)
                ) {
                    StartLine = 6,
                    EndLine = 6,
                    StartColumn = 13,
                    EndColumn = 46
                }
            );
            mainMethod.DebugInformation.Scope = new ScopeDebugInformation(il.Body.Instructions.First(), il.Body.Instructions.Last());
            
            var oldType = assembly.MainModule.GetType(subjectNamespace + ".Program");
            var oldDebugInfo = oldType
                .GetMethods()
                .First()
                .DebugInformation;
            
            mainMethod.DebugInformation.Scope.Import = oldDebugInfo.Scope.Import;
            
            // set the entry point and save the module
            assembly.EntryPoint = mainMethod;

            // ensure we referencing only ref assemblies
            var systemPrivateCoreLib = module.AssemblyReferences.FirstOrDefault(x => x.Name.StartsWith("System.Private.CoreLib", StringComparison.InvariantCultureIgnoreCase));
            Debug.Assert(systemPrivateCoreLib == null, "systemPrivateCoreLib == null");

            assembly.Write(
                modifiedName + ".dll",
                new WriterParameters {
                    SymbolStream = File.Create(modifiedName + ".pdb"),
                    WriteSymbols = true,
                    SymbolWriterProvider = new PortablePdbWriterProvider()
                }
            );

            var runtimeConfigJsonExt = ".runtimeConfig.json";
            File.Copy(subjectName + runtimeConfigJsonExt, modifiedName + runtimeConfigJsonExt, true);
        }
    }
}