using Il2CppInterop.Runtime;
using System;
using System.Runtime.InteropServices;
using Unity.Entities;

namespace SoloTweaker;

internal static class ECSExtensions
{
    static EntityManager EntityManager => SoloBuffLogic.GetServerWorld().EntityManager;

    public static unsafe void Write<T>(this Entity entity, T componentData) where T : struct
    {
        var ct = new ComponentType(Il2CppType.Of<T>());
        byte[] byteArray = StructureToByteArray(componentData);
        int size = Marshal.SizeOf<T>();
        fixed (byte* p = byteArray)
        {
            EntityManager.SetComponentDataRaw(entity, ct.TypeIndex, p, size);
        }
    }

    public static unsafe T Read<T>(this Entity entity) where T : struct
    {
        var ct = new ComponentType(Il2CppType.Of<T>());
        void* rawPointer = EntityManager.GetComponentDataRawRO(entity, ct.TypeIndex);
        return Marshal.PtrToStructure<T>(new IntPtr(rawPointer));
    }

    public static DynamicBuffer<T> ReadBuffer<T>(this Entity entity) where T : struct
    {
        return EntityManager.GetBuffer<T>(entity);
    }

    public static bool Has<T>(this Entity entity)
    {
        var ct = new ComponentType(Il2CppType.Of<T>());
        return EntityManager.HasComponent(entity, ct);
    }

    public static void Add<T>(this Entity entity)
    {
        var ct = new ComponentType(Il2CppType.Of<T>());
        EntityManager.AddComponent(entity, ct);
    }

    public static void Remove<T>(this Entity entity)
    {
        var ct = new ComponentType(Il2CppType.Of<T>());
        EntityManager.RemoveComponent(entity, ct);
    }

    static byte[] StructureToByteArray<T>(T structure) where T : struct
    {
        int size = Marshal.SizeOf(structure);
        byte[] byteArray = new byte[size];
        IntPtr ptr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(structure, ptr, true);
        Marshal.Copy(ptr, byteArray, 0, size);
        Marshal.FreeHGlobal(ptr);
        return byteArray;
    }
}
