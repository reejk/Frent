﻿using Frent.Collections;
using Frent.Core;
using Frent.Core.Structures;
using Frent.Updating;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Frent;

partial class World
{
    /*  
     *  This file contains all core functions related to structual changes on the world
     *  The only core structual change function not here is the normal create function, since it needs to be source generated
     *  These functions take all the data it needs, with no validation that an entity is alive
     */
    /// <summary>
    /// Note - This function DOES NOT invoke events, as it is also used for command buffer entity creation
    /// </summary>
    internal void AddComponentRange(Entity entity, ReadOnlySpan<(ComponentID Component, int Index)> comps)
    {
        EntityLocation location = EntityTable[entity.EntityID].Location;
        Archetype currentArchetype = location.Archetype(this);

        ReadOnlySpan<ComponentID> existingComponentIDs = currentArchetype.ArchetypeTypeArray.AsSpan();
        int newCompCount = comps.Length + existingComponentIDs.Length;
        if ((uint)newCompCount > 16)
            FrentExceptions.Throw_InvalidOperationException("Too many components");

        Span<ComponentID> allComps = stackalloc ComponentID[newCompCount];
        existingComponentIDs.CopyTo(allComps);
        int j = 0;
        for (int i = existingComponentIDs.Length; i < comps.Length; i++)
            allComps[i] = comps[j++].Component;
        var tags = currentArchetype.ArchetypeTagArray;

        var destination = Archetype.CreateOrGetExistingArchetype(allComps, tags.AsSpan(), this, null, tags);
        destination.CreateEntityLocation(location.Flags, out var nextELoc).Init(entity);

        for (int i = 0; i < currentArchetype.Components.Length; i++)
        {
            destination.Components[i].PullComponentFromAndDelete(currentArchetype.Components[i], nextELoc.Index, location.Index);
        }

        j = 0;
        for (int i = existingComponentIDs.Length; i < currentArchetype.Components.Length; i++)
        {
            var componentLocation = comps[j++];
            currentArchetype.Components[i].PullComponentFrom(
                Component.ComponentTable[componentLocation.Component.ID].Stack,
                nextELoc.Index,
                componentLocation.Index);
        }


        EntityIDOnly movedDown = currentArchetype.DeleteEntityFromStorage(location.Index);

        EntityTable[movedDown.ID].Location = location;
        EntityTable[entity.EntityID].Location = nextELoc;
    }

    //Add
    //Note: this fucntion doesn't actually do the last step of setting the component in the new archetype
    //the caller's job is to set the component
    [SkipLocalsInit]
    internal IComponentRunner AddComponent(Entity entity, EntityLocation entityLocation, ComponentID component, out EntityLocation nextLocation)
    {
        Archetype from = entityLocation.Archetype(this);

        Archetype? destination;
        uint key = CompAddLookup.GetKey(component.ID, entityLocation.ArchetypeID);
        int index = CompAddLookup.LookupIndex(key);
        if(index != 32)
        {
            destination = CompAddLookup.Archetypes.UnsafeArrayIndex(index);
        }
        else if(!CompAddLookup.FallbackLookup.TryGetValue(key, out destination))
        {
            destination = from.FindArchetypeAdjacentAdd(this, component);
        }

        destination.CreateEntityLocation(entityLocation.Flags, out nextLocation).Init(entity);

        IComponentRunner[] fromRunners = from.Components;
        IComponentRunner[] toRunners = destination.Components;

        int i = 0;
        for (; i < fromRunners.Length; i++)
        {
            toRunners.UnsafeArrayIndex(i).PullComponentFromAndDelete(fromRunners[i], nextLocation.Index, entityLocation.Index);
        }

        EntityIDOnly movedDown = from.DeleteEntityFromStorage(entityLocation.Index);

        EntityTable.UnsafeIndexNoResize(movedDown.ID).Location = entityLocation;
        EntityTable.UnsafeIndexNoResize(entity.EntityID).Location = nextLocation;

        return toRunners.UnsafeArrayIndex(i);
    }

