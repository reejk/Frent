﻿using Frent.Collections;
using Frent.Core.Events;
using System.Diagnostics.CodeAnalysis;

namespace Frent.Core;

public readonly struct ComponentHandle : IEquatable<ComponentHandle>, IDisposable
{
    private readonly int _index;
    private readonly ComponentID _componentType;


    internal ComponentHandle(int index, ComponentID componentID)
    {
        _index = index;
        _componentType = componentID;
    }

    public static ComponentHandle Create<T>(in T comp)
    {
        return Component<T>.StoreComponent(comp);
    }

    public static ComponentHandle CreateFromBoxed(ComponentID typeAs, object @object)
    {
        var index = Component.ComponentTable[typeAs.RawIndex].Storage.CreateBoxed(@object);
        return new ComponentHandle(index, typeAs);
    }

    public static ComponentHandle CreateFromBoxed(object @object) => CreateFromBoxed(Component.GetComponentID(@object.GetType()), @object);

    /// <summary>
    /// Gets the value of this component strongly typed.
    /// </summary>
    /// <typeparam name="T">The type of the component. <see cref="Component{T}.ID"/> should be equal to <see cref="ComponentID"/></typeparam>
    /// <returns>The component value.</returns>
    public T Retrieve<T>()
    {
        if(_componentType != Component<T>.ID)
            FrentExceptions.Throw_InvalidOperationException("Wrong component handle type!");
        return Component<T>.GeneralComponentStorage.Take(_index);
    }

    /// <summary>
    /// Gets the value of the component represented bu this <see cref="ComponentHandle"/>, boxing if needed.
    /// </summary>
    /// <returns>The component value.</returns>
    public object RetrieveBoxed()
    {
        return Component.ComponentTable[_componentType.RawIndex].Storage.TakeBoxed(_index);
    }

    internal void InvokeComponentEventAndConsume(Entity entity, GenericEvent? @event)
    {
        Component.ComponentTable[_componentType.RawIndex].Storage.InvokeEventWithAndConsume(@event, entity, _index);
    }

    /// <summary>
    /// Frees the memory associated with this component handle and marks it for reuse.
    /// </summary>
    /// <remarks>It is very easy to leak memory by improperly disposing of <see cref="ComponentHandle"/> instances. The handle does not check for double disposes.</remarks>
    public void Dispose() => Component.ComponentTable[_componentType.RawIndex].Storage.Consume(_index);
    /// <summary>
    /// Checks if a <see cref="ComponentHandle"/> is equal to this handle and so points to the same component.
    /// </summary>
    /// <param name="other">The <see cref="ComponentHandle"/> to compare to.</param>
    /// <returns><see langword="true"/> when they are equal, <see langword="false"/> otherwise.</returns>
    public bool Equals(ComponentHandle other) => other.ComponentID == ComponentID && other.Index == Index;
    /// <summary>
    /// Checks if an object is equal to this component handle and points to the same component.
    /// </summary>
    /// <param name="obj">The object to check.</param>
    /// <returns><see langword="true"/> when they are equal, <see langword="false"/> otherwise.</returns>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ComponentHandle handle && Equals(handle);
    /// <summary>
    /// Checks if two component handles point to the same component.
    /// </summary>
    /// <param name="left">The first component handle.</param>
    /// <param name="right">The second component handle.</param>
    /// <returns><see langword="true"/> when they are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(ComponentHandle left, ComponentHandle right) => left.Equals(right);
    /// <summary>
    /// Checks if two component handles do not point to the same component.
    /// </summary>
    /// <param name="left">The first component handle.</param>
    /// <param name="right">The second component handle.</param>
    /// <returns><see langword="true"/> when they are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(ComponentHandle left, ComponentHandle right) => !left.Equals(right);

    /// <summary>
    /// The type of component represented by this <see cref="ComponentHandle"/>
    /// </summary>
    public Type Type => _componentType.Type;
    /// <summary>
    /// The <see cref="Core.ComponentID"/> of the component represented by this <see cref="ComponentHandle"/>
    /// </summary>
    public ComponentID ComponentID => _componentType;
    /// <summary>
    /// The hashcode.
    /// </summary>
    /// <returns>The hashcode -_-.</returns>
    public override int GetHashCode() => HashCode.Combine(_componentType, _index);
    internal int Index => _index;
    internal IDTable ParentTable => Component.ComponentTable[_componentType.RawIndex].Storage;
}
