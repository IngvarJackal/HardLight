using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.Shuttles.Save;
using Content.Shared._NF.Shipyard.Components;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Robust.Shared.Log;

namespace Content.Server.Shuttles.Save
{
    public sealed class ShipSaveSystem : EntitySystem
    {
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;
        [Dependency] private readonly IAdminManager _adminManager = default!;

        // Static caches for admin ship save interactions
        private static readonly Dictionary<string, Action<string>> PendingAdminRequests = new();
        private static readonly Dictionary<string, List<(string filename, string shipName, DateTime timestamp)>> PlayerShipCache = new();

        public override void Initialize()
        {
            base.Initialize();
            SubscribeNetworkEvent<RequestSaveShipServerMessage>(OnRequestSaveShipServer);
            SubscribeNetworkEvent<RequestLoadShipMessage>(OnRequestLoadShip);
            SubscribeNetworkEvent<RequestAvailableShipsMessage>(OnRequestAvailableShips);
            SubscribeNetworkEvent<AdminSendPlayerShipsMessage>(OnAdminSendPlayerShips);
            SubscribeNetworkEvent<AdminSendShipDataMessage>(OnAdminSendShipData);
        }

        private void OnRequestSaveShipServer(RequestSaveShipServerMessage msg, EntitySessionEventArgs args)
        {
            var playerSession = args.SenderSession;
            if (playerSession == null)
                return;

            var deedUid = new EntityUid((int)msg.DeedUid);
            // Only save the grid referenced by the shuttle deed. Do NOT fall back to the player's current grid / station.
            if (!_entityManager.TryGetComponent<ShuttleDeedComponent>(deedUid, out var deed) || deed.ShuttleUid == null ||
                !_entityManager.TryGetEntity(deed.ShuttleUid.Value, out var shuttleNetUid))
            {
                Logger.Warning($"Player {playerSession.Name} attempted ship save without a valid shuttle deed / shuttle reference on ID {deedUid}");
                return;
            }

            // VRS: validate the requester actually possesses the deed entity (or is admin).
            // The shipyard console flow goes through ShipyardConsoleSaveMessage and is authenticated by the
            // ID slot + access reader; this bare network handler is reachable from any client and previously
            // accepted any EntityUid, which would let a malicious client save (and post-save destroy) any
            // ship by guessing/learning its deed EntityUid. Possession-by-parent-chain mirrors the intended
            // trust model (whoever holds the ID can save the ship).
            if (!IsAuthorizedForDeed(playerSession, deedUid))
            {
                Logger.Warning($"Player {playerSession.Name} attempted ship save on deed {deedUid} they do not possess (and are not admin); rejecting.");
                return;
            }

            var gridToSave = shuttleNetUid.Value;
            if (!_entityManager.HasComponent<MapGridComponent>(gridToSave))
            {
                Logger.Warning($"Player {playerSession.Name} deed shuttle {gridToSave} is not a grid");
                return;
            }

            var shipName = deed.ShuttleName ?? $"SavedShip_{DateTime.Now:yyyyMMdd_HHmmss}";

            var shipyardGridSaveSystem = _entitySystemManager.GetEntitySystem<Content.Server._NF.Shipyard.Systems.ShipyardGridSaveSystem>();
            Logger.Info($"Player {playerSession.Name} is saving deed-referenced ship {shipName} (grid {gridToSave})");
            var result = shipyardGridSaveSystem.TrySaveGridAsTrackedShip(gridToSave, deedUid, shipName, playerSession);
            if (result == Content.Server._NF.Shipyard.Systems.ShipyardGridSaveSystem.TrackedShipSaveResult.Queued)
            {
                Logger.Info($"Queued tracked ship save for {shipName}");
            }
            else if (result == Content.Server._NF.Shipyard.Systems.ShipyardGridSaveSystem.TrackedShipSaveResult.AlreadyInProgress)
            {
                Logger.Info($"Skipped duplicate tracked ship save request for {shipName}");
            }
            else
            {
                Logger.Error($"Failed to save ship {shipName}");
            }
        }
        public void RequestSaveShip(EntityUid deedUid, ICommonSession? playerSession)
        {
            if (playerSession == null)
            {
                Logger.Warning($"Attempted to save ship for deed {deedUid} without a valid player session.");
                return;
            }

            if (!_entityManager.TryGetComponent<ShuttleDeedComponent>(deedUid, out var deedComponent))
            {
                Logger.Warning($"Player {playerSession.Name} tried to save ship with invalid deed UID: {deedUid}");
                return;
            }

            if (deedComponent.ShuttleUid == null || !_entityManager.TryGetEntity(deedComponent.ShuttleUid.Value, out var shuttleUid) || !_entityManager.TryGetComponent<MapGridComponent>(shuttleUid.Value, out var grid))
            {
                Logger.Warning($"Player {playerSession.Name} tried to save ship with deed {deedUid} but no valid shuttle UID found.");
                return;
            }

            // Integrate with ShipyardGridSaveSystem for ship saving functionality
            var shipName = deedComponent.ShuttleName ?? "SavedShip_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // Get the ShipyardGridSaveSystem and use it to save the ship
            var shipyardGridSaveSystem = _entitySystemManager.GetEntitySystem<Content.Server._NF.Shipyard.Systems.ShipyardGridSaveSystem>();

            Logger.Info($"Player {playerSession.Name} is saving ship {shipName} via ShipyardGridSaveSystem");

            // Save the ship using the tracked grid-based system so cleanup only happens after the client writes the file.
            var result = shipyardGridSaveSystem.TrySaveGridAsTrackedShip(shuttleUid.Value, deedUid, shipName, playerSession);
            if (result == Content.Server._NF.Shipyard.Systems.ShipyardGridSaveSystem.TrackedShipSaveResult.Queued)
            {
                Logger.Info($"Queued tracked ship save for {shipName}");
            }
            else if (result == Content.Server._NF.Shipyard.Systems.ShipyardGridSaveSystem.TrackedShipSaveResult.AlreadyInProgress)
            {
                Logger.Info($"Skipped duplicate tracked ship save request for {shipName}");
            }
            else
            {
                Logger.Error($"Failed to save ship {shipName}");
            }
        }

