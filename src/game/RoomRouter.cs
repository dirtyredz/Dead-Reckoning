using System.Collections.Generic;
using Chicken.Utilities;
using UnityEngine;

namespace DeadReckoning
{
    /// <summary>
    /// Cross-room routing: given the player's current room and a target room, returns the world
    /// position of the door (in the current scene) to head toward to reach that room. Handles
    /// multi-room houses by BFS over the game's room-adjacency graph (NavigationLibrary).
    ///
    /// We steer at the door rather than the NPC's raw NavPosition because NavPosition lives in a
    /// per-room offset nav space, not the current world — pointing at it directly aims nowhere.
    /// </summary>
    internal static class RoomRouter
    {
        /// <summary>World position of the door heading toward the FIRST room in <paramref name="rooms"/>
        /// (list order) that we can route to from <paramref name="current"/>, or null if none is
        /// reachable. Used for a multi-room place target — any of its rooms will do.</summary>
        internal static Vector3? DoorTowardFirstReachable(RoomAsset current, List<RoomAsset> rooms)
        {
            if (rooms == null) return null;
            for (int i = 0; i < rooms.Count; i++)
            {
                Vector3? door = DoorToward(current, rooms[i]);
                if (door.HasValue) return door;
            }
            return null;
        }

        /// <summary>World position of the door heading toward <paramref name="target"/>, or null.</summary>
        internal static Vector3? DoorToward(RoomAsset current, RoomAsset target)
        {
            if (current == null || target == null) return null;
            if (!AddressableLibrary<NavigationLibrary>.Exists) return null;
            NavigationLibrary nav = AddressableLibrary<NavigationLibrary>.Instance;

            // Direct exit into the target room.
            Vector3? direct = DoorLeadingTo(nav, target);
            if (direct.HasValue) return direct;

            // Multi-hop: find the first room to step into, then the door into that room.
            RoomAsset firstHop = BfsFirstHop(nav, current, target);
            if (firstHop != null)
            {
                Vector3? hop = DoorLeadingTo(nav, firstHop);
                if (hop.HasValue) return hop;
            }
            return null;
        }

        /// <summary>A loaded door in this scene whose far side is <paramref name="room"/>.</summary>
        private static Vector3? DoorLeadingTo(NavigationLibrary nav, RoomAsset room)
        {
            List<RoomSwitch> all = RoomSwitch.All;
            for (int i = 0; i < all.Count; i++)
            {
                RoomSwitch s = all[i];
                if (s == null || s.Door == null || s.TargetLocation == null) continue;
                if (nav.GetRoomAsset(s.TargetLocation) == room)
                    return s.Door.transform.position;
            }
            return null;
        }

        /// <summary>BFS the room graph from <paramref name="current"/> to <paramref name="target"/>;
        /// returns the first room to step into along the way.</summary>
        private static RoomAsset BfsFirstHop(NavigationLibrary nav, RoomAsset current, RoomAsset target)
        {
            NavigationLibrary.RoomData start = nav.GetRoomData(current);
            if (start == null) return null;

            var visited = new HashSet<RoomAsset> { current };
            var queue = new Queue<KeyValuePair<NavigationLibrary.RoomData, RoomAsset>>();

            foreach (NavigationLibrary.RoomSwitchData sw in start.RoomSwitches)
            {
                NavigationLibrary.RoomData nd = nav.GetRoomData(sw.TargetLocationEntityGuid);
                if (nd == null || nd.RoomAsset == null || !visited.Add(nd.RoomAsset)) continue;
                queue.Enqueue(new KeyValuePair<NavigationLibrary.RoomData, RoomAsset>(nd, nd.RoomAsset));
            }

            while (queue.Count > 0)
            {
                KeyValuePair<NavigationLibrary.RoomData, RoomAsset> item = queue.Dequeue();
                if (item.Key.RoomAsset == target) return item.Value;
                foreach (NavigationLibrary.RoomSwitchData sw in item.Key.RoomSwitches)
                {
                    NavigationLibrary.RoomData nd = nav.GetRoomData(sw.TargetLocationEntityGuid);
                    if (nd == null || nd.RoomAsset == null || !visited.Add(nd.RoomAsset)) continue;
                    queue.Enqueue(new KeyValuePair<NavigationLibrary.RoomData, RoomAsset>(nd, item.Value));
                }
            }
            return null;
        }
    }
}