    //Remove
    [SkipLocalsInit]
    internal void RemoveComponent(Entity entity, EntityLocation entityLocation, ComponentID component)
    {
        Archetype from = entityLocation.Archetype(this);

        Archetype? destination;
        uint key = CompRemoveLookup.GetKey(component.ID, entityLocation.ArchetypeID);
        int index = CompRemoveLookup.LookupIndex(key);
        if (index != 32)
        {
            destination = CompRemoveLookup.Archetypes.UnsafeArrayIndex(index);
        }
        else if (!CompRemoveLookup.FallbackLookup.TryGetValue(key, out destination))
        {
            destination = from.FindArchetypeAdjacentRemove(this, component);
        }

        destination.CreateEntityLocation(entityLocation.Flags, out EntityLocation nextLocation).Init(entity);

        int skipIndex = from.ComponentTagTable.UnsafeArrayIndex(component.ID);

        int j = 0;

        TrimmableStack? tmpEventComponentStorage = null;
        int tmpEventComponentIndex = -1;

        var destinationComponents = destination.Components;
        for (int i = 0; i < from.Components.Length; i++)
        {
            if (i == skipIndex)
            {
                if (entityLocation.HasEvent(EntityFlags.GenericRemoveComp))
                {
                    from.Components.UnsafeArrayIndex(i).PushComponentToStack(entityLocation.Index, out tmpEventComponentIndex);
                }
                continue;
            }
            destinationComponents.UnsafeArrayIndex(j++).PullComponentFromAndDelete(from.Components.UnsafeArrayIndex(i), nextLocation.Index, entityLocation.Index);
        }

        EntityIDOnly movedDown = from.DeleteEntityFromStorage(entityLocation.Index);

        EntityTable.UnsafeIndexNoResize(movedDown.ID).Location = entityLocation;
        EntityTable.UnsafeIndexNoResize(entity.EntityID).Location = nextLocation;

        entityLocation.Flags |= WorldEventFlags;
        if(entityLocation.HasEvent(EntityFlags.RemoveComp | EntityFlags.GenericRemoveComp | EntityFlags.WorldRemoveComp))
        {
            ComponentRemovedEvent.Invoke(entity, component);
            ref var eventData = ref CollectionsMarshal.GetValueRefOrNullRef(EventLookup, entity.EntityIDOnly);
            eventData.Remove.NormalEvent.Invoke(entity, component);
            tmpEventComponentStorage?.InvokeEventWith(eventData.Remove.GenericEvent, entity, tmpEventComponentIndex);
        }
    }

    //Delete
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void DeleteEntity(Entity entity, EntityLocation entityLocation)
    {
        EntityFlags check = entityLocation.Flags | WorldEventFlags;
        if ((check & EntityFlags.AllEvents) != 0)
            InvokeDeleteEvents(entity, entityLocation);
        DeleteEntityWithoutEvents(entity, entityLocation);
    }

