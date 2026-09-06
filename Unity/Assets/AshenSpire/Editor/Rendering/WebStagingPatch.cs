// WebStagingPatch.cs — bounded workaround for Unity 6000.6.0f1 staging allocation.
// Called only on the temporary player assembly by WebStagingBuildProcessor.
// Consolidate before sizing; allow two alignment elements per remaining range.
// Keep this implementation and its tests together when reviewing an Editor upgrade.
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace AshenSpire.Editor.Rendering
{
    public static class WebStagingPatch
    {
        public const string Marker = "AshenSpireStagingCapacity";
        public const string TypeName = "UnityEngine.UIElements.UIR.GpuUpdaterStaged";

        public static void Apply(ModuleDefinition module)
        {
            var type = module.Types.SingleOrDefault(value => value.FullName == TypeName)
                ?? throw new InvalidDataException("Expected Unity staged renderer is missing.");
            if (type.Methods.Any(method => method.Name == Marker))
                throw new InvalidDataException("Renderer is already patched; require a fresh player assembly.");
            var complete = type.Methods.Single(method => method.Name == "CompleteUpdate");
            var prepare = type.Methods.Single(method => method.Name == "PrepareCopyRanges");
            var instructions = complete.Body.Instructions;
            var dirtyReads = instructions.Where(i => i.Operand is MethodReference method && method.Name == "get_totalDirtyCount").ToArray();
            if (dirtyReads.Length != 1)
                throw new InvalidDataException("Expected one unpatched dirty-count allocation.");
            var dirtyRead = dirtyReads[0];
            if (!(dirtyRead.Next?.Operand is MethodReference allocation) || allocation.Name != "FindOrAllocateBuffer")
                throw new InvalidDataException("Renderer allocation order changed; review the workaround.");
            var dirtyCount = (MethodReference)dirtyRead.Operand;
            var body = prepare.Body.Instructions;
            if (body.Count < 4 || body[0].OpCode != OpCodes.Ldarg_1 || body[1].OpCode != OpCodes.Ldc_R4 ||
                (float)body[1].Operand != .9f || !(body[2].Operand is MethodReference consolidate) || consolidate.Name != "ConsolidateRanges")
                throw new InvalidDataException("Renderer consolidation order changed; review the workaround.");
            var ranges = body.Select(i => i.Operand).OfType<FieldReference>().First(field => field.Name == "dirtyRanges");
            var rangeCount = body.Select(i => i.Operand).OfType<MethodReference>().First(method => method.Name == "get_Count");
            if (dirtyCount.ReturnType.MetadataType != MetadataType.UInt32 || rangeCount.ReturnType.MetadataType != MetadataType.Int32)
                throw new InvalidDataException("Renderer count types changed.");

            var capacity = new MethodDefinition(Marker, MethodAttributes.Private | MethodAttributes.Static, module.TypeSystem.Int32);
            capacity.Parameters.Add(new ParameterDefinition("dataSet", ParameterAttributes.None, dirtyCount.DeclaringType));
            var il = capacity.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_R4, .9f);
            il.Emit(OpCodes.Callvirt, consolidate);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, dirtyCount);
            il.Emit(OpCodes.Conv_Ovf_I4_Un);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ranges);
            il.Emit(OpCodes.Callvirt, rangeCount);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Mul_Ovf);
            il.Emit(OpCodes.Add_Ovf);
            il.Emit(OpCodes.Ret);
            type.Methods.Add(capacity);
            // The receiver already on the stack becomes the helper's single argument.
            dirtyRead.OpCode = OpCodes.Call;
            dirtyRead.Operand = capacity;
        }
    }
}
