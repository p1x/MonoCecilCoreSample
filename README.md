# MonoCecilCoreSample
This sample shows how to create or modify .Net Core assembly and symbols files (.pdb) with Mono.Cecil.

The main goal was to use reference assemblies (e.g. System.Runtime) instead of implementations assemblies (e.g. System.Private.CoreLib).

Also it shows how to modify debug information for emmited code.

* MonoCecilCoreSample.NewAssembly - creating new assembly.
* MonoCecilCoreSample.ExistedAssembly - modifying existed assembly.
* MonoCecilCoreSample.Subject - simple assemby for modifying.

PS Make sure to build the MonoCecilCoreSample.Subject project before run.