    //let the jit decide whether or not to inline
    private void InvokeDeleteEvents(Entity entity, EntityLocation entityLocation)
    {
        EntityDeletedEvent.Invoke(entity);
        if (entityLocation.HasEvent(EntityFlags.OnDelete))
        {
            foreach (var @event in EventLookup[entity.EntityIDOnly].Delete.AsSpan())
            {
                @event.Invoke(entity);
            }
        }
        EventLookup.Remove(entity.EntityIDOnly);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void DeleteEntityWithoutEvents(Entity entity, EntityLocation entityLocation)
    {
        //entity is guaranteed to be alive here
        EntityIDOnly replacedEntity = entityLocation.Archetype(this).DeleteEntity(entityLocation.Index);
        EntityTable[replacedEntity.ID] = new(entityLocation, replacedEntity.Version);
        EntityTable[entity.EntityID].Version = ushort.MaxValue;

        int nextVersion = entity.EntityVersion + 1;
        if (nextVersion != ushort.MaxValue)
        {
            //can't use max value as an ID, as it is used as a default value
            _recycledEntityIds.Push() = new EntityIDOnly(entity.EntityID, (ushort)(nextVersion));
        }
    }

    //Tag
    internal bool Tag(Entity entity, EntityLocation entityLocation, TagID tagID)
    {
        if (GlobalWorldTables.HasTag(entityLocation.ArchetypeID, tagID))
            return false;

        Archetype from = entityLocation.Archetype(this);

        ref var destination = ref CollectionsMarshal.GetValueRefOrAddDefault(ArchetypeGraphEdges,
            ArchetypeEdgeKey.Tag(tagID, entityLocation.ArchetypeID, ArchetypeEdgeType.AddTag),
            out bool exist);

        if (!exist)
        {
            destination = Archetype.CreateOrGetExistingArchetype(from.ArchetypeTypeArray.AsSpan(), MemoryHelpers.Concat(from.ArchetypeTagArray, tagID, out var res), this, from.ArchetypeTypeArray, res);
        }

        destination!.CreateEntityLocation(entityLocation.Flags, out var nextLocation).Init(entity);

        Debug.Assert(from.Components.Length == destination.Components.Length);
        Span<IComponentRunner> fromRunners = from.Components.AsSpan();
        Span<IComponentRunner> toRunners = destination.Components.AsSpan()[..fromRunners.Length];//avoid bounds checks

        for (int i = 0; i < fromRunners.Length; i++)
            toRunners[i].PullComponentFromAndDelete(fromRunners[i], nextLocation.Index, entityLocation.Index);

        EntityIDOnly movedDown = from.DeleteEntityFromStorage(entityLocation.Index);

        EntityTable[movedDown.ID].Location = entityLocation;
        EntityTable[entity.EntityID].Location = nextLocation;

        ref var eventData = ref TryGetEventData(entityLocation, entity.EntityIDOnly, EntityFlags.Tagged, out bool eventExist);
        if (eventExist)
            eventData.Tag.Invoke(entity, tagID);

        Tagged.Invoke(entity, tagID);

        return true;
    }

    //Detach
    internal bool Detach(Entity entity, EntityLocation entityLocation, TagID tagID)
    {
        if (!GlobalWorldTables.HasTag(entityLocation.ArchetypeID, tagID))
            return false;

        Archetype from = entityLocation.Archetype(this);
        ref var destination = ref CollectionsMarshal.GetValueRefOrAddDefault(ArchetypeGraphEdges,
            ArchetypeEdgeKey.Tag(tagID, from.ID, ArchetypeEdgeType.RemoveTag),
            out bool exist);

        if (!exist)
        {
            destination = Archetype.CreateOrGetExistingArchetype(from.ArchetypeTypeArray.AsSpan(), MemoryHelpers.Remove(from.ArchetypeTagArray, tagID, out var arr), this, from.ArchetypeTypeArray, arr);
        }

        destination!.CreateEntityLocation(entityLocation.Flags, out var nextLocation).Init(entity);

        Debug.Assert(from.Components.Length == destination.Components.Length);
        Span<IComponentRunner> fromRunners = from.Components.AsSpan();
        Span<IComponentRunner> toRunners = destination.Components.AsSpan()[..fromRunners.Length];//avoid bounds checks

        for (int i = 0; i < fromRunners.Length; i++)
            toRunners[i].PullComponentFromAndDelete(fromRunners[i], nextLocation.Index, entityLocation.Index);

        EntityIDOnly movedDown = from.DeleteEntityFromStorage(entityLocation.Index);
        
        EntityTable[movedDown.ID].Location = entityLocation;
        EntityTable[entity.EntityID].Location = nextLocation;


        ref var eventData = ref TryGetEventData(entityLocation, entity.EntityIDOnly, EntityFlags.Detach, out bool eventExist);
        if (eventExist)
            eventData.Detach.Invoke(entity, tagID);

        Detached.Invoke(entity, tagID);

        return true;
    }
}