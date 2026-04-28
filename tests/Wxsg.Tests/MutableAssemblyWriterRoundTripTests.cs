using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Obfuscar.Metadata.Mutable;

namespace Wxsg.Tests;

/// <summary>
/// Verifies that MutableAssemblyWriter round-trips an assembly that contains a
/// foreach loop over a generic Dictionary without corrupting struct (Enumerator)
/// type metadata — the scenario that caused TypeLoadException in WpfDesign.Designer.
/// </summary>
public class MutableAssemblyWriterRoundTripTests
{
    [Fact]
    public void RoundTrip_DictionaryForeach_DoesNotCorruptEnumeratorStruct()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "wxsg_roundtrip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var asmPath = Path.Combine(tempDir, "RoundTripTest.dll");
        var rewrittenPath = Path.Combine(tempDir, "RoundTripTest_rw.dll");

        try
        {
            EmitTestAssembly(asmPath);

            // Round-trip through MutableAssemblyReader / Writer (no changes)
            var bytes = File.ReadAllBytes(asmPath);
            var reader = new MutableAssemblyReader();
            var assembly = reader.Read(new MemoryStream(bytes), new MutableReaderParameters { ReadMethodBodies = true });
            assembly.MainModule.FileName = rewrittenPath;

            var writer = new MutableAssemblyWriter(assembly);
            writer.Write(rewrittenPath);

            // Load in an isolated context and invoke the method
            var ctx = new AssemblyLoadContext("RoundTripTest_" + Guid.NewGuid(), isCollectible: true);
            Assembly loaded;
            try
            {
                loaded = ctx.LoadFromAssemblyPath(rewrittenPath);
            }
            catch (Exception ex)
            {
                Assert.Fail($"Failed to load round-tripped assembly: {ex}");
                return;
            }

            var type = loaded.GetType("RoundTripTest.DictIterator");
            Assert.NotNull(type);
            var method = type!.GetMethod("IterateAndSum", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(method);

            Exception? invokeEx = null;
            object? result = null;
            try
            {
                result = method!.Invoke(null, null);
            }
            catch (TargetInvocationException tie) { invokeEx = tie.InnerException ?? tie; }
            catch (Exception ex) { invokeEx = ex; }

            ctx.Unload();

            if (invokeEx != null)
                Assert.Fail($"Round-tripped assembly threw {invokeEx.GetType().Name}: {invokeEx.Message}\n{invokeEx}");

            Assert.Equal(6, result);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static void EmitTestAssembly(string outputPath)
    {
        var asmName = new AssemblyName("RoundTripTest");
        // PersistedAssemblyBuilder is the .NET 9+ save-capable builder
        var asmBuilder = new PersistedAssemblyBuilder(asmName, typeof(object).Assembly);
        var modBuilder = asmBuilder.DefineDynamicModule("RoundTripTest");
        var typeBuilder = modBuilder.DefineType("RoundTripTest.DictIterator",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Abstract);

        // public static int IterateAndSum() — does foreach over Dictionary<string,int>
        var methodBuilder = typeBuilder.DefineMethod("IterateAndSum",
            MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);

        var il = methodBuilder.GetILGenerator();
        var dictType = typeof(Dictionary<string, int>);
        var kvpType = typeof(KeyValuePair<string, int>);
        // Dictionary<string,int>.Enumerator — the value type whose struct layout must survive round-trip
        var enumType = dictType.GetNestedType("Enumerator")!.MakeGenericType(typeof(string), typeof(int));

        var dictLocal = il.DeclareLocal(dictType);
        var sumLocal  = il.DeclareLocal(typeof(int));
        var enumLocal = il.DeclareLocal(enumType);
        var pairLocal = il.DeclareLocal(kvpType);

        il.Emit(OpCodes.Newobj, dictType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, dictLocal);

        foreach (var (key, val) in new[] { ("a", 1), ("b", 2), ("c", 3) })
        {
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, key);
            il.Emit(OpCodes.Ldc_I4, val);
            il.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item")!);
        }

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, sumLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumLocal);

        var loopStart = il.DefineLabel();
        var loopEnd   = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumLocal);
        il.Emit(OpCodes.Call, enumType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        il.Emit(OpCodes.Ldloca, enumLocal);
        il.Emit(OpCodes.Call, enumType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, pairLocal);

        il.Emit(OpCodes.Ldloc, sumLocal);
        il.Emit(OpCodes.Ldloca, pairLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, sumLocal);

        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        il.Emit(OpCodes.Ldloc, sumLocal);
        il.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
        asmBuilder.Save(outputPath);
    }
}
