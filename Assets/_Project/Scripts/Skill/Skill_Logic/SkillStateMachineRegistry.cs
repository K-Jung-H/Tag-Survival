using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class SkillStateMachineRegistry
{
    private static readonly Dictionary<string, ConstructorInfo> constructorsByKey = new(StringComparer.Ordinal);
    private static bool isBuilt;

    // - Role: Create a skill state machine from definition logic key.
    public static SkillStateMachine Create(SkillDefinition definition)
    {
        if (definition == null)
        {
            return null;
        }

        string logicKey = definition.LogicKey;
        if (string.IsNullOrWhiteSpace(logicKey))
        {
            Debug.LogWarning($"[SkillStateMachineRegistry] LogicKey is empty. skillId={definition.SkillId}");
            return null;
        }

        BuildIfNeeded();
        if (!constructorsByKey.TryGetValue(logicKey, out ConstructorInfo constructor))
        {
            Debug.LogWarning(
                $"[SkillStateMachineRegistry] LogicKey '{logicKey}' is not registered. skillId={definition.SkillId}");
            return null;
        }

        try
        {
            return (SkillStateMachine)constructor.Invoke(new object[] { definition });
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"[SkillStateMachineRegistry] Failed to create state machine. logicKey={logicKey}, type={constructor.DeclaringType.Name}, error={exception.Message}");
            return null;
        }
    }

    // - Role: Build state machine registry once.
    private static void BuildIfNeeded()
    {
        if (isBuilt)
        {
            return;
        }

        isBuilt = true;
        constructorsByKey.Clear();

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            RegisterAssembly(assemblies[i]);
        }
    }

    // - Role: Register skill state machine types from one assembly.
    private static void RegisterAssembly(Assembly assembly)
    {
        if (assembly == null)
        {
            return;
        }

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types;
        }

        if (types == null)
        {
            return;
        }

        for (int i = 0; i < types.Length; i++)
        {
            RegisterType(types[i]);
        }
    }

    // - Role: Register one state machine type.
    private static void RegisterType(Type type)
    {
        if (type == null
            || type.IsAbstract
            || !typeof(SkillStateMachine).IsAssignableFrom(type))
        {
            return;
        }

        SkillLogicAttribute attribute = type.GetCustomAttribute<SkillLogicAttribute>();
        if (attribute == null || string.IsNullOrWhiteSpace(attribute.LogicKey))
        {
            return;
        }

        ConstructorInfo constructor = type.GetConstructor(new[] { typeof(SkillDefinition) });
        if (constructor == null)
        {
            Debug.LogWarning(
                $"[SkillStateMachineRegistry] {type.Name} has SkillLogicAttribute but no SkillDefinition constructor.");
            return;
        }

        if (constructorsByKey.ContainsKey(attribute.LogicKey))
        {
            Debug.LogWarning(
                $"[SkillStateMachineRegistry] Duplicate logicKey '{attribute.LogicKey}' found. Type {type.Name} ignored.");
            return;
        }

        constructorsByKey.Add(attribute.LogicKey, constructor);
    }
}