        private void OnRequestLoadShip(RequestLoadShipMessage msg, EntitySessionEventArgs args)
        {
            var playerSession = args.SenderSession;
            if (playerSession == null)
                return;

            Logger.Info($"Player {playerSession.Name} requested to load ship from YAML data");

            // TODO: Implement ship loading from saved files
            // This would involve deserializing the ship data and spawning it in the game world
            // For now, we just log the request
        }

        private void OnRequestAvailableShips(RequestAvailableShipsMessage msg, EntitySessionEventArgs args)
        {
            var playerSession = args.SenderSession;
            if (playerSession == null)
                return;

            // Client handles available ships from local user data
            Logger.Info($"Player {playerSession.Name} requested available ships - client handles this locally");
        }

        private void OnAdminSendPlayerShips(AdminSendPlayerShipsMessage msg, EntitySessionEventArgs args)
        {
            var key = $"player_ships_{msg.AdminName}";
            if (PendingAdminRequests.TryGetValue(key, out var callback))
            {
                // Cache the ship data for later commands
                PlayerShipCache[key] = msg.Ships;

                var result = $"=== Ships for player ===\n\n";
                for (int i = 0; i < msg.Ships.Count; i++)
                {
                    var (filename, shipName, timestamp) = msg.Ships[i];
                    result += $"[{i + 1}] {shipName} ({filename})\n";
                    result += $"    Saved: {timestamp:yyyy-MM-dd HH:mm:ss}\n";
                    result += "\n";
                }
                callback(result);
                PendingAdminRequests.Remove(key);
            }
        }

        private void OnAdminSendShipData(AdminSendShipDataMessage msg, EntitySessionEventArgs args)
        {
            var key = $"ship_data_{msg.AdminName}_{msg.ShipFilename}";
            if (PendingAdminRequests.TryGetValue(key, out var callback))
            {
                callback(msg.ShipData);
                PendingAdminRequests.Remove(key);
            }
        }

        public static void RegisterAdminRequest(string key, Action<string> callback)
        {
            PendingAdminRequests[key] = callback;
        }

        public void SendAdminRequestPlayerShips(Guid playerId, string adminName, ICommonSession targetSession)
        {
            RaiseNetworkEvent(new AdminRequestPlayerShipsMessage(playerId, adminName), targetSession);
        }

        public void SendAdminRequestShipData(string filename, string adminName, ICommonSession targetSession)
        {
            RaiseNetworkEvent(new AdminRequestShipDataMessage(filename, adminName), targetSession);
        }

        /// <summary>
        /// VRS: returns true if <paramref name="session"/> is allowed to save the ship referenced by
        /// <paramref name="deedUid"/>. Admins are always allowed (preserves admin saveship tooling and
        /// the ability to force a save even when the admin is nowhere near the deed). Otherwise the
        /// session's attached entity must be an ancestor (in the transform/container chain) of the deed
        /// entity — i.e. the player physically carries the ID with the deed on it.
        /// </summary>
        private bool IsAuthorizedForDeed(ICommonSession session, EntityUid deedUid)
        {
            if (_adminManager.HasAdminFlag(session, AdminFlags.Admin))
                return true;

            if (session.AttachedEntity is not { } player || !_entityManager.EntityExists(player))
                return false;

            // Walk up the parent chain from the deed entity. If we reach the player's attached
            // entity, the deed (or its container, e.g. an ID card in a PDA in their belt) is on them.
            var current = deedUid;
            var safety = 0;
            while (safety++ < 32 && _entityManager.TryGetComponent<TransformComponent>(current, out var xform))
            {
                if (current == player)
                    return true;

                var parent = xform.ParentUid;
                if (!parent.IsValid() || parent == current)
                    return false;

                current = parent;
            }

            return false;
        }
    }
}
